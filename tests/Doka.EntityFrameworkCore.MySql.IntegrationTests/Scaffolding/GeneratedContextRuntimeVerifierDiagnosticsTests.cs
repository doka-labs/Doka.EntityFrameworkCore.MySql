using System.Xml.Linq;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies isolated scaffold compilation, dependency binding and failure diagnostics.</summary>
public sealed class GeneratedContextRuntimeVerifierDiagnosticsTests
{
    private const string Password = "diagnostic-test-secret";

    private const string ConnectionString =
        "Server=127.0.0.1;Database=unused;User ID=unused;Password=" + Password + ";Pooling=false";

    /// <summary>References the qualified binaries without recursively restoring or building source projects.</summary>
    [Fact]
    public void Generated_project_references_the_qualified_assemblies_without_source_projects()
    {
        // Arrange
        var expectedPaths = new[]
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
            .Select(assembly => assembly.Location)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Act
        var project = XDocument.Parse(GeneratedContextRuntimeVerifier.CreateProjectCode());

        // Assert
        var references = project
            .Descendants("Reference")
            .ToArray();

        Assert.Empty(project.Descendants("ProjectReference"));
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Equal(
            expectedPaths,
            references
                .Select(reference => reference.Element("HintPath")
                        ?.Value
                    ?? string.Empty)
                .Order(StringComparer.Ordinal));

        Assert.All(
            references,
            reference => Assert.Equal(
                "true",
                reference.Element("Private")
                    ?.Value));
    }

