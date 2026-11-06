using System.IO;
using System.Security;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Compiles scaffolded source in an isolated temporary project and executes its
/// schema-reconstruction, CRUD, generated-value, and relationship-fixup contract.
/// </summary>
internal static class GeneratedContextRuntimeVerifier
{
    private const string ConnectionStringVariable = "DOKA_GENERATED_CONTEXT_CONNECTION";
    private const string ServerVersionVariable = "DOKA_GENERATED_CONTEXT_SERVER_VERSION";
    private const string SuccessMarker = "DOKA_GENERATED_CONTEXT_OK";
    private const int MaximumDiagnosticCharacters = 32_768;

    /// <summary>
    /// Compiles and runs the scaffolded model against the configured database.
    /// </summary>
    /// <param name="scaffoldedModel">The generated model source to compile.</param>
    /// <param name="connectionString">The target database connection string.</param>
    /// <param name="serverVersionText">The provider server-version identity.</param>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    public static Task VerifyAsync(
        ScaffoldedModel scaffoldedModel,
        string connectionString,
        string serverVersionText,
        CancellationToken cancellationToken
    ) => VerifyCoreAsync(scaffoldedModel, connectionString, serverVersionText, null, cancellationToken);

    /// <summary>Verifies a scaffold with an independently controlled execution deadline.</summary>
    /// <param name="scaffoldedModel">The generated model source to compile.</param>
    /// <param name="connectionString">The target database connection string.</param>
    /// <param name="serverVersionText">The provider server-version identity.</param>
    /// <param name="executionDeadlineToken">The deadline signaled after the child has started.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <remarks>Tests signal the deadline after child readiness instead of depending on machine speed.</remarks>
    internal static Task VerifyAsync(
        ScaffoldedModel scaffoldedModel,
        string connectionString,
        string serverVersionText,
        CancellationToken executionDeadlineToken,
        CancellationToken cancellationToken
    ) => VerifyCoreAsync(
        scaffoldedModel,
        connectionString,
        serverVersionText,
        executionDeadlineToken,
        cancellationToken);

    /// <summary>Prepares and owns one isolated generated-context execution.</summary>
    /// <param name="scaffoldedModel">The generated model source to compile.</param>
    /// <param name="connectionString">The target database connection string.</param>
    /// <param name="serverVersionText">The provider server-version identity.</param>
    /// <param name="executionDeadlineToken">An explicit deadline, or null for the normal launch-time deadline.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    private static async Task VerifyCoreAsync(
        ScaffoldedModel scaffoldedModel,
        string connectionString,
        string serverVersionText,
        CancellationToken? executionDeadlineToken,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(scaffoldedModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverVersionText);
        cancellationToken.ThrowIfCancellationRequested();

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "doka-generated-context-"
            + Guid
                .NewGuid()
                .ToString("N", CultureInfo.InvariantCulture));

        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            await WriteProjectAsync(temporaryDirectory, scaffoldedModel, cancellationToken)
                .ConfigureAwait(false);

            var result = await RunProjectAsync(
                    temporaryDirectory,
                    connectionString,
                    serverVersionText,
                    executionDeadlineToken,
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.ExitCode != 0
                || !result.StandardOutput.Contains(SuccessMarker, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Generated context execution exited with code {result.ExitCode}."
                    + Environment.NewLine
                    + FormatDiagnostics(result.StandardOutput, result.StandardError, connectionString));
            }
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Writes the isolated project and its generated model source.
    /// </summary>
    /// <param name="temporaryDirectory">The isolated project directory.</param>
    /// <param name="scaffoldedModel">The generated model source to write.</param>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    private static async Task WriteProjectAsync(
        string temporaryDirectory,
        ScaffoldedModel scaffoldedModel,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repositoryRoot = FindRepositoryRoot();

        // The child process starts below /tmp, so SDK discovery cannot see the
        // repository global.json. Copy the repository contract into the isolated
        // project to prevent a newer machine-wide SDK from changing analyzers.
        File.Copy(
            Path.Combine(repositoryRoot, "global.json"),
            Path.Combine(temporaryDirectory, "global.json"),
            overwrite: true);

        await File
            .WriteAllTextAsync(
                Path.Combine(temporaryDirectory, "GeneratedContext.csproj"),
                CreateProjectCode(),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await File
            .WriteAllTextAsync(
                Path.Combine(temporaryDirectory, "Program.cs"),
                CreateProgramCode(),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await WriteScaffoldedFileAsync(temporaryDirectory, scaffoldedModel.ContextFile, cancellationToken)
            .ConfigureAwait(false);

        foreach (var additionalFile in scaffoldedModel.AdditionalFiles)
        {
            await WriteScaffoldedFileAsync(temporaryDirectory, additionalFile, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Creates an SDK project bound to the binaries qualified by this test host.</summary>
    /// <returns>The standalone project XML with explicit compile references and copy-local runtime dependencies.</returns>
    internal static string CreateProjectCode()
    {
        // WHY: Rebuilding source ProjectReferences in each isolated child duplicates
        // provider compilation across parallel tests. Loaded assembly paths also keep
        // the child on the exact EF patch and provider binaries selected by this run.
        var references = string.Join(
            Environment.NewLine,
            new[]
                {
                    typeof(MySqlServerVersion).Assembly,
                    typeof(MySqlNetTopologySuiteDbContextOptionsBuilderExtensions).Assembly,
                    typeof(DbContext).Assembly,
                    typeof(RelationalDatabaseFacadeExtensions).Assembly,
                    typeof(OwnedAttribute).Assembly,
                    typeof(IReverseEngineerScaffolder).Assembly,
                    typeof(Geometry).Assembly,
                    typeof(IServiceCollection).Assembly,
                    typeof(ILoggerFactory).Assembly,
                    typeof(MySqlConnection).Assembly,
                }
                .Distinct()
                .Select(assembly =>
                {
                    var assemblyName = assembly.GetName().Name
                        ?? throw new InvalidOperationException("A required assembly has no name.");

                    return $"""
                            <Reference Include="{EscapeXml(assemblyName)}">
                              <HintPath>{EscapeXml(assembly.Location)}</HintPath>
                              <Private>true</Private>
                            </Reference>
                            """;
                }));

        return $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net11.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>disable</ImplicitUsings>
                    <WarningsAsErrors>true</WarningsAsErrors>
                    <RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>
                    <NuGetAudit>false</NuGetAudit>
                  </PropertyGroup>
                  <ItemGroup>
                    {references}
                  </ItemGroup>
                </Project>
                """;
    }

    /// <summary>
    /// Writes one generated source file into the isolated project directory.
    /// </summary>
    /// <param name="temporaryDirectory">The isolated project directory.</param>
    /// <param name="scaffoldedFile">The generated source file to write.</param>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    private static async Task WriteScaffoldedFileAsync(
        string temporaryDirectory,
        ScaffoldedFile scaffoldedFile,
        CancellationToken cancellationToken
    )
    {
        var fileName = Path.GetFileName(scaffoldedFile.Path);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("A scaffolded source file did not have a valid file name.");
        }

        await File
            .WriteAllTextAsync(
                Path.Combine(temporaryDirectory, fileName),
                scaffoldedFile.Code,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the isolated project within the caller's lifetime and the build deadline.
    /// </summary>
    /// <param name="temporaryDirectory">The isolated project directory.</param>
    /// <param name="connectionString">The target database connection string.</param>
    /// <param name="serverVersionText">The provider server-version identity.</param>
    /// <param name="executionDeadlineToken">An explicit deadline, or null for the normal two-minute deadline.</param>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    private static async Task<ProcessResult> RunProjectAsync(
        string temporaryDirectory,
        string connectionString,
        string serverVersionText,
        CancellationToken? executionDeadlineToken,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = temporaryDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine(temporaryDirectory, "GeneratedContext.csproj"));
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--artifacts-path");
        startInfo.ArgumentList.Add(Path.Combine(temporaryDirectory, "artifacts"));
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--verbosity");
        startInfo.ArgumentList.Add("minimal");
        startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");
        startInfo.Environment[ConnectionStringVariable] = connectionString;
        startInfo.Environment[ServerVersionVariable] = serverVersionText;
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The generated context process could not be started.");

        // WHY: Output must drain even after cancellation so redirected pipes cannot
        // strand the child while its process tree is being terminated.
        var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var exited = process.WaitForExitAsync(CancellationToken.None);
        using var automaticDeadline = new CancellationTokenSource();

        if (executionDeadlineToken is null)
        {
            // WHY: Start hang protection at launch, after project preparation; this
            // bounds an unresponsive child rather than rating provider performance.
            automaticDeadline.CancelAfter(TimeSpan.FromMinutes(2));
        }

        var deadlineToken = executionDeadlineToken ?? automaticDeadline.Token;
        using var observerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            deadlineToken);

        OperationCanceledException? deadlineFailure = null;
        string[] output;

        try
        {
            // WHY: The observer distinguishes timeout from caller cancellation;
            // finally still completes the owned exit task before either outcome escapes.
            await exited
                .WaitAsync(observerCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            // WHY: The linked observer carries its own token; callers must receive
            // their original token, including when their cancellation and deadline race.
            throw new OperationCanceledException(exception.Message, exception, cancellationToken);
        }
        catch (OperationCanceledException exception) when (deadlineToken.IsCancellationRequested)
        {
            // WHY: Freeze the observed outcome before teardown. Later caller
            // cancellation cannot reclassify this deadline as a caller cancellation.
            deadlineFailure = exception;
        }
        finally
        {
            // WHY: Once started, the child and its pipes must be reaped before the
            // temporary project can be deleted, including on caller cancellation.
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // The child can exit between the status check and Kill.
                }
            }

            await exited
                .ConfigureAwait(false);

            output = await Task
                .WhenAll(standardOutput, standardError)
                .ConfigureAwait(false);
        }

        if (deadlineFailure is not null)
        {
            // WHY: Construct the failure only after termination and pipe draining,
            // so the diagnostic identifies whether build or runtime was reached.
            throw new TimeoutException(
                "Generated context compilation and execution exceeded its execution deadline."
                + Environment.NewLine
                + FormatDiagnostics(output[0], output[1], connectionString),
                deadlineFailure);
        }

        return new ProcessResult(process.ExitCode, output[0], output[1]);
    }

    /// <summary>Formats bounded child diagnostics without exposing supplied credentials.</summary>
    /// <param name="standardOutput">The drained child standard output.</param>
    /// <param name="standardError">The drained child standard error.</param>
    /// <param name="connectionString">The supplied connection string to redact.</param>
    /// <returns>Labeled diagnostic excerpts safe for test failure output.</returns>
    private static string FormatDiagnostics(
        string standardOutput,
        string standardError,
        string connectionString
    )
    {
        var connection = new MySqlConnectionStringBuilder(connectionString);
        var password = connection.Password;
        var doubleQuotedPassword = password.Replace("\"", "\"\"", StringComparison.Ordinal);
        var singleQuotedPassword = password.Replace("'", "''", StringComparison.Ordinal);

        return $"stdout: {Sanitize(standardOutput)}{Environment.NewLine}stderr: {Sanitize(standardError)}";

        string Sanitize(
            string text
        )
        {
            var sanitized = text.Replace(connectionString, "[redacted connection]", StringComparison.Ordinal);

            if (connection.ConnectionString.Length > 0)
            {
                sanitized = sanitized.Replace(
                    connection.ConnectionString,
                    "[redacted connection]",
                    StringComparison.Ordinal);
            }

            if (password.Length > 0)
            {
                // WHY: Connection-string serializers double their enclosing quote.
                // Reordered output may not match either full connection string, so
                // redact both escaped forms before the decoded credential as well.
                sanitized = sanitized.Replace(doubleQuotedPassword, "[redacted password]", StringComparison.Ordinal);
                sanitized = sanitized.Replace(singleQuotedPassword, "[redacted password]", StringComparison.Ordinal);
                sanitized = sanitized.Replace(password, "[redacted password]", StringComparison.Ordinal);
            }

            // WHY: Redact before truncating so cutting through a credential cannot
            // leave its prefix visible at the diagnostic excerpt boundary.
            return sanitized.Length <= MaximumDiagnosticCharacters
                ? sanitized
                : sanitized[..MaximumDiagnosticCharacters] + " [output truncated]";
        }
    }

    private static string EscapeXml(
        string value
    ) => SecurityElement.Escape(value)
        ?? throw new InvalidOperationException("A project path could not be escaped for XML.");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Doka.EntityFrameworkCore.MySql.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from the integration-test output path.");
    }

    private static string CreateProgramCode() =>
        """
        using System;
        using System.Collections;
        using System.Linq;
        using System.Threading.Tasks;
        using Doka.EntityFrameworkCore.MySql;
        using Doka.Scaffolding;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Design;
        using Microsoft.EntityFrameworkCore.Infrastructure;
        using Microsoft.EntityFrameworkCore.Metadata;
        using Microsoft.EntityFrameworkCore.Migrations;

        // WHY: These boundaries distinguish compilation from model, DDL and CRUD
        // execution in retained timeout diagnostics without evaluating elapsed time.
        Console.WriteLine("DOKA_GENERATED_CONTEXT_STAGE: started");
        var connectionString = Environment.GetEnvironmentVariable("DOKA_GENERATED_CONTEXT_CONNECTION")
            ?? throw new InvalidOperationException("The generated context connection string is missing.");
        var serverVersionText = Environment.GetEnvironmentVariable("DOKA_GENERATED_CONTEXT_SERVER_VERSION")
            ?? throw new InvalidOperationException("The generated context server version is missing.");
        var serverVersion = MySqlServerVersion.Parse(serverVersionText);
        var options = new DbContextOptionsBuilder<RuntimeSchemaContext>()
            .UseMySql(
                connectionString,
                serverVersion,
                providerOptions => providerOptions.UseNetTopologySuite())
            .Options;

        await using var context = new RuntimeSchemaContext(options);
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        var modelDiffer = context.GetService<IMigrationsModelDiffer>();
        var sqlGenerator = context.GetService<IMigrationsSqlGenerator>();
        Console.WriteLine("DOKA_GENERATED_CONTEXT_STAGE: model-ready");
        var operations = modelDiffer.GetDifferences(
            null,
            designTimeModel.GetRelationalModel());

        foreach (var command in sqlGenerator.Generate(operations, designTimeModel))
        {
            await context.Database.ExecuteSqlRawAsync(command.CommandText);
        }

        Console.WriteLine("DOKA_GENERATED_CONTEXT_STAGE: schema-created");
        var parentEntityType = context.Model.GetEntityTypes()
            .Single(entityType => entityType.GetTableName() == "doka_scaffold_core_parent");
        var childEntityType = context.Model.GetEntityTypes()
            .Single(entityType => entityType.GetTableName() == "doka_scaffold_core_child");
        var parent = Activator.CreateInstance(parentEntityType.ClrType)
            ?? throw new InvalidOperationException("The generated parent could not be created.");

        SetProperty(parent, "Code", "runtime-parent");
        SetProperty(parent, "OptionalCount", 9);
        context.Add(parent);
        await context.SaveChangesAsync();
        await context.Entry(parent).ReloadAsync();

        var parentId = GetInt32Property(parent, "Id");
        var computedCount = GetInt32Property(parent, "ComputedCount");

        if (parentId <= 0 || computedCount != 10)
        {
            throw new InvalidOperationException("Generated parent values were not read back correctly.");
        }

        var child = Activator.CreateInstance(childEntityType.ClrType)
            ?? throw new InvalidOperationException("The generated child could not be created.");

        SetProperty(child, "ParentId", parentId);
        context.Add(child);
        await context.SaveChangesAsync();
        var childId = GetInt32Property(child, "Id");

        if (childId <= 0)
        {
            throw new InvalidOperationException("The generated child key was not populated.");
        }

        context.ChangeTracker.Clear();

        var loadedChild = await context.FindAsync(
                childEntityType.ClrType,
                new object?[] { childId })
            ?? throw new InvalidOperationException("The generated child could not be read.");
        var loadedParent = await context.FindAsync(
                parentEntityType.ClrType,
                new object?[] { parentId })
            ?? throw new InvalidOperationException("The generated parent could not be read.");
        var foreignKey = childEntityType.GetForeignKeys().Single();
        var dependentNavigation = foreignKey.DependentToPrincipal
            ?? throw new InvalidOperationException("The dependent navigation is missing.");
        var principalNavigation = foreignKey.PrincipalToDependent
            ?? throw new InvalidOperationException("The principal navigation is missing.");
        var fixedUpParent = dependentNavigation.PropertyInfo?.GetValue(loadedChild);
        var fixedUpChildren = principalNavigation.PropertyInfo?.GetValue(loadedParent) as IEnumerable;

        if (!ReferenceEquals(fixedUpParent, loadedParent)
            || fixedUpChildren is null
            || !fixedUpChildren.Cast<object>().Contains(loadedChild))
        {
            throw new InvalidOperationException("Generated relationship fixup did not preserve both navigations.");
        }

        await context.Database.OpenConnectionAsync();
        await using var richTableCommand = context.Database.GetDbConnection().CreateCommand();
        richTableCommand.CommandText = "SELECT COUNT(*) FROM `doka_scaffold_index_store`;";
        _ = await richTableCommand.ExecuteScalarAsync();

        Console.WriteLine("DOKA_GENERATED_CONTEXT_OK");

        static void SetProperty(
            object instance,
            string propertyName,
            object value
        )
        {
            var property = instance.GetType().GetProperty(propertyName)
                ?? throw new InvalidOperationException($"Generated property '{propertyName}' is missing.");
            property.SetValue(instance, value);
        }

        static int GetInt32Property(
            object instance,
            string propertyName
        )
        {
            var property = instance.GetType().GetProperty(propertyName)
                ?? throw new InvalidOperationException($"Generated property '{propertyName}' is missing.");

            return Convert.ToInt32(property.GetValue(instance), System.Globalization.CultureInfo.InvariantCulture);
        }
        """;

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError
    );
}
