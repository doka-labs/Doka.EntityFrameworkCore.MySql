namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers the first diagnostics and logging baseline for the provider.
/// </summary>
public sealed class MySqlDiagnosticsTests
{
    [Fact]
    public void Invalid_configuration_logging_redacts_connection_string_secrets()
    {
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        var optionsBuilder = new DbContextOptionsBuilder();
        var extension = new MySqlOptionsExtension().WithConnectionString(
            "Server=localhost;Database=doka;User ID=root;Password=super-secret;");

        optionsBuilder.UseLoggerFactory(loggerFactory);

        var exception = Assert.Throws<InvalidOperationException>(() => extension.Validate(optionsBuilder.Options));

        Assert.Equal("A MySQL server version must be configured.", exception.Message);

        var entry = Assert.Single(sink.Entries);

        Assert.Equal(MySqlEventId.InvalidConfiguration.Id, entry.EventId.Id);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.Equal(MySqlLoggerCategory.Configuration, entry.Category);
        Assert.Contains("Password=***", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that schema-unsupported diagnostics use EF Core's model-validation category
    /// without exposing schema or object names.
    /// </summary>
    [Fact]
    public void Schema_unsupported_logging_uses_the_model_validation_category()
    {
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        var logger = loggerFactory.CreateLogger(DbLoggerCategory.Model.Validation.Name);

        MySqlLoggerMessages.SchemaUnsupported(
            logger,
            "Model",
            "schema unsupported",
            "remove the schema");

        var entry = Assert.Single(sink.Entries);

        Assert.Equal(DbLoggerCategory.Model.Validation.Name, entry.Category);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.DoesNotContain("ScopeName", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that server-version resolution emits a structured provider event.
    /// </summary>
    [Fact]
    public void Server_version_resolution_logging_uses_the_configuration_category()
    {
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        var builder = new DbContextOptionsBuilder();
        var singletonOptions = new MySqlSingletonOptions();
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));

        builder.UseLoggerFactory(loggerFactory);
        builder.UseMySql("Server=localhost;Database=doka;User ID=root;Password=password;", serverVersion);

        singletonOptions.Initialize(builder.Options);

        var entry = Assert.Single(sink.Entries);

        Assert.Equal(MySqlEventId.ServerVersionResolved.Id, entry.EventId.Id);
        Assert.Equal(LogLevel.Information, entry.LogLevel);
        Assert.Equal(MySqlLoggerCategory.Configuration, entry.Category);
        Assert.Contains("MariaDB", entry.Message, StringComparison.Ordinal);
        Assert.Contains("11.8.0", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that keyed/indexed max-length diagnostics use EF Core's model-validation category.
    /// </summary>
    [Fact]
    public void Keyed_or_indexed_max_length_validation_uses_the_model_validation_category()
    {
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        var logger = loggerFactory.CreateLogger(DbLoggerCategory.Model.Validation.Name);

        MySqlLoggerMessages.KeyOrIndexMaxLengthRequired(
            logger,
            "Entity",
            "Code",
            "text");

        var entry = Assert.Single(sink.Entries);

        Assert.Equal(DbLoggerCategory.Model.Validation.Name, entry.Category);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.Contains("explicit max length", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that implicit decimal precision diagnostics use EF Core's model-validation category.
    /// </summary>
    [Fact]
    public void Implicit_decimal_precision_warning_uses_the_model_validation_category()
    {
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        var logger = loggerFactory.CreateLogger(DbLoggerCategory.Model.Validation.Name);

        MySqlLoggerMessages.ImplicitDecimalPrecisionDefaulted(
            logger,
            "Entity",
            "Amount",
            defaultPrecision: 18,
            defaultScale: 2);

        var entry = Assert.Single(sink.Entries);

        Assert.Equal(DbLoggerCategory.Model.Validation.Name, entry.Category);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Contains("decimal(18,2)", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the provider always uses the logging execution-strategy wrapper for operability diagnostics.
    /// </summary>
    [Fact]
    public void Provider_uses_the_logging_execution_strategy_wrapper_for_operability_diagnostics()
    {
        using var context = new DiagnosticsContext(CreateOptions());
        var strategy = context.Database.CreateExecutionStrategy();

        Assert.IsType<MySqlLoggingExecutionStrategy>(strategy);
    }

    private static DbContextOptions<DiagnosticsContext> CreateOptions()
    {
        var optionsBuilder = new DbContextOptionsBuilder<DiagnosticsContext>();

        optionsBuilder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.EnableRetryOnFailure(maxRetryCount: 2, maxRetryDelay: TimeSpan.FromMilliseconds(1)));

        return optionsBuilder.Options;
    }

    private sealed class DiagnosticsContext : DbContext
    {
        public DiagnosticsContext(
            DbContextOptions<DiagnosticsContext> options
        ) : base(options) { }
    }
}
