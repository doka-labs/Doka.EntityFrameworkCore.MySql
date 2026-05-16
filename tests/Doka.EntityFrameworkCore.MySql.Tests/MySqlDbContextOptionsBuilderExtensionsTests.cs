namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers option-extension registration and mutation behavior.
/// </summary>
public sealed class MySqlDbContextOptionsBuilderExtensionsTests
{
    /// <summary>
    /// Verifies that <c>UseMySql(...)</c> stores the basic provider options.
    /// </summary>
    [Fact]
    public void UseMySql_stores_connection_string_and_server_version()
    {
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        builder.UseMySql("Server=localhost;Database=doka;User ID=root;Password=password;", serverVersion);

        var extension = Assert.IsType<MySqlOptionsExtension>(builder.Options.FindExtension<MySqlOptionsExtension>());

        Assert.Equal("Server=localhost;Database=doka;User ID=root;Password=password;", extension.ConnectionString);
        Assert.Equal(serverVersion, extension.ServerVersion);
        Assert.Null(extension.RetryOptions);
        Assert.Equal(MySqlGuidFormat.Binary16, extension.DefaultGuidFormat);
    }

    /// <summary>
    /// Verifies that the approved provider-specific options mutate the existing extension snapshot.
    /// </summary>
    [Fact]
    public void Approved_provider_options_update_the_existing_extension()
    {
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => options
                .EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(12))
                .DefaultGuidFormat(MySqlGuidFormat.Char36));

        var extension = Assert.IsType<MySqlOptionsExtension>(builder.Options.FindExtension<MySqlOptionsExtension>());

        Assert.NotNull(extension.RetryOptions);
        Assert.Equal(3, extension.RetryOptions.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(12), extension.RetryOptions.MaxRetryDelay);
        Assert.Equal(MySqlGuidFormat.Char36, extension.DefaultGuidFormat);
    }

    /// <summary>
    /// Verifies that the data-source overload stores the caller-owned data source.
    /// </summary>
    [Fact]
    public void UseMySql_stores_data_source_and_server_version()
    {
        using var dataSource = new MySqlDataSourceBuilder(
            "Server=localhost;Database=doka;User ID=root;Password=password;").Build();
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        builder.UseMySql(dataSource, serverVersion);

        var extension = Assert.IsType<MySqlOptionsExtension>(builder.Options.FindExtension<MySqlOptionsExtension>());

        Assert.Same(dataSource, extension.DataSource);
        Assert.Null(extension.ConnectionString);
        Assert.Null(extension.Connection);
        Assert.Equal(serverVersion, extension.ServerVersion);
    }

    /// <summary>
    /// Verifies that retry configuration validates invalid arguments.
    /// </summary>
    [Fact]
    public void EnableRetryOnFailure_rejects_invalid_arguments()
    {
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => options.EnableRetryOnFailure(maxRetryCount: 0)));

        Assert.Equal("maxRetryCount", exception.ParamName);
    }

    /// <summary>
    /// Verifies that <c>CommandTimeout</c> mutates the relational options snapshot.
    /// </summary>
    [Fact]
    public void CommandTimeout_stores_value_on_extension()
    {
        var extension = BuildExtension(options => options.CommandTimeout(45));

        Assert.Equal(45, extension.CommandTimeout);
    }

    /// <summary>
    /// Verifies that <c>CommandTimeout</c> rejects non-positive values.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CommandTimeout_rejects_non_positive_values(
        int commandTimeout
    )
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildExtension(options => options.CommandTimeout(commandTimeout)));

        Assert.Equal(nameof(commandTimeout), exception.ParamName);
    }

    /// <summary>
    /// Verifies that <c>MaxBatchSize</c> mutates the relational options snapshot.
    /// </summary>
    [Fact]
    public void MaxBatchSize_stores_value_on_extension()
    {
        var extension = BuildExtension(options => options.MaxBatchSize(128));

        Assert.Equal(128, extension.MaxBatchSize);
    }

    /// <summary>
    /// Verifies that <c>MaxBatchSize</c> rejects non-positive values.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void MaxBatchSize_rejects_non_positive_values(
        int maxBatchSize
    )
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildExtension(options => options.MaxBatchSize(maxBatchSize)));

        Assert.Equal(nameof(maxBatchSize), exception.ParamName);
    }

    /// <summary>
    /// Verifies that <c>MinBatchSize</c> mutates the relational options snapshot.
    /// </summary>
    [Fact]
    public void MinBatchSize_stores_value_on_extension()
    {
        var extension = BuildExtension(options => options.MinBatchSize(2));

        Assert.Equal(2, extension.MinBatchSize);
    }

    /// <summary>
    /// Verifies that <c>MinBatchSize</c> rejects non-positive values.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void MinBatchSize_rejects_non_positive_values(
        int minBatchSize
    )
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildExtension(options => options.MinBatchSize(minBatchSize)));

        Assert.Equal(nameof(minBatchSize), exception.ParamName);
    }

    /// <summary>
    /// Verifies that <c>MigrationsHistoryTable</c> stores the table name + optional schema
    /// on the relational options snapshot.
    /// </summary>
    [Fact]
    public void MigrationsHistoryTable_stores_name_and_schema_on_extension()
    {
        var extension = BuildExtension(options =>
            options.MigrationsHistoryTable("__custom_history", schema: "doka_meta"));

        Assert.Equal("__custom_history", extension.MigrationsHistoryTableName);
        Assert.Equal("doka_meta", extension.MigrationsHistoryTableSchema);
    }

    /// <summary>
    /// Verifies that <c>MigrationsHistoryTable</c> stores only the name when schema is omitted.
    /// </summary>
    [Fact]
    public void MigrationsHistoryTable_omits_schema_when_not_supplied()
    {
        var extension = BuildExtension(options => options.MigrationsHistoryTable("__custom_history"));

        Assert.Equal("__custom_history", extension.MigrationsHistoryTableName);
        Assert.Null(extension.MigrationsHistoryTableSchema);
    }

    /// <summary>
    /// Verifies that <c>MigrationsHistoryTable</c> rejects an empty or whitespace table name.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MigrationsHistoryTable_rejects_blank_table_name(
        string tableName
    )
    {
        Assert.Throws<ArgumentException>(() => BuildExtension(options => options.MigrationsHistoryTable(tableName)));
    }

    /// <summary>
    /// Verifies that <c>UseQuerySplittingBehavior</c> stores both legal enum values.
    /// </summary>
    [Theory]
    [InlineData(QuerySplittingBehavior.SingleQuery)]
    [InlineData(QuerySplittingBehavior.SplitQuery)]
    public void UseQuerySplittingBehavior_stores_value_on_extension(
        QuerySplittingBehavior behavior
    )
    {
        var extension = BuildExtension(options => options.UseQuerySplittingBehavior(behavior));

        Assert.Equal(behavior, extension.QuerySplittingBehavior);
    }

    /// <summary>
    /// Verifies that <c>UseQuerySplittingBehavior</c> rejects undefined enum values.
    /// </summary>
    [Fact]
    public void UseQuerySplittingBehavior_rejects_undefined_enum_value()
    {
        const QuerySplittingBehavior invalidValue = (QuerySplittingBehavior)99;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildExtension(options => options.UseQuerySplittingBehavior(invalidValue)));

        Assert.Equal("querySplittingBehavior", exception.ParamName);
    }

    private static MySqlOptionsExtension BuildExtension(
        Action<MySqlDbContextOptionsBuilder> configure
    )
    {
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        builder.UseMySql("Server=localhost;Database=doka;User ID=root;Password=password;", serverVersion, configure);

        return Assert.IsType<MySqlOptionsExtension>(builder.Options.FindExtension<MySqlOptionsExtension>());
    }
}
