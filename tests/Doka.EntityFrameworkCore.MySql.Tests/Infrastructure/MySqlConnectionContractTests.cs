namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the provider's ownership-aware MySqlConnector configuration
/// contract without opening a database connection.
/// </summary>
public sealed class MySqlConnectionContractTests
{
    private const string BaseConnectionString =
        "Server=localhost;Database=doka;User ID=provider;Password=secret-canary;";

    private const string CompatibleBorrowedConnectionString =
        BaseConnectionString + "GuidFormat=Binary16;";

    /// <summary>
    /// Verifies provider owned normalization applies stable defaults once.
    /// </summary>
    [Fact]
    public void Provider_owned_normalization_applies_stable_defaults_once()
    {
        // Arrange

        // Act
        var first = MySqlConnectionContract.NormalizeProviderOwned(
            BaseConnectionString,
            userVariablesRequired: false);

        var second = MySqlConnectionContract.NormalizeProviderOwned(
            first,
            userVariablesRequired: false);

        var actual = new MySqlConnectionStringBuilder(first);

        // Assert
        Assert.Equal(first, second);
        Assert.Equal(MySqlConnector.MySqlGuidFormat.Binary16, actual.GuidFormat);
        Assert.Equal(MySqlDiagnostics.DefaultDriverPoolName, actual.ApplicationName);
        Assert.False(actual.UseAffectedRows);
        Assert.False(actual.ContainsKey("Use Affected Rows"));
        Assert.False(actual.AllowUserVariables);
        Assert.False(actual.ContainsKey("Allow User Variables"));
    }

    /// <summary>
    /// Verifies provider owned normalization preserves unrelated semantic values.
    /// </summary>
    [Fact]
    public void Provider_owned_normalization_preserves_unrelated_semantic_values()
    {
        // Arrange
        var configured = new MySqlConnectionStringBuilder(BaseConnectionString)
        {
            Port = 3307,
            Pooling = true,
            MinimumPoolSize = 2,
            MaximumPoolSize = 17,
            ConnectionTimeout = 9,
            ApplicationName = "custom-pool",
            SslMode = MySqlSslMode.VerifyFull,
        };

        // Act
        var normalized = new MySqlConnectionStringBuilder(
            MySqlConnectionContract.NormalizeProviderOwned(
                configured.ConnectionString,
                userVariablesRequired: false));

        // Assert
        Assert.Equal(configured.Server, normalized.Server);
        Assert.Equal(configured.Database, normalized.Database);
        Assert.Equal(configured.UserID, normalized.UserID);
        Assert.Equal(configured.Password, normalized.Password);
        Assert.Equal(configured.Port, normalized.Port);
        Assert.Equal(configured.Pooling, normalized.Pooling);
        Assert.Equal(configured.MinimumPoolSize, normalized.MinimumPoolSize);
        Assert.Equal(configured.MaximumPoolSize, normalized.MaximumPoolSize);
        Assert.Equal(configured.ConnectionTimeout, normalized.ConnectionTimeout);
        Assert.Equal(configured.ApplicationName, normalized.ApplicationName);
        Assert.Equal(configured.SslMode, normalized.SslMode);
    }

