using Doka.Caching.MySql;
using Microsoft.Extensions.Options;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers the deployed cache schema and configuration input boundaries.
/// </summary>
public sealed class MySqlCacheOptionsTests
{
    /// <summary>
    /// Verifies valid defaults and explicit schema requirements.
    /// </summary>
    [Fact]
    public void Valid_options_retain_documented_expiration_defaults()
    {
        // Arrange
        var options = MySqlCacheTestFactory.CreateValidOptions();

        // Act
        var validation = new MySqlCacheOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(20), options.DefaultSlidingExpiration);
        Assert.Equal(TimeSpan.FromMinutes(30), options.ExpiredItemsDeletionInterval);
        Assert.True(validation.Succeeded);
    }

    /// <summary>
    /// Verifies all configuration failures are reported together.
    /// </summary>
    [Fact]
    public void Validation_collects_independent_errors()
    {
        // Arrange
        var options = new MySqlCacheOptions
        {
            DefaultSlidingExpiration = TimeSpan.Zero,
            ExpiredItemsDeletionInterval = TimeSpan.Zero
        };

        // Act
        var result = new MySqlCacheOptionsValidator().Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("ConnectionString", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("SchemaName", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("TableName", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("DefaultSlidingExpiration", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure =>
            failure.Contains("ExpiredItemsDeletionInterval", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies malformed connection strings are rejected without exposing credentials.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("Server=localhost;Password=private-cache-password;UnsupportedCacheOption=true")]
    public void Invalid_connection_strings_fail_without_disclosing_secrets(
        string? connectionString
    )
    {
        // Arrange
        using var provider = MySqlCacheTestFactory.CreateProvider(options =>
            options.ConnectionString = connectionString!);

        Func<object?> act = () =>
            provider.GetRequiredService<IOptions<MySqlCacheOptions>>().Value;

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<OptionsValidationException>(recordedException);
        Assert.DoesNotContain("private-cache-password", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("ConnectionString", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a caller-owned data source is accepted without changing its connection settings.
    /// </summary>
    [Fact]
    public async Task External_data_source_is_retained_without_rebuilding_its_connection_string()
    {
        // Arrange
        const string connectionString = "Server=127.0.0.1;Port=1;User ID=cache;AutoEnlist=false;Pooling=false";
        await using var dataSource = new MySqlDataSource(connectionString);
        var options = MySqlCacheTestFactory.CreateValidOptions();
        options.ConnectionString = string.Empty;
        options.DataSource = dataSource;

        // Act
        var validation = new MySqlCacheOptionsValidator().Validate(null, options);
        var settings = new MySqlCacheSettings(options);
        options.DataSource = null;

        // Assert
        Assert.True(validation.Succeeded);
        Assert.Same(dataSource, settings.DataSource);
        Assert.Equal(connectionString, settings.ConnectionString);
    }

    /// <summary>
    /// Verifies conflicting connection sources are rejected without including credentials in the error.
    /// </summary>
    [Fact]
    public async Task Connection_string_and_external_data_source_are_mutually_exclusive()
    {
        // Arrange
        await using var dataSource = new MySqlDataSource(
            "Server=127.0.0.1;Password=private-source-password;AutoEnlist=false");

        await using var provider = MySqlCacheTestFactory.CreateProvider(options => options.DataSource = dataSource);
        Func<object?> act = () =>
            provider.GetRequiredService<IOptions<MySqlCacheOptions>>().Value;

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<OptionsValidationException>(recordedException);
        Assert.Contains("DataSource and ConnectionString", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-source-password", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies borrowed pools cannot silently enlist cache operations into an application transaction.
    /// </summary>
    [Theory]
    [InlineData("Server=127.0.0.1")]
    [InlineData("Server=127.0.0.1;AutoEnlist=true")]
    public async Task External_data_sources_must_disable_ambient_transaction_enlistment(
        string connectionString
    )
    {
        // Arrange
        await using var dataSource = new MySqlDataSource(connectionString);
        await using var provider = MySqlCacheTestFactory.CreateProvider(options =>
        {
            options.ConnectionString = string.Empty;
            options.DataSource = dataSource;
        });

        Func<Task> act = () =>
            provider
                .GetRequiredService<IAsyncStartupValidator>()
                .ValidateAsync(TestContext.Current.CancellationToken);

        // Act
        var recordedException = await Record.ExceptionAsync(act);

        // Assert
        var exception = Assert.IsType<OptionsValidationException>(recordedException);
        Assert.Contains("AutoEnlist=false", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies owned pools disable ambient enlistment without modifying the caller's options.
    /// </summary>
    [Fact]
    public void Owned_data_source_settings_disable_ambient_transaction_enlistment()
    {
        // Arrange
        var options = MySqlCacheTestFactory.CreateValidOptions();
        options.ConnectionString += ";AutoEnlist=true";

        // Act
        var settings = new MySqlCacheSettings(options);

        // Assert
        Assert.Null(settings.DataSource);
        Assert.False(new MySqlConnectionStringBuilder(settings.ConnectionString).AutoEnlist);
        Assert.True(new MySqlConnectionStringBuilder(options.ConnectionString).AutoEnlist);
    }

    /// <summary>
    /// Verifies durations below the supported precision and cleanup interval are rejected.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(9)]
    public void Default_sliding_expiration_must_be_at_least_one_microsecond(
        long ticks
    )
    {
        // Arrange
        var options = MySqlCacheTestFactory.CreateValidOptions();
        options.DefaultSlidingExpiration = TimeSpan.FromTicks(ticks);

        // Act
        var actual = new MySqlCacheOptionsValidator().Validate(null, options).Failed;

        // Assert
        Assert.True(actual);
    }

    /// <summary>
    /// Verifies that the supported minimum expiration and cleanup values are accepted.
    /// </summary>
    [Fact]
    public void Minimum_supported_durations_are_accepted()
    {
        // Arrange
        var options = MySqlCacheTestFactory.CreateValidOptions();
        options.DefaultSlidingExpiration = TimeSpan.FromMicroseconds(1);
        options.ExpiredItemsDeletionInterval = TimeSpan.FromMinutes(5);

        // Act
        var actual = new MySqlCacheOptionsValidator().Validate(null, options).Succeeded;

        // Assert
        Assert.True(actual);
    }

    /// <summary>
    /// Verifies cleanup cannot run more frequently than the documented lower bound.
    /// </summary>
    [Fact]
    public void Cleanup_interval_below_five_minutes_is_rejected()
    {
        // Arrange
        var options = MySqlCacheTestFactory.CreateValidOptions();
        options.ExpiredItemsDeletionInterval = TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1);

        // Act
        var actual = new MySqlCacheOptionsValidator().Validate(null, options).Failed;

        // Assert
        Assert.True(actual);
    }

    /// <summary>
    /// Verifies a very long cleanup interval does not overflow scheduling during resolution.
    /// </summary>
    [Fact]
    public async Task Maximum_cleanup_interval_is_accepted_without_timestamp_overflow()
    {
        // Arrange
        using var provider = MySqlCacheTestFactory.CreateProvider(options =>
            options.ExpiredItemsDeletionInterval = TimeSpan.MaxValue);

        await provider.GetRequiredService<IAsyncStartupValidator>()
            .ValidateAsync(TestContext.Current.CancellationToken);

        // Act
        var actual = provider.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();

        // Assert
        Assert.NotNull(actual);
    }

    /// <summary>
    /// Verifies identifier quoting keeps embedded delimiters inside one identifier.
    /// </summary>
    [Theory]
    [InlineData("cache_database", "cache_entries", "`cache_database`.`cache_entries`")]
    [InlineData("cache database", "select", "`cache database`.`select`")]
    [InlineData("db`name", "entries`; DROP TABLE x; --", "`db``name`.`entries``; DROP TABLE x; --`")]
    [InlineData("db.with.dot", "table.with.dot", "`db.with.dot`.`table.with.dot`")]
    [InlineData("\u00e9", "\u4e2d", "`\u00e9`.`\u4e2d`")]
    public void Schema_script_quotes_each_identifier_independently(
        string schemaName,
        string tableName,
        string quotedName
    )
    {
        // Arrange: The theory supplies schemaName, tableName, quotedName.

        // Act
        var script = MySqlCacheSchema.GetCreateScript(schemaName, tableName);

        // Assert
        Assert.StartsWith($"CREATE TABLE IF NOT EXISTS {quotedName} (", script, StringComparison.Ordinal);
        Assert.Contains($"Doka.Caching.MySql schema version {MySqlCacheSchema.Version}", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the MySQL identifier length boundary accepts exactly 64 characters.
    /// </summary>
    [Fact]
    public void Identifiers_at_the_server_length_limit_are_accepted()
    {
        // Arrange
        var identifier = new string('a', 64);

        // Act
        var actual = MySqlCacheSchema.GetCreateScript(identifier, identifier);

        // Assert
        Assert.Contains($"`{identifier}`.`{identifier}`", actual);
    }

    /// <summary>
    /// Verifies atomic upsert transmits the value parameter only once.
    /// </summary>
    [Fact]
    public void Atomic_upsert_transmits_the_value_parameter_only_once()
    {
        // Arrange
        var sql = new MySqlCacheSql("`cache_database`.`cache_entries`").Set;

        // Act
        var actual = sql.Split("@value", StringSplitOptions.None).Length;

        // Assert
        Assert.Equal(2, actual);
        Assert.Contains("ON DUPLICATE KEY UPDATE", sql, StringComparison.Ordinal);
        Assert.Contains("`Value` = `incoming`.`NewValue`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("VALUES(", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies invalid schema and table identifiers fail before SQL is emitted.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidIdentifierCases))]
    public void Schema_script_rejects_invalid_identifiers(
        string scenario,
        bool tableIdentifier
    )
    {
        // Arrange
        var identifier = CreateInvalidIdentifier(scenario);
        var schemaName = tableIdentifier ? "cache" : identifier!;
        var tableName = tableIdentifier ? identifier! : "entries";
        Action act = () => MySqlCacheSchema.GetCreateScript(schemaName, tableName);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(recordedException);
    }

    /// <summary>
    /// Verifies invalid identifiers fail options validation for each independently configurable name.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidIdentifierCases))]
    public void Options_reject_invalid_identifiers(
        string scenario,
        bool tableIdentifier
    )
    {
        // Arrange
        var identifier = CreateInvalidIdentifier(scenario);
        var options = MySqlCacheTestFactory.CreateValidOptions();

        if (tableIdentifier)
        {
            options.TableName = identifier!;
        }
        else
        {
            options.SchemaName = identifier!;
        }

        // Act
        var result = new MySqlCacheOptionsValidator().Validate(null, options);

        // Assert
        Assert.True(result.Failed);
    }

    /// <summary>
    /// Supplies invalid identifier contents separately for schema and table boundaries.
    /// </summary>
    public static IEnumerable<object[]> InvalidIdentifierCases()
    {
        foreach (var scenario in new[]
                 {
                     "null",
                     "empty",
                     "whitespace",
                     "null_character",
                     "trailing_space",
                     "supplementary_character",
                     "oversized",
                     "unpaired_high_surrogate",
                     "unpaired_low_surrogate"
                 })
        {
            yield return [scenario, false];
            yield return [scenario, true];
        }
    }

    /// <summary>Constructs invalid UTF-16 locally so theory serialization cannot replace its surrogate code units.</summary>
    private static string? CreateInvalidIdentifier(string scenario) => scenario switch
    {
        "null" => null,
        "empty" => string.Empty,
        "whitespace" => " \t",
        "null_character" => "name\0suffix",
        "trailing_space" => "trailing ",
        "supplementary_character" => "\ud83d\ude00",
        "oversized" => new string('a', 65),
        "unpaired_high_surrogate" => "name\ud800",
        "unpaired_low_surrogate" => "name\udfff",
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    /// <summary>
    /// Verifies constructed settings do not change if a caller later mutates the options.
    /// </summary>
    [Fact]
    public void Resolved_settings_snapshot_the_configuration()
    {
        // Arrange
        var options = MySqlCacheTestFactory.CreateValidOptions();
        var settings = new MySqlCacheSettings(options);
        var connectionString = settings.ConnectionString;

        // Act
        options.ConnectionString = "Server=other-host";
        options.SchemaName = "other_database";
        options.TableName = "other_entries";
        options.DefaultSlidingExpiration = TimeSpan.FromHours(1);

        options.ExpiredItemsDeletionInterval = TimeSpan.FromHours(2);

        // Assert
        Assert.Equal(connectionString, settings.ConnectionString);
        Assert.Equal("`cache_database`.`cache_entries`", settings.QualifiedTableName);
        Assert.Equal(1_200_000_000L, settings.DefaultSlidingExpirationMicroseconds);
        Assert.Equal(TimeSpan.FromMinutes(30), settings.ExpiredItemsDeletionInterval);
    }
}
