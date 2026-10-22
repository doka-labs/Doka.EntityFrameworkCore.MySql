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
        // Arrange
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        // Act
        builder.UseMySql("Server=localhost;Database=doka;User ID=root;Password=password;", serverVersion);

        var actual = builder.Options.FindExtension<MySqlOptionsExtension>();

        // Assert
        var extension = Assert.IsType<MySqlOptionsExtension>(actual);
        Assert.Equal("Server=localhost;Database=doka;User ID=root;Password=password;", extension.ConnectionString);
        Assert.Equal(serverVersion, extension.ServerVersion);
        Assert.Null(extension.RetryOptions);
        Assert.Equal(MySqlGuidFormat.Binary16, extension.DefaultGuidFormat);
        Assert.Equal(ParameterTranslationMode.MultipleParameters, extension.ParameterizedCollectionMode);
    }

    /// <summary>
    /// Verifies that ordinary collections retain EF Core 10's cardinality-visible
    /// multiple-parameter default.
    /// </summary>
    [Fact]
    public void UseMySql_defaults_parameterized_collections_to_multiple_parameters()
    {
        // Arrange: The factory supplies the default provider configuration.

        // Act
        var extension = BuildExtension(_ => { });

        // Assert
        Assert.Equal(ParameterTranslationMode.MultipleParameters, extension.ParameterizedCollectionMode);
    }

    /// <summary>
    /// Verifies that every supported EF Core collection-translation mode can
    /// replace Doka's provider default through the provider options callback.
    /// </summary>
    [Theory]
    [InlineData(ParameterTranslationMode.Constant)]
    [InlineData(ParameterTranslationMode.MultipleParameters)]
    [InlineData(ParameterTranslationMode.Parameter)]
    public void Explicit_parameterized_collection_mode_overrides_the_provider_default(
        ParameterTranslationMode mode
    )
    {
        // Arrange: The theory supplies mode.

        // Act
        var extension = BuildExtension(options => options.UseParameterizedCollectionMode(mode));

        // Assert
        Assert.Equal(mode, extension.ParameterizedCollectionMode);
    }

    /// <summary>
    /// Verifies that an undefined collection-translation mode fails during
    /// options construction instead of reaching query compilation.
    /// </summary>
    [Fact]
    public void Explicit_parameterized_collection_mode_rejects_undefined_value()
    {
        // Arrange
        const ParameterTranslationMode invalidMode = (ParameterTranslationMode)99;
        Action act = () =>
            BuildExtension(options => options.UseParameterizedCollectionMode(invalidMode));

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Equal("Parameterized collection mode '99' is not defined.", exception.Message);
    }

    /// <summary>
    /// A later mutation through EF Core's inherited relational builder is
    /// rejected when the final options snapshot is validated.
    /// </summary>
    [Fact]
    public void Later_undefined_parameterized_collection_mode_is_rejected()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        new MySqlDbContextOptionsBuilder(builder)
            .UseParameterizedCollectionMode((ParameterTranslationMode)99);
        var actual = builder.Options.FindExtension<MySqlOptionsExtension>();
        var extension = Assert.IsType<MySqlOptionsExtension>(
            actual);

        // Act
        var recordedException = Record.Exception(() => extension.Validate(builder.Options));

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Equal("Parameterized collection mode '99' is not defined.", exception.Message);
    }

    /// <summary>
    /// Verifies that the approved provider-specific options mutate the existing extension snapshot.
    /// </summary>
    [Fact]
    public void Approved_provider_options_update_the_existing_extension()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        // Act
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => options
                .EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(12))
                .DefaultGuidFormat(MySqlGuidFormat.Char36));

        var actual = builder.Options.FindExtension<MySqlOptionsExtension>();

        // Assert
        var extension = Assert.IsType<MySqlOptionsExtension>(actual);
        Assert.NotNull(extension.RetryOptions);
        Assert.Equal(3, extension.RetryOptions.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(12), extension.RetryOptions.MaxRetryDelay);
        Assert.Equal(MySqlGuidFormat.Char36, extension.DefaultGuidFormat);
    }

    /// <summary>
    /// Verifies that the user-variable capability is immutable provider state.
    /// </summary>
    [Fact]
    public void RequireUserVariables_stores_the_requirement()
    {
        // Arrange
        Action<MySqlDbContextOptionsBuilder> configure = options => options.RequireUserVariables();

        // Act
        var extension = BuildExtension(configure);

        // Assert
        Assert.True(extension.UserVariablesRequired);
    }

    /// <summary>
    /// Verifies that requiring the capability before connection configuration
    /// survives the later provider options clone.
    /// </summary>
    [Fact]
    public void RequireUserVariables_before_UseMySql_is_preserved()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();
        new MySqlDbContextOptionsBuilder(builder).RequireUserVariables();

        // Act
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        var actual = builder.Options.FindExtension<MySqlOptionsExtension>();

        // Assert
        var extension = Assert.IsType<MySqlOptionsExtension>(
            actual);

        Assert.True(extension.UserVariablesRequired);
    }

    /// <summary>
    /// Verifies that repeating the one-way capability requirement is idempotent.
    /// </summary>
    [Fact]
    public void RequireUserVariables_is_idempotent()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();
        var providerBuilder = new MySqlDbContextOptionsBuilder(builder);
        var first = providerBuilder.RequireUserVariables();
        var second = providerBuilder.RequireUserVariables();

        // Act
        var actual = builder.Options.FindExtension<MySqlOptionsExtension>();

        // Assert
        var extension = Assert.IsType<MySqlOptionsExtension>(
            actual);

        Assert.Same(providerBuilder, first);
        Assert.Same(providerBuilder, second);
        Assert.True(extension.UserVariablesRequired);
    }

    /// <summary>
    /// Verifies that a requirement recorded before a borrowed connection is
    /// configured validates that exact later input.
    /// </summary>
    [Fact]
    public void RequireUserVariables_before_borrowed_connection_validates_the_later_input()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();
        new MySqlDbContextOptionsBuilder(builder).RequireUserVariables();
        using var connection = new MySqlConnection(
            "Server=localhost;Database=doka;GuidFormat=Binary16;AllowUserVariables=true;");

        // Act
        builder.UseMySql(
            connection,
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        var actual = builder.Options.FindExtension<MySqlOptionsExtension>();

        // Assert
        var extension = Assert.IsType<MySqlOptionsExtension>(
            actual);

        Assert.True(extension.UserVariablesRequired);
        Assert.Same(connection, extension.Connection);
        Assert.Equal(ParameterTranslationMode.MultipleParameters, extension.ParameterizedCollectionMode);
    }

    /// <summary>
    /// Verifies that a later borrowed input cannot bypass an already-recorded
    /// user-variable requirement.
    /// </summary>
    [Fact]
    public void RequireUserVariables_before_borrowed_connection_rejects_missing_capability()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();
        new MySqlDbContextOptionsBuilder(builder).RequireUserVariables();
        using var connection = new MySqlConnection(
            "Server=localhost;Database=doka;GuidFormat=Binary16;");

        Action act = () =>
            builder.UseMySql(
                connection,
                MySqlServerVersion.MySql(new Version(8, 4, 0)));

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlConnectionContractException>(recordedException);
        Assert.Equal(MySqlConfigurationFailureReason.UserVariablesUnavailable, exception.Reason);
    }

    /// <summary>
    /// Verifies that the data-source overload stores the caller-owned data source.
    /// </summary>
    [Fact]
    public void UseMySql_stores_data_source_and_server_version()
    {
        // Arrange
        using var dataSource = new MySqlDataSourceBuilder(
            "Server=localhost;Database=doka;User ID=root;Password=password;GuidFormat=Binary16;").Build();

        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        // Act
        builder.UseMySql(
            dataSource,
            serverVersion,
            options => options.UseParameterizedCollectionMode(ParameterTranslationMode.MultipleParameters));

        var actual = builder.Options.FindExtension<MySqlOptionsExtension>();

        // Assert
        var extension = Assert.IsType<MySqlOptionsExtension>(actual);
        Assert.Same(dataSource, extension.DataSource);
        Assert.Null(extension.ConnectionString);
        Assert.Null(extension.Connection);
        Assert.Equal(serverVersion, extension.ServerVersion);
        Assert.Equal(ParameterTranslationMode.MultipleParameters, extension.ParameterizedCollectionMode);
    }

    /// <summary>
    /// Verifies that retry configuration validates invalid arguments.
    /// </summary>
    [Fact]
    public void EnableRetryOnFailure_rejects_invalid_arguments()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        Action act = () => builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => options.EnableRetryOnFailure(maxRetryCount: 0));

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<ArgumentOutOfRangeException>(recordedException);
        Assert.Equal("maxRetryCount", exception.ParamName);
    }

    /// <summary>
    /// Verifies that <c>CommandTimeout</c> mutates the relational options snapshot.
    /// </summary>
    [Fact]
    public void CommandTimeout_stores_value_on_extension()
    {
        // Arrange
        const int timeout = 45;

        // Act
        var extension = BuildExtension(options => options.CommandTimeout(timeout));

        // Assert
        Assert.Equal(timeout, extension.CommandTimeout);
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
        // Arrange
        Action act = () =>
            BuildExtension(options => options.CommandTimeout(commandTimeout));

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<ArgumentOutOfRangeException>(recordedException);
        Assert.Equal(nameof(commandTimeout), exception.ParamName);
    }

    /// <summary>
    /// Verifies that <c>MaxBatchSize</c> mutates the relational options snapshot.
    /// </summary>
    [Fact]
    public void MaxBatchSize_stores_value_on_extension()
    {
        // Arrange
        const int batchSize = 128;

        // Act
        var extension = BuildExtension(options => options.MaxBatchSize(batchSize));

        // Assert
        Assert.Equal(batchSize, extension.MaxBatchSize);
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
        // Arrange
        Action act = () =>
            BuildExtension(options => options.MaxBatchSize(maxBatchSize));

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<ArgumentOutOfRangeException>(recordedException);
        Assert.Equal(nameof(maxBatchSize), exception.ParamName);
    }

    /// <summary>
    /// Verifies that <c>MinBatchSize</c> mutates the relational options snapshot.
    /// </summary>
    [Fact]
    public void MinBatchSize_stores_value_on_extension()
    {
        // Arrange
        const int batchSize = 2;

        // Act
        var extension = BuildExtension(options => options.MinBatchSize(batchSize));

        // Assert
        Assert.Equal(batchSize, extension.MinBatchSize);
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
        // Arrange
        Action act = () =>
            BuildExtension(options => options.MinBatchSize(minBatchSize));

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<ArgumentOutOfRangeException>(recordedException);
        Assert.Equal(nameof(minBatchSize), exception.ParamName);
    }

    /// <summary>
    /// Verifies that <c>MigrationsHistoryTable</c> preserves the relational schema
    /// value for diagnostic runtime validation.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("doka_meta")]
    public void MigrationsHistoryTable_preserves_schema_for_runtime_validation(
        string schema
    )
    {
        // Arrange: The theory supplies schema.

        // Act
        var extension = BuildExtension(options =>
            options.MigrationsHistoryTable("__custom_history", schema));

        // Assert
        Assert.Equal("__custom_history", extension.MigrationsHistoryTableName);
        Assert.Equal(schema, extension.MigrationsHistoryTableSchema);
    }

    /// <summary>
    /// Verifies that <c>MigrationsHistoryTable</c> stores only the name when schema is omitted.
    /// </summary>
    [Fact]
    public void MigrationsHistoryTable_omits_schema_when_not_supplied()
    {
        // Arrange
        const string tableName = "__custom_history";

        // Act
        var extension = BuildExtension(options => options.MigrationsHistoryTable(tableName));

        // Assert
        Assert.Equal(tableName, extension.MigrationsHistoryTableName);
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
        // Arrange
        Action act = () => BuildExtension(options => options.MigrationsHistoryTable(tableName));

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentException>(recordedException);
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
        // Arrange: The theory supplies behavior.

        // Act
        var extension = BuildExtension(options => options.UseQuerySplittingBehavior(behavior));

        // Assert
        Assert.Equal(behavior, extension.QuerySplittingBehavior);
    }

    /// <summary>
    /// Verifies that <c>UseQuerySplittingBehavior</c> rejects undefined enum values.
    /// </summary>
    [Fact]
    public void UseQuerySplittingBehavior_rejects_undefined_enum_value()
    {
        // Arrange
        const QuerySplittingBehavior invalidValue = (QuerySplittingBehavior)99;
        Action act = () =>
            BuildExtension(options => options.UseQuerySplittingBehavior(invalidValue));

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<ArgumentOutOfRangeException>(recordedException);
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
