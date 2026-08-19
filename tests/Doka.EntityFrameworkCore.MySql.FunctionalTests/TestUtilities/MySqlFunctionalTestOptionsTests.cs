using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Infrastructure;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the internal-service-provider ownership policy for the complete
/// functional-test assembly.
/// </summary>
public sealed class MySqlFunctionalTestOptionsTests
{
    private static readonly string[] s_expectedRawBuilderSites =
    [
        "Infrastructure/MySqlOptionsRegistrationTests.cs:Generic_builder_overload_returns_the_same_builder_instance",
        "Infrastructure/MySqlOptionsRegistrationTests.cs:Generic_data_source_builder_overload_returns_the_same_builder_instance",
        "Infrastructure/MySqlOptionsRegistrationTests.cs:Repeated_UseMySql_calls_keep_a_single_extension_instance",
        "Infrastructure/MySqlOptionsRegistrationTests.cs:Repeated_UseMySql_calls_replace_the_connection_path_consistently",
        "Migrations/MySqlMigrationOperationHandlerTests.cs:CreateContext",
        "Specification/CrossCutting/Infrastructure/MySqlLoggingSpecificationTests.cs:CreateOptionsBuilder",
        "Specification/Query/RelationalModelQueryMySqlTests.cs:CreateContext",
        "Specification/Update/TransactionMySqlTest.cs:CreateContextWithConnectionString",
        "TestUtilities/MySqlFunctionalTestOptions.cs:CreateTransientBuilder",
        "TestUtilities/MySqlFunctionalTestOptionsTests.cs:Seeding_context_uses_the_transient_provider_policy",
        "TestUtilities/MySqlFunctionalTestOptionsTests.cs:Transient_provider_policy_is_narrow_and_does_not_cache_providers",
    ];

    private static readonly string[] s_expectedNonTransientUseMySqlSites =
    [
        "Infrastructure/MySqlOptionsRegistrationTests.cs:Generic_builder_overload_returns_the_same_builder_instance",
        "Infrastructure/MySqlOptionsRegistrationTests.cs:Generic_data_source_builder_overload_returns_the_same_builder_instance",
        "Infrastructure/MySqlOptionsRegistrationTests.cs:Repeated_UseMySql_calls_keep_a_single_extension_instance",
        "Infrastructure/MySqlOptionsRegistrationTests.cs:Repeated_UseMySql_calls_replace_the_connection_path_consistently",
        "Specification/CrossCutting/Infrastructure/MySqlInfrastructureSpecificationTests.cs:CreateTestOptions",
        "Specification/TestUtilities/MySqlTestHelpers.cs:UseProviderOptions",
        "Specification/TestUtilities/MySqlTestStore.cs:AddProviderOptions",
    ];

    /// <summary>
    /// Verifies that isolated providers remain transient while only their
    /// expected process-wide threshold diagnostic is relaxed.
    /// </summary>
    [Fact]
    public void Transient_provider_policy_is_narrow_and_does_not_cache_providers()
    {
        var optionsBuilder = new DbContextOptionsBuilder()
            .ConfigureWarnings(warnings => warnings.Default(WarningBehavior.Throw))
            .UseTransientInternalServiceProvider();

        MySqlTestHelpers.Instance.UseProviderOptions(optionsBuilder);

        using var firstContext = new DbContext(optionsBuilder.Options);
        using var secondContext = new DbContext(optionsBuilder.Options);
        var firstProvider = ((IInfrastructure<IServiceProvider>)firstContext).Instance;
        var secondProvider = ((IInfrastructure<IServiceProvider>)secondContext).Instance;
        var loggingOptions = firstProvider.GetRequiredService<ILoggingOptions>();

        Assert.NotSame(firstProvider, secondProvider);
        Assert.Equal(
            WarningBehavior.Log,
            loggingOptions.WarningsConfiguration.GetBehavior(CoreEventId.ManyServiceProvidersCreatedWarning));
        Assert.Equal(
            WarningBehavior.Throw,
            loggingOptions.WarningsConfiguration.GetBehavior(CoreEventId.DetachedLazyLoadingWarning));
    }