    /// <summary>Compiles and executes a context against the same provider, EF and spatial identities as its parent.</summary>
    [Fact]
    public async Task Generated_context_loads_the_qualified_dependency_identities()
    {
        // Arrange
        var efIdentity = JsonSerializer.Serialize(typeof(DbContext).Assembly.FullName);
        var providerIdentity = JsonSerializer.Serialize(typeof(MySqlServerVersion).Assembly.FullName);
        var spatialIdentity = JsonSerializer.Serialize(
            typeof(MySqlNetTopologySuiteDbContextOptionsBuilderExtensions).Assembly.FullName);

        var geometryIdentity = JsonSerializer.Serialize(typeof(Geometry).Assembly.FullName);
        var model = CreateModel(
            $$"""
              if (typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly.FullName != {{efIdentity}}
                  || typeof(Doka.EntityFrameworkCore.MySql.MySqlServerVersion).Assembly.FullName != {{providerIdentity}}
                  || typeof(Doka.EntityFrameworkCore.MySql.MySqlNetTopologySuiteDbContextOptionsBuilderExtensions)
                      .Assembly.FullName != {{spatialIdentity}}
                  || typeof(NetTopologySuite.Geometries.Geometry).Assembly.FullName != {{geometryIdentity}})
              {
                  throw new InvalidOperationException("The child loaded a different dependency identity.");
              }

              Console.WriteLine("DOKA_GENERATED_CONTEXT_OK");
              Environment.Exit(0);
              """);

        // WHY: This probe exits before database access to isolate SDK compilation and binary
        // binding. The six-engine roundtrip tests separately exercise schema reconstruction and CRUD.

        // Act
        var exception = await Record.ExceptionAsync(() =>
            GeneratedContextRuntimeVerifier.VerifyAsync(
                model,
                ConnectionString,
                "mariadb:12.3",
                CancellationToken.None));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>Retains the compiler error and generated file identity when the source is invalid.</summary>
    [Fact]
    public async Task Invalid_generated_source_preserves_compiler_diagnostics()
    {
        // Arrange
        var model = new ScaffoldedModel
        {
            ContextFile = new ScaffoldedFile(
                "RuntimeSchemaContext.cs",
                "namespace Doka.Scaffolding; public class RuntimeSchemaContext { this is invalid C#; }"),
        };

        // Act
        var exception = await Record.ExceptionAsync(() =>
            GeneratedContextRuntimeVerifier.VerifyAsync(
                model,
                ConnectionString,
                "mariadb:12.3",
                CancellationToken.None));

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("RuntimeSchemaContext.cs", failure.Message, StringComparison.Ordinal);
        Assert.Contains("error CS", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Retains both child streams and its failure while removing connection credentials.</summary>
    /// <param name="password">The decoded credential that may require escaping during serialization.</param>
    [Theory]
    [InlineData("diagnostic-test-secret")]
    [InlineData("a\"b'c")]
    public async Task Runtime_failure_preserves_both_streams_without_credentials(
        string password
    )
    {
        // Arrange
        var serializedCredential = new MySqlConnectionStringBuilder
        {
            Password = password,
        }.ConnectionString;

        var serializedPassword = serializedCredential[(serializedCredential.IndexOf('=') + 1)..];

        if (serializedPassword[0] is '\'' or '"')
        {
            serializedPassword = serializedPassword[1..^1];
        }

        // WHY: Reordering and alias normalization prevent exact input replacement from
        // hiding a leak in the driver's separately escaped password representation.
        var connectionString = serializedCredential
            + ";Uid=unused;Database=unused;Server=127.0.0.1;Pooling=false";

        var model = CreateModel(
            """
            var supplied = new MySqlConnector.MySqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("DOKA_GENERATED_CONTEXT_CONNECTION"));
            var reordered = new MySqlConnector.MySqlConnectionStringBuilder
            {
                Server = supplied.Server,
                Database = supplied.Database,
                UserID = supplied.UserID,
                Password = supplied.Password,
                Pooling = supplied.Pooling,
            };

            Console.WriteLine("stdout marker: " + reordered.ConnectionString);
            Console.Error.WriteLine("stderr marker: " + supplied.Password);
            throw new InvalidOperationException("intentional generated-context failure");
            """);

        // Act
        var exception = await Record.ExceptionAsync(() =>
            GeneratedContextRuntimeVerifier.VerifyAsync(
                model,
                connectionString,
                "mariadb:12.3",
                CancellationToken.None));

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("stdout marker:", failure.Message, StringComparison.Ordinal);
        Assert.Contains("stderr marker:", failure.Message, StringComparison.Ordinal);
        Assert.Contains("intentional generated-context failure", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(password, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(serializedCredential, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(serializedPassword, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Bounds exception diagnostics after redaction while still draining excessive child output.</summary>
    [Fact]
    public async Task Excessive_failure_output_is_truncated_without_exposing_credentials()
    {
        // Arrange
        var model = CreateModel(
            $$"""
              Console.WriteLine("credential marker: " + {{JsonSerializer.Serialize(Password)}});
              Console.WriteLine(new string('x', 40000));
              Console.Error.WriteLine("large-output failure");
              throw new InvalidOperationException("intentional large-output failure");
              """);

        // Act
        var exception = await Record.ExceptionAsync(() =>
            GeneratedContextRuntimeVerifier.VerifyAsync(
                model,
                ConnectionString,
                "mariadb:12.3",
                CancellationToken.None));

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("credential marker:", failure.Message, StringComparison.Ordinal);
        Assert.Contains("truncated", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("large-output failure", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, failure.Message, StringComparison.Ordinal);
        Assert.True(failure.Message.Length < 70000, "Each diagnostic stream must retain a bounded excerpt.");
    }

    /// <summary>Creates a context whose constructor observes the isolated execution without database access.</summary>
    /// <param name="constructorBody">The behavior executed before the generated program reaches its database contract.</param>
    /// <returns>A minimal scaffold compiled by the real runtime verifier.</returns>
    private static ScaffoldedModel CreateModel(
        string constructorBody
    ) => new()
    {
        ContextFile = new ScaffoldedFile(
            "RuntimeSchemaContext.cs",
            $$"""
              using System;
              using Microsoft.EntityFrameworkCore;

              namespace Doka.Scaffolding;

              /// <summary>Provides a controlled generated context for dependency and diagnostic probes.</summary>
              public sealed class RuntimeSchemaContext : DbContext
              {
                  /// <summary>Runs the test-owned probe before any database operation.</summary>
                  /// <param name="options">The options supplied by the generated program.</param>
                  public RuntimeSchemaContext(
                      DbContextOptions<RuntimeSchemaContext> options
                  ) : base(options)
                  {
                      {{constructorBody}}
                  }
              }
              """),
    };
}
