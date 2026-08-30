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

    [Fact]
    public void Provider_owned_normalization_applies_stable_defaults_once()
    {
        var first = MySqlConnectionContract.NormalizeProviderOwned(
            BaseConnectionString,
            userVariablesRequired: false);

        var second = MySqlConnectionContract.NormalizeProviderOwned(
            first,
            userVariablesRequired: false);

        var actual = new MySqlConnectionStringBuilder(first);

        Assert.Equal(first, second);
        Assert.Equal(MySqlConnector.MySqlGuidFormat.Binary16, actual.GuidFormat);
        Assert.Equal(MySqlDiagnostics.DefaultDriverPoolName, actual.ApplicationName);
        Assert.False(actual.UseAffectedRows);
        Assert.False(actual.ContainsKey("Use Affected Rows"));
        Assert.False(actual.AllowUserVariables);
        Assert.False(actual.ContainsKey("Allow User Variables"));
    }

    [Fact]
    public void Provider_owned_normalization_preserves_unrelated_semantic_values()
    {
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

        var normalized = new MySqlConnectionStringBuilder(
            MySqlConnectionContract.NormalizeProviderOwned(
                configured.ConnectionString,
                userVariablesRequired: false));

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

    [Theory]
    [InlineData("UseAffectedRows=true")]
    [InlineData("Use Affected Rows=true")]
    public void Changed_row_semantics_are_rejected_for_provider_owned_strings(
        string option
    )
    {
        var exception = Assert.Throws<MySqlConnectionContractException>(() =>
            MySqlConnectionContract.NormalizeProviderOwned(
                $"{BaseConnectionString}{option};",
                userVariablesRequired: false));

        Assert.Equal(MySqlConfigurationFailureReason.ChangedRowSemanticsUnsupported, exception.Reason);
        Assert.DoesNotContain("secret-canary", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("UseAffectedRows=false")]
    [InlineData("Use Affected Rows=false")]
    public void Matched_row_semantics_are_accepted_through_supported_aliases(
        string option
    )
    {
        var normalized = MySqlConnectionContract.NormalizeProviderOwned(
            $"{BaseConnectionString}{option};",
            userVariablesRequired: false);

        Assert.False(new MySqlConnectionStringBuilder(normalized).UseAffectedRows);
    }

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
        var exception = Assert.Throws<MySqlConnectionContractException>(() =>
            MySqlConnectionContract.NormalizeProviderOwned(
                $"{BaseConnectionString}GuidFormat={guidFormat};",
                userVariablesRequired: false));

        Assert.Equal(MySqlConfigurationFailureReason.GuidTransportIncompatible, exception.Reason);
    }

    [Fact]
    public void Legacy_guid_transport_is_rejected_for_provider_owned_strings()
    {
        var exception = Assert.Throws<MySqlConnectionContractException>(() =>
            MySqlConnectionContract.NormalizeProviderOwned(
                $"{BaseConnectionString}Old Guids=true;",
                userVariablesRequired: false));

        Assert.Equal(MySqlConfigurationFailureReason.GuidTransportIncompatible, exception.Reason);
    }

    [Fact]
    public void Explicit_binary16_transport_is_accepted_for_provider_owned_strings()
    {
        var normalized = MySqlConnectionContract.NormalizeProviderOwned(
            CompatibleBorrowedConnectionString,
            userVariablesRequired: false);

        Assert.Equal(
            MySqlConnector.MySqlGuidFormat.Binary16,
            new MySqlConnectionStringBuilder(normalized).GuidFormat);
    }

    [Fact]
    public void Required_user_variables_are_added_when_omitted()
    {
        var normalized = MySqlConnectionContract.NormalizeProviderOwned(
            BaseConnectionString,
            userVariablesRequired: true);

        Assert.True(new MySqlConnectionStringBuilder(normalized).AllowUserVariables);
    }

    [Theory]
    [InlineData("AllowUserVariables=true")]
    [InlineData("Allow User Variables=true")]
    public void Required_user_variables_accept_supported_true_aliases(
        string option
    )
    {
        var normalized = MySqlConnectionContract.NormalizeProviderOwned(
            $"{BaseConnectionString}{option};",
            userVariablesRequired: true);

        Assert.True(new MySqlConnectionStringBuilder(normalized).AllowUserVariables);
    }

    [Theory]
    [InlineData("AllowUserVariables=false")]
    [InlineData("Allow User Variables=false")]
    public void Required_user_variables_reject_explicit_false_aliases(
        string option
    )
    {
        var exception = Assert.Throws<MySqlConnectionContractException>(() =>
            MySqlConnectionContract.NormalizeProviderOwned(
                $"{BaseConnectionString}{option};",
                userVariablesRequired: true));

        Assert.Equal(MySqlConfigurationFailureReason.UserVariablesUnavailable, exception.Reason);
    }

    [Fact]
    public void Malformed_provider_owned_string_uses_a_sanitized_error()
    {
        var exception = Assert.Throws<MySqlConnectionContractException>(() =>
            MySqlConnectionContract.NormalizeProviderOwned(
                $"{BaseConnectionString}Unsupported Secret Option=secret-canary;",
                userVariablesRequired: false));

        Assert.Equal(MySqlConfigurationFailureReason.InvalidConnectionString, exception.Reason);
        Assert.Equal("The MySQL connection configuration is invalid.", exception.Message);
        Assert.DoesNotContain("secret-canary", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Unreadable_borrowed_connection_uses_a_sanitized_error_without_state_change()
    {
        using var connection = new ContractDbConnection(
            CompatibleBorrowedConnectionString,
            throwOnConnectionStringRead: true);
        connection.Open();

        var exception = Assert.Throws<MySqlConnectionContractException>(() =>
            MySqlConnectionContract.ValidateBorrowed(connection, userVariablesRequired: false));

        Assert.Equal(MySqlConfigurationFailureReason.InvalidConnectionString, exception.Reason);
        Assert.Equal("The MySQL connection configuration is invalid.", exception.Message);
        Assert.Equal(ConnectionState.Open, connection.State);
    }

    [Fact]
    public void Compatible_borrowed_connection_is_accepted_without_mutation()
    {
        using var connection = new ContractDbConnection(CompatibleBorrowedConnectionString);
        var originalConnectionString = connection.ConnectionString;

        MySqlConnectionContract.ValidateBorrowed(connection, userVariablesRequired: false);

        Assert.Equal(originalConnectionString, connection.ConnectionString);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void Compatible_open_borrowed_connection_is_accepted_without_state_change()
    {
        using var connection = new ContractDbConnection(
            CompatibleBorrowedConnectionString + "AllowUserVariables=true;");
        connection.Open();

        MySqlConnectionContract.ValidateBorrowed(connection, userVariablesRequired: true);

        Assert.Equal(ConnectionState.Open, connection.State);
    }

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
        using var connection = new ContractDbConnection($"{BaseConnectionString}{option};");
        connection.Open();

        var exception = Assert.Throws<MySqlConnectionContractException>(() =>
            MySqlConnectionContract.ValidateBorrowed(connection, userVariablesRequired: false));

        Assert.Equal(MySqlConfigurationFailureReason.GuidTransportIncompatible, exception.Reason);
        Assert.Equal(ConnectionState.Open, connection.State);
    }

    [Fact]
    public void Borrowed_connection_with_changed_row_semantics_is_rejected_without_mutation()
    {
        var configured = CompatibleBorrowedConnectionString + "UseAffectedRows=true;";
        using var connection = new ContractDbConnection(configured);

        var exception = Assert.Throws<MySqlConnectionContractException>(() =>
            MySqlConnectionContract.ValidateBorrowed(connection, userVariablesRequired: false));

        Assert.Equal(MySqlConfigurationFailureReason.ChangedRowSemanticsUnsupported, exception.Reason);
        Assert.Equal(configured, connection.ConnectionString);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AllowUserVariables=false;")]
    public void Borrowed_connection_without_required_user_variables_is_rejected(
        string option
    )
    {
        using var connection = new ContractDbConnection(
            CompatibleBorrowedConnectionString + option);

        var exception = Assert.Throws<MySqlConnectionContractException>(() =>
            MySqlConnectionContract.ValidateBorrowed(connection, userVariablesRequired: true));

        Assert.Equal(MySqlConfigurationFailureReason.UserVariablesUnavailable, exception.Reason);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void Compatible_borrowed_data_source_is_accepted_without_reconstruction()
    {
        using var dataSource = new MySqlDataSourceBuilder(
            CompatibleBorrowedConnectionString + "AllowUserVariables=true;").Build();

        var extension = new MySqlOptionsExtension()
            .WithDataSource(dataSource)
            .WithUserVariablesRequired();

        Assert.Same(dataSource, extension.DataSource);
        Assert.True(extension.UserVariablesRequired);
    }

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
        using var dataSource = new MySqlDataSourceBuilder(BaseConnectionString + options).Build();

        Assert.Throws<MySqlConnectionContractException>(() =>
            new MySqlOptionsExtension()
                .WithDataSource(dataSource)
                .WithUserVariablesRequired());
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