    /// <summary>
    /// Keeps the shared seeding specification's expected model-validation error
    /// from being replaced by a process-wide service-provider threshold warning.
    /// </summary>
    [Fact]
    public void Seeding_context_uses_the_transient_provider_policy()
    {
        var optionsBuilder =
            new DbContextOptionsBuilder().ConfigureWarnings(warnings => warnings.Default(WarningBehavior.Throw));

        SeedingMySqlTest.ConfigureTransientOptions(
            optionsBuilder,
            "Server=127.0.0.1;Database=doka_seeding_options;User ID=root;Password=root_password;",
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)));

        var coreOptions = optionsBuilder.Options.FindExtension<CoreOptionsExtension>();

        Assert.NotNull(coreOptions);
        Assert.False(coreOptions.ServiceProviderCachingEnabled);
        Assert.Equal(
            WarningBehavior.Log,
            coreOptions.WarningsConfiguration.GetBehavior(CoreEventId.ManyServiceProvidersCreatedWarning));
        Assert.Equal(
            WarningBehavior.Throw,
            coreOptions.WarningsConfiguration.GetBehavior(CoreEventId.DetachedLazyLoadingWarning));
    }

    /// <summary>
    /// Prevents a new raw options builder from silently acquiring ownership of
    /// an internal provider without choosing transient, explicit, or fixture ownership.
    /// </summary>
    [Fact]
    public void Raw_options_builder_sites_have_explicit_provider_ownership()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "tests", "Doka.EntityFrameworkCore.MySql.FunctionalTests");
        var actual = EnumerateSourceTrees(projectRoot)
            .SelectMany(item => item
                .Root
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Where(node => node
                    .Type
                    .ToString()
                    .StartsWith("DbContextOptionsBuilder", StringComparison.Ordinal))
                .Select(node => FormattableString.Invariant(
                    $"{item.RelativePath}:{node.Ancestors().OfType<MethodDeclarationSyntax>().First().Identifier.ValueText}")))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var expected = s_expectedRawBuilderSites
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"Expected raw builder sites:{Environment.NewLine}{string.Join(Environment.NewLine, expected)}"
            + $"{Environment.NewLine}Actual raw builder sites:{Environment.NewLine}"
            + string.Join(Environment.NewLine, actual));
    }

    /// <summary>
    /// Prevents context-owned provider configuration from bypassing the
    /// transient policy through an <c>OnConfiguring</c> override.
    /// </summary>
    [Fact]
    public void On_configuring_provider_paths_use_the_transient_policy()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "tests", "Doka.EntityFrameworkCore.MySql.FunctionalTests");
        var unprotected = EnumerateSourceTrees(projectRoot)
            .SelectMany(item => item
                .Root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == "OnConfiguring")
                .Where(method => !method
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(invocation => invocation
                            .Expression
                            .ToString()
                            .EndsWith(".UseTransientInternalServiceProvider", StringComparison.Ordinal)
                        || invocation.Expression.ToString() == "ConfigureTransientOptions"))
                .Select(method => FormattableString.Invariant(
                    $"{item.RelativePath}:{method.GetLocation().GetLineSpan().StartLinePosition.Line + 1}")))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unprotected);
    }

    /// <summary>
    /// Requires every provider registration without a local transient policy
    /// to remain in the reviewed options-only or fixture-owned inventory.
    /// </summary>
    [Fact]
    public void UseMySql_sites_without_a_transient_policy_have_registered_owners()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "tests", "Doka.EntityFrameworkCore.MySql.FunctionalTests");
        var actual = EnumerateSourceTrees(projectRoot)
            .SelectMany(item => item
                .Root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(invocation => Invokes(invocation, "UseMySql")))
                .Where(method => !method
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(invocation => Invokes(invocation, "CreateTransientBuilder")
                        || Invokes(invocation, "UseTransientInternalServiceProvider")
                        || Invokes(invocation, "UseInternalServiceProvider")))
                .Select(method => FormattableString.Invariant($"{item.RelativePath}:{method.Identifier.ValueText}")))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var expected = s_expectedNonTransientUseMySqlSites
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"Expected externally owned UseMySql sites:{Environment.NewLine}{string.Join(Environment.NewLine, expected)}"
            + $"{Environment.NewLine}Actual externally owned UseMySql sites:{Environment.NewLine}"
            + string.Join(Environment.NewLine, actual));
    }

    private static bool Invokes(
        InvocationExpressionSyntax invocation,
        string methodName
    ) => invocation.Expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText == methodName,
        GenericNameSyntax generic => generic.Identifier.ValueText == methodName,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText == methodName,
        _ => false,
    };

    private static IEnumerable<(string RelativePath, CompilationUnitSyntax Root)> EnumerateSourceTrees(
        string projectRoot
    ) => Directory
        .EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal))
        .Select(path =>
        {
            var relativePath = Path
                .GetRelativePath(projectRoot, path)
                .Replace(Path.DirectorySeparatorChar, '/');

            var root = CSharpSyntaxTree
                .ParseText(File.ReadAllText(path))
                .GetCompilationUnitRoot();

            return (relativePath, root);
        });

    private static string FindRepositoryRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("DOKA_TEST_REPOSITORY_ROOT");

        if (!string.IsNullOrWhiteSpace(configuredRoot)
            && File.Exists(Path.Combine(configuredRoot, "Directory.Build.props")))
        {
            return Path.GetFullPath(configuredRoot);
        }

        foreach (var startPath in new[]
                 {
                     Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory
                 })
        {
            var directory = new DirectoryInfo(startPath);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