    /// <summary>
    /// Verifies changed row semantics are rejected for provider owned strings.
    /// </summary>
    [Theory]
    [InlineData("UseAffectedRows=true")]
    [InlineData("Use Affected Rows=true")]
    public void Changed_row_semantics_are_rejected_for_provider_owned_strings(
        string option
    )
    {
        // Arrange
        Action act = () =>
            MySqlConnectionContract.NormalizeProviderOwned(
                $"{BaseConnectionString}{option};",
                userVariablesRequired: false);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlConnectionContractException>(recordedException);
        Assert.Equal(MySqlConfigurationFailureReason.ChangedRowSemanticsUnsupported, exception.Reason);
        Assert.DoesNotContain("secret-canary", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies matched row semantics are accepted through supported aliases.
    /// </summary>
    [Theory]
    [InlineData("UseAffectedRows=false")]
    [InlineData("Use Affected Rows=false")]
    public void Matched_row_semantics_are_accepted_through_supported_aliases(
        string option
    )
    {
        // Arrange

        // Act
        var normalized = MySqlConnectionContract.NormalizeProviderOwned(
            $"{BaseConnectionString}{option};",
            userVariablesRequired: false);

        var actual = new MySqlConnectionStringBuilder(normalized).UseAffectedRows;

        // Assert
        Assert.False(actual);
    }

    /// <summary>
    /// Verifies incompatible explicit guid transports are rejected for provider owned strings.
    /// </summary>
    [Theory]
    [InlineData("Default")]
    [InlineData("Char36")]
    [InlineData("LittleEndianBinary16")]
    [InlineData("TimeSwapBinary16")]
    [InlineData("None")]
    public void Incompatible_explicit_guid_transports_are_rejected_for_provider_owned_strings(
        string guidFormat
    )
    {
        // Arrange
        Action act = () =>
            MySqlConnectionContract.NormalizeProviderOwned(
                $"{BaseConnectionString}GuidFormat={guidFormat};",
                userVariablesRequired: false);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlConnectionContractException>(recordedException);
        Assert.Equal(MySqlConfigurationFailureReason.GuidTransportIncompatible, exception.Reason);
    }

    /// <summary>
    /// Verifies legacy guid transport is rejected for provider owned strings.
    /// </summary>
    [Fact]
    public void Legacy_guid_transport_is_rejected_for_provider_owned_strings()
    {
        // Arrange
        Action act = () =>
            MySqlConnectionContract.NormalizeProviderOwned(
                $"{BaseConnectionString}Old Guids=true;",
                userVariablesRequired: false);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlConnectionContractException>(recordedException);
        Assert.Equal(MySqlConfigurationFailureReason.GuidTransportIncompatible, exception.Reason);
    }

    /// <summary>
    /// Verifies explicit binary16 transport is accepted for provider owned strings.
    /// </summary>
    [Fact]
    public void Explicit_binary16_transport_is_accepted_for_provider_owned_strings()
    {
        // Arrange

        // Act
        var normalized = MySqlConnectionContract.NormalizeProviderOwned(
            CompatibleBorrowedConnectionString,
            userVariablesRequired: false);

        var actual = new MySqlConnectionStringBuilder(normalized).GuidFormat;

        // Assert
        Assert.Equal(
            MySqlConnector.MySqlGuidFormat.Binary16,
            actual);
    }

    /// <summary>
    /// Verifies required user variables are added when omitted.
    /// </summary>
    [Fact]
    public void Required_user_variables_are_added_when_omitted()
    {
        // Arrange

        // Act
        var normalized = MySqlConnectionContract.NormalizeProviderOwned(
            BaseConnectionString,
            userVariablesRequired: true);

        var actual = new MySqlConnectionStringBuilder(normalized).AllowUserVariables;

        // Assert
        Assert.True(actual);
    }

    /// <summary>
    /// Verifies required user variables accept supported true aliases.
    /// </summary>
    [Theory]
    [InlineData("AllowUserVariables=true")]
    [InlineData("Allow User Variables=true")]
    public void Required_user_variables_accept_supported_true_aliases(
        string option
    )
    {
        // Arrange

        // Act
        var normalized = MySqlConnectionContract.NormalizeProviderOwned(
            $"{BaseConnectionString}{option};",
            userVariablesRequired: true);

        var actual = new MySqlConnectionStringBuilder(normalized).AllowUserVariables;

        // Assert
        Assert.True(actual);
    }

    /// <summary>
    /// Verifies required user variables reject explicit false aliases.
    /// </summary>
    [Theory]
    [InlineData("AllowUserVariables=false")]
    [InlineData("Allow User Variables=false")]
    public void Required_user_variables_reject_explicit_false_aliases(
        string option
    )
    {
        // Arrange
        Action act = () =>
            MySqlConnectionContract.NormalizeProviderOwned(
                $"{BaseConnectionString}{option};",
                userVariablesRequired: true);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlConnectionContractException>(recordedException);
        Assert.Equal(MySqlConfigurationFailureReason.UserVariablesUnavailable, exception.Reason);
    }

    /// <summary>
    /// Verifies malformed provider owned string uses a sanitized error.
    /// </summary>
    [Fact]
    public void Malformed_provider_owned_string_uses_a_sanitized_error()
    {
        // Arrange
        Action act = () =>
            MySqlConnectionContract.NormalizeProviderOwned(
                $"{BaseConnectionString}Unsupported Secret Option=secret-canary;",
                userVariablesRequired: false);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlConnectionContractException>(recordedException);
        Assert.Equal(MySqlConfigurationFailureReason.InvalidConnectionString, exception.Reason);
        Assert.Equal("The MySQL connection configuration is invalid.", exception.Message);
        Assert.DoesNotContain("secret-canary", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies unreadable borrowed connection uses a sanitized error without state change.
    /// </summary>
    [Fact]
    public void Unreadable_borrowed_connection_uses_a_sanitized_error_without_state_change()
    {
        // Arrange
        using var connection = new ContractDbConnection(
            CompatibleBorrowedConnectionString,
            throwOnConnectionStringRead: true);

        connection.Open();
        Action act = () =>
            MySqlConnectionContract.ValidateBorrowed(connection, userVariablesRequired: false);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlConnectionContractException>(recordedException);
        Assert.Equal(MySqlConfigurationFailureReason.InvalidConnectionString, exception.Reason);
        Assert.Equal("The MySQL connection configuration is invalid.", exception.Message);
        Assert.Equal(ConnectionState.Open, connection.State);
    }

    /// <summary>
    /// Verifies compatible borrowed connection is accepted without mutation.
    /// </summary>
    [Fact]
    public void Compatible_borrowed_connection_is_accepted_without_mutation()
    {
        // Arrange
        using var connection = new ContractDbConnection(CompatibleBorrowedConnectionString);
        var originalConnectionString = connection.ConnectionString;

        // Act
        MySqlConnectionContract.ValidateBorrowed(connection, userVariablesRequired: false);

        // Assert
        Assert.Equal(originalConnectionString, connection.ConnectionString);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>
    /// Verifies compatible open borrowed connection is accepted without state change.
    /// </summary>
    [Fact]
    public void Compatible_open_borrowed_connection_is_accepted_without_state_change()
    {
        // Arrange
        using var connection = new ContractDbConnection(
            CompatibleBorrowedConnectionString + "AllowUserVariables=true;");

        connection.Open();

        // Act
        MySqlConnectionContract.ValidateBorrowed(connection, userVariablesRequired: true);

        // Assert
        Assert.Equal(ConnectionState.Open, connection.State);
    }

    /// <summary>
    /// Verifies incompatible borrowed guid transport is rejected without state change.
    /// </summary>
    [Theory]
    [InlineData("GuidFormat=Default")]
    [InlineData("GuidFormat=Char36")]
    [InlineData("GuidFormat=LittleEndianBinary16")]
    [InlineData("GuidFormat=TimeSwapBinary16")]
    [InlineData("GuidFormat=None")]
    [InlineData("Old Guids=true")]
    public void Incompatible_borrowed_guid_transport_is_rejected_without_state_change(
        string option
    )
    {
        // Arrange
        using var connection = new ContractDbConnection($"{BaseConnectionString}{option};");
        connection.Open();
        Action act = () =>
            MySqlConnectionContract.ValidateBorrowed(connection, userVariablesRequired: false);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlConnectionContractException>(recordedException);
        Assert.Equal(MySqlConfigurationFailureReason.GuidTransportIncompatible, exception.Reason);
        Assert.Equal(ConnectionState.Open, connection.State);
    }

    /// <summary>
    /// Verifies borrowed connection with changed row semantics is rejected without mutation.
    /// </summary>
    [Fact]
    public void Borrowed_connection_with_changed_row_semantics_is_rejected_without_mutation()
    {
        // Arrange
        var configured = CompatibleBorrowedConnectionString + "UseAffectedRows=true;";
        using var connection = new ContractDbConnection(configured);
        Action act = () =>
            MySqlConnectionContract.ValidateBorrowed(connection, userVariablesRequired: false);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlConnectionContractException>(recordedException);
        Assert.Equal(MySqlConfigurationFailureReason.ChangedRowSemanticsUnsupported, exception.Reason);
        Assert.Equal(configured, connection.ConnectionString);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>
    /// Verifies borrowed connection without required user variables is rejected.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("AllowUserVariables=false;")]
    public void Borrowed_connection_without_required_user_variables_is_rejected(
        string option
    )
    {
        // Arrange
        using var connection = new ContractDbConnection(
            CompatibleBorrowedConnectionString + option);

        Action act = () =>
            MySqlConnectionContract.ValidateBorrowed(connection, userVariablesRequired: true);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlConnectionContractException>(recordedException);
        Assert.Equal(MySqlConfigurationFailureReason.UserVariablesUnavailable, exception.Reason);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>
    /// Verifies compatible borrowed data source is accepted without reconstruction.
    /// </summary>
    [Fact]
    public void Compatible_borrowed_data_source_is_accepted_without_reconstruction()
    {
        // Arrange
        using var dataSource = new MySqlDataSourceBuilder(
            CompatibleBorrowedConnectionString + "AllowUserVariables=true;").Build();

        // Act
        var extension = new MySqlOptionsExtension()
            .WithDataSource(dataSource)
            .WithUserVariablesRequired();

        // Assert
        Assert.Same(dataSource, extension.DataSource);
        Assert.True(extension.UserVariablesRequired);
    }

    /// <summary>
    /// Verifies incompatible borrowed data source is rejected without reconstruction.
    /// </summary>
    [Theory]
    [InlineData("GuidFormat=Default;")]
    [InlineData("GuidFormat=Char36;")]
    [InlineData("GuidFormat=LittleEndianBinary16;")]
    [InlineData("GuidFormat=TimeSwapBinary16;")]
    [InlineData("GuidFormat=None;")]
    [InlineData("GuidFormat=Binary16;")]
    [InlineData("GuidFormat=Binary16;UseAffectedRows=true;")]
    [InlineData("GuidFormat=Binary16;AllowUserVariables=false;")]
    public void Incompatible_borrowed_data_source_is_rejected_without_reconstruction(
        string options
    )
    {
        // Arrange
        using var dataSource = new MySqlDataSourceBuilder(BaseConnectionString + options).Build();
        Action act = () =>
            new MySqlOptionsExtension()
                .WithDataSource(dataSource)
                .WithUserVariablesRequired();

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<MySqlConnectionContractException>(recordedException);
    }

    private sealed class ContractDbConnection : DbConnection
    {
        private string _connectionString;
        private ConnectionState _state;
        private readonly bool _throwOnConnectionStringRead;

        public ContractDbConnection(
            string connectionString,
            bool throwOnConnectionStringRead = false
        )
        {
            _connectionString = connectionString;
            _throwOnConnectionStringRead = throwOnConnectionStringRead;
        }

        [AllowNull]
        public override string ConnectionString
        {
            get => _throwOnConnectionStringRead
                ? throw new InvalidOperationException("secret-canary")
                : _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public override string Database => "doka";

        public override string DataSource => "contract";

        public override string ServerVersion => "8.4.0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(
            string databaseName
        ) => throw new NotSupportedException();

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open() => _state = ConnectionState.Open;

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel
        ) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
