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
        var options = MySqlCacheTestFactory.CreateValidOptions();

        Assert.Equal(TimeSpan.FromMinutes(20), options.DefaultSlidingExpiration);
        Assert.Equal(TimeSpan.FromMinutes(30), options.ExpiredItemsDeletionInterval);
        Assert.True(new MySqlCacheOptionsValidator().Validate(null, options).Succeeded);
    }

    /// <summary>
    /// Verifies all configuration failures are reported together.
    /// </summary>
    [Fact]
    public void Validation_collects_independent_errors()
    {
        var options = new MySqlCacheOptions
        {
            DefaultSlidingExpiration = TimeSpan.Zero,
            ExpiredItemsDeletionInterval = TimeSpan.Zero
        };

        var result = new MySqlCacheOptionsValidator().Validate(null, options);

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
        using var provider = MySqlCacheTestFactory.CreateProvider(options =>
            options.ConnectionString = connectionString!);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<MySqlCacheOptions>>().Value);

        Assert.DoesNotContain("private-cache-password", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("ConnectionString", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a caller-owned data source is accepted without changing its connection settings.
    /// </summary>
    [Fact]
    public async Task External_data_source_is_retained_without_rebuilding_its_connection_string()
    {
        const string connectionString = "Server=127.0.0.1;Port=1;User ID=cache;AutoEnlist=false;Pooling=false";
        await using var dataSource = new MySqlDataSource(connectionString);
        var options = MySqlCacheTestFactory.CreateValidOptions();
        options.ConnectionString = string.Empty;
        options.DataSource = dataSource;

        Assert.True(new MySqlCacheOptionsValidator()
            .Validate(null, options)
            .Succeeded);

        var settings = new MySqlCacheSettings(options);
        options.DataSource = null;

        Assert.Same(dataSource, settings.DataSource);
        Assert.Equal(connectionString, settings.ConnectionString);
    }

    /// <summary>
    /// Verifies conflicting connection sources are rejected without including credentials in the error.
    /// </summary>
    [Fact]
    public async Task Connection_string_and_external_data_source_are_mutually_exclusive()
    {
        await using var dataSource = new MySqlDataSource(
            "Server=127.0.0.1;Password=private-source-password;AutoEnlist=false");

        await using var provider = MySqlCacheTestFactory.CreateProvider(options => options.DataSource = dataSource);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<MySqlCacheOptions>>().Value);

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
        await using var dataSource = new MySqlDataSource(connectionString);
        await using var provider = MySqlCacheTestFactory.CreateProvider(options =>
        {
            options.ConnectionString = string.Empty;
            options.DataSource = dataSource;
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider
                .GetRequiredService<IStartupValidator>()
                .Validate());

        Assert.Contains("AutoEnlist=false", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies owned pools disable ambient enlistment without modifying the caller's options.
    /// </summary>
    [Fact]
    public void Owned_data_source_settings_disable_ambient_transaction_enlistment()
    {
        var options = MySqlCacheTestFactory.CreateValidOptions();
        options.ConnectionString += ";AutoEnlist=true";
        var settings = new MySqlCacheSettings(options);

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
        var options = MySqlCacheTestFactory.CreateValidOptions();
        options.DefaultSlidingExpiration = TimeSpan.FromTicks(ticks);

        Assert.True(new MySqlCacheOptionsValidator().Validate(null, options).Failed);
    }

    /// <summary>
    /// Verifies that the supported minimum expiration and cleanup values are accepted.
    /// </summary>
    [Fact]
    public void Minimum_supported_durations_are_accepted()
    {
        var options = MySqlCacheTestFactory.CreateValidOptions();
        options.DefaultSlidingExpiration = TimeSpan.FromMicroseconds(1);
        options.ExpiredItemsDeletionInterval = TimeSpan.FromMinutes(5);

        Assert.True(new MySqlCacheOptionsValidator().Validate(null, options).Succeeded);
    }

    /// <summary>
    /// Verifies cleanup cannot run more frequently than the documented lower bound.
    /// </summary>
    [Fact]
    public void Cleanup_interval_below_five_minutes_is_rejected()
    {
        var options = MySqlCacheTestFactory.CreateValidOptions();
        options.ExpiredItemsDeletionInterval = TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1);

        Assert.True(new MySqlCacheOptionsValidator().Validate(null, options).Failed);
    }

    /// <summary>
    /// Verifies a very long cleanup interval does not overflow scheduling during resolution.
    /// </summary>
    [Fact]
    public void Maximum_cleanup_interval_is_accepted_without_timestamp_overflow()
    {
        using var provider = MySqlCacheTestFactory.CreateProvider(options =>
            options.ExpiredItemsDeletionInterval = TimeSpan.MaxValue);

        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.NotNull(provider.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>());
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
        var script = MySqlCacheSchema.GetCreateScript(schemaName, tableName);

        Assert.StartsWith($"CREATE TABLE IF NOT EXISTS {quotedName} (", script, StringComparison.Ordinal);
        Assert.Contains($"Doka.Caching.MySql schema version {MySqlCacheSchema.Version}", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the MySQL identifier length boundary accepts exactly 64 characters.
    /// </summary>
    [Fact]
    public void Identifiers_at_the_server_length_limit_are_accepted()
    {
        var identifier = new string('a', 64);

        Assert.Contains($"`{identifier}`.`{identifier}`", MySqlCacheSchema.GetCreateScript(identifier, identifier));
    }

    [Fact]
    public void Atomic_upsert_transmits_the_value_parameter_only_once()
    {
        var sql = new MySqlCacheSql("`cache_database`.`cache_entries`").Set;

        Assert.Equal(2, sql.Split("@value", StringSplitOptions.None).Length);
        Assert.Contains("ON DUPLICATE KEY UPDATE", sql, StringComparison.Ordinal);
        Assert.Contains("`Value` = `incoming`.`NewValue`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("VALUES(", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies invalid schema and table identifiers fail before SQL is emitted.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("name\0suffix")]
    [InlineData("trailing ")]
    [InlineData("\ud83d\ude00")]
    public void Invalid_identifiers_are_rejected_by_schema_and_options(
        string? identifier
    )
    {
        Assert.ThrowsAny<ArgumentException>(() => MySqlCacheSchema.GetCreateScript(identifier!, "entries"));
        Assert.ThrowsAny<ArgumentException>(() => MySqlCacheSchema.GetCreateScript("cache", identifier!));

        var options = MySqlCacheTestFactory.CreateValidOptions();
        options.SchemaName = identifier!;
        Assert.True(new MySqlCacheOptionsValidator().Validate(null, options).Failed);
        options = MySqlCacheTestFactory.CreateValidOptions();
        options.TableName = identifier!;
        Assert.True(new MySqlCacheOptionsValidator().Validate(null, options).Failed);
    }

    /// <summary>
    /// Verifies oversized and malformed UTF-16 identifiers cannot enter a DDL statement.
    /// </summary>
    [Fact]
    public void Oversized_and_unpaired_surrogate_identifiers_are_rejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => MySqlCacheSchema.GetCreateScript(new string('a', 65), "entries"));
        Assert.ThrowsAny<ArgumentException>(() => MySqlCacheSchema.GetCreateScript("cache", new string('a', 65)));
        Assert.ThrowsAny<ArgumentException>(() => MySqlCacheSchema.GetCreateScript("cache", "name\ud800"));
        Assert.ThrowsAny<ArgumentException>(() => MySqlCacheSchema.GetCreateScript("name\udfff", "entries"));
    }

    /// <summary>
    /// Verifies constructed settings do not change if a caller later mutates the options.
    /// </summary>
    [Fact]
    public void Resolved_settings_snapshot_the_configuration()
    {
        var options = MySqlCacheTestFactory.CreateValidOptions();
        var settings = new MySqlCacheSettings(options);
        var connectionString = settings.ConnectionString;
        options.ConnectionString = "Server=other-host";
        options.SchemaName = "other_database";
        options.TableName = "other_entries";
        options.DefaultSlidingExpiration = TimeSpan.FromHours(1);
        options.ExpiredItemsDeletionInterval = TimeSpan.FromHours(2);

        Assert.Equal(connectionString, settings.ConnectionString);
        Assert.Equal("`cache_database`.`cache_entries`", settings.QualifiedTableName);
        Assert.Equal(1_200_000_000L, settings.DefaultSlidingExpirationMicroseconds);
        Assert.Equal(TimeSpan.FromMinutes(30), settings.ExpiredItemsDeletionInterval);
    }
}
