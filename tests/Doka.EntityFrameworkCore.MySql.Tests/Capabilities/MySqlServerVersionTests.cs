namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers server-version parsing and capability resolution.
/// </summary>
public sealed class MySqlServerVersionTests
{
    /// <summary>
    /// Verifies the provider support profile for the supported MySQL line.
    /// </summary>
    [Fact]
    public void MySql_factory_creates_mysql_capabilities()
    {
        // Arrange
        var input = new Version(8, 4, 0);

        // Act
        var serverVersion = MySqlServerVersion.MySql(input);

        // Assert
        Assert.False(serverVersion.IsMariaDb);
        AssertMySqlProviderProfile(serverVersion);
    }

    /// <summary>
    /// Verifies that MySQL 9.7 retains the complete MySQL provider profile.
    /// </summary>
    [Fact]
    public void MySql97_factory_creates_the_expected_mysql_capabilities()
    {
        // Arrange
        var input = new Version(9, 7, 0);

        // Act
        var serverVersion = MySqlServerVersion.MySql(input);

        // Assert
        Assert.False(serverVersion.IsMariaDb);
        AssertMySqlProviderProfile(serverVersion);
    }

    /// <summary>
    /// Verifies the provider support profile for the supported MariaDB 11.8 line.
    /// </summary>
    [Fact]
    public void MariaDb_factory_creates_mariadb_capabilities()
    {
        // Arrange
        var input = new Version(11, 8, 0);

        // Act
        var serverVersion = MySqlServerVersion.MariaDb(input);

        // Assert
        Assert.True(serverVersion.IsMariaDb);
        AssertMariaDbProviderProfile(serverVersion);
    }

    /// <summary>
    /// Verifies that MariaDB 11.4 uses the same provider support profile as MariaDB 11.8.
    /// </summary>
    [Fact]
    public void MariaDb114_factory_creates_the_expected_mariadb_capabilities()
    {
        // Arrange
        var input = new Version(11, 4, 0);

        // Act
        var serverVersion = MySqlServerVersion.MariaDb(input);

        // Assert
        Assert.True(serverVersion.IsMariaDb);
        AssertMariaDbProviderProfile(serverVersion);
    }

    /// <summary>
    /// Verifies that MariaDB 10.11 retains the complete MariaDB provider profile.
    /// </summary>
    [Fact]
    public void MariaDb1011_factory_creates_the_expected_mariadb_capabilities()
    {
        // Arrange
        var input = new Version(10, 11, 0);

        // Act
        var serverVersion = MySqlServerVersion.MariaDb(input);

        // Assert
        Assert.True(serverVersion.IsMariaDb);
        AssertMariaDbProviderProfile(serverVersion);
    }

    /// <summary>
    /// Verifies that MariaDB 12.3 retains the complete MariaDB provider profile.
    /// </summary>
    [Fact]
    public void MariaDb123_factory_creates_the_expected_mariadb_capabilities()
    {
        // Arrange
        var input = new Version(12, 3, 0);

        // Act
        var serverVersion = MySqlServerVersion.MariaDb(input);

        // Assert
        Assert.True(serverVersion.IsMariaDb);
        AssertMariaDbProviderProfile(serverVersion);
    }

    /// <summary>
    /// Verifies that MariaDB version strings are detected correctly.
    /// </summary>
    [Fact]
    public void Parse_recognizes_mariadb_version_strings()
    {
        // Arrange
        const string input = "11.8.1-MariaDB-ubu2404";

        // Act
        var serverVersion = MySqlServerVersion.Parse(input);

        // Assert
        Assert.True(serverVersion.IsMariaDb);
        Assert.Equal(new Version(11, 8, 1), serverVersion.Version);
    }

    /// <summary>
    /// Verifies that MariaDB's legacy client-compatibility prefix cannot mask
    /// the authoritative release token returned by the server.
    /// </summary>
    [Fact]
    public void Parse_ignores_mariadb_legacy_compatibility_prefix()
    {
        // Arrange
        const string input = "5.5.5-10.11.18-MariaDB-ubu2204";

        // Act
        var serverVersion = MySqlServerVersion.Parse(input);

        // Assert
        Assert.True(serverVersion.IsMariaDb);
        Assert.Equal(new Version(10, 11, 18), serverVersion.Version);
        Assert.Equal(MySqlServerVersionSupportStatus.Supported, serverVersion.SupportStatus);
    }

    /// <summary>
    /// Verifies that malformed version strings fail instead of producing a best guess.
    /// </summary>
    [Theory]
    [InlineData("MariaDB")]
    [InlineData("mysql-8x0")]
    [InlineData("version8")]
    public void Parse_rejects_malformed_version_strings(
        string rawServerVersion
    )
    {
        // Arrange
        Action act = () => MySqlServerVersion.Parse(rawServerVersion);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<ArgumentException>(recordedException);
        Assert.Equal("serverVersion", exception.ParamName);
    }

    /// <summary>
    /// Verifies that connection-based auto-detect fails when the connection exposes no version.
    /// </summary>
    [Fact]
    public void AutoDetect_connection_rejects_missing_server_version()
    {
        // Arrange
        using var connection = new StubDbConnection(string.Empty);
        Action act = () => MySqlServerVersion.AutoDetect(connection);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Equal("The supplied connection did not expose a server version.", exception.Message);
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.False(connection.WasDisposed);
    }

    /// <summary>
    /// Verifies that both connection overloads reject a closed driver connection
    /// without opening it or taking ownership of it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutoDetect_connection_rejects_closed_driver_connection(
        bool useExplicitCompatibilityMode
    )
    {
        // Arrange
        await using var connection = new MySqlConnection();
        Func<object?> act1 = () => useExplicitCompatibilityMode
            ? MySqlServerVersion.AutoDetect(connection, MySqlServerVersionCompatibilityMode.AllowUnsupported)
            : MySqlServerVersion.AutoDetect(connection);

        Func<object?> act2 = () => connection.ServerVersion;

        // Act
        var recordedException1 = Record.Exception(act1);
        var recordedException2 = Record.Exception(act2);

        // Assert
        Assert.IsType<InvalidOperationException>(recordedException1);
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.IsType<InvalidOperationException>(recordedException2);
    }

    /// <summary>
    /// Verifies that both connection overloads preserve the caller's open
    /// connection and the selected compatibility mode.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoDetect_connection_preserves_caller_ownership_and_compatibility_mode(
        bool useExplicitCompatibilityMode
    )
    {
        // Arrange
        using var connection = new StubDbConnection("8.4.9");

        // Act
        var detected = useExplicitCompatibilityMode
            ? MySqlServerVersion.AutoDetect(connection, MySqlServerVersionCompatibilityMode.AllowUnsupported)
            : MySqlServerVersion.AutoDetect(connection);

        // Assert
        Assert.Equal(new Version(8, 4, 9), detected.Version);
        Assert.Equal(
            useExplicitCompatibilityMode
                ? MySqlServerVersionCompatibilityMode.AllowUnsupported
                : MySqlServerVersionCompatibilityMode.SupportedOnly,
            detected.CompatibilityMode);
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.False(connection.WasDisposed);
    }

    /// <summary>
    /// Verifies that both connection overloads reject a missing connection.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoDetect_connection_rejects_null(
        bool useExplicitCompatibilityMode
    )
    {
        // Arrange
        Func<object?> act = () => useExplicitCompatibilityMode
            ? MySqlServerVersion.AutoDetect(null!, MySqlServerVersionCompatibilityMode.AllowUnsupported)
            : MySqlServerVersion.AutoDetect((DbConnection)null!);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<ArgumentNullException>(recordedException);
        Assert.Equal("connection", exception.ParamName);
    }

    /// <summary>
    /// Verifies that connection-string auto-detection rejects unusable input
    /// before constructing or opening a connection.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AutoDetect_connection_string_rejects_missing_input(
        string? connectionString
    )
    {
        // Arrange
        Action act = () => MySqlServerVersion.AutoDetect(connectionString!);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsAssignableFrom<ArgumentException>(recordedException);
        Assert.Equal("connectionString", exception.ParamName);
    }

    /// <summary>
    /// Verifies that malformed connection options fail before network I/O.
    /// </summary>
    [Theory]
    [InlineData("this is not a connection string")]
    [InlineData("Server=localhost;UnsupportedDokaOption=true")]
    [InlineData("Server=localhost;Port=not-a-number")]
    public void AutoDetect_connection_string_rejects_malformed_options(
        string connectionString
    )
    {
        // Arrange
        Action act = () => MySqlServerVersion.AutoDetect(connectionString);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(recordedException);
    }

    /// <summary>
    /// Verifies every support-policy boundary by release line rather than patch.
    /// </summary>
    [Theory]
    [InlineData(false, 8, 3, 99, MySqlServerVersionSupportStatus.Legacy)]
    [InlineData(false, 8, 4, 99, MySqlServerVersionSupportStatus.Supported)]
    [InlineData(false, 8, 5, 0, MySqlServerVersionSupportStatus.Unvalidated)]
    [InlineData(false, 9, 6, 99, MySqlServerVersionSupportStatus.Unvalidated)]
    [InlineData(false, 9, 7, 99, MySqlServerVersionSupportStatus.Supported)]
    [InlineData(false, 9, 8, 0, MySqlServerVersionSupportStatus.Future)]
    [InlineData(true, 10, 10, 99, MySqlServerVersionSupportStatus.Legacy)]
    [InlineData(true, 10, 11, 99, MySqlServerVersionSupportStatus.Supported)]
    [InlineData(true, 11, 3, 99, MySqlServerVersionSupportStatus.Unvalidated)]
    [InlineData(true, 11, 4, 99, MySqlServerVersionSupportStatus.Supported)]
    [InlineData(true, 11, 5, 0, MySqlServerVersionSupportStatus.Unvalidated)]
    [InlineData(true, 11, 7, 99, MySqlServerVersionSupportStatus.Unvalidated)]
    [InlineData(true, 11, 8, 99, MySqlServerVersionSupportStatus.Supported)]
    [InlineData(true, 11, 9, 0, MySqlServerVersionSupportStatus.Unvalidated)]
    [InlineData(true, 12, 2, 99, MySqlServerVersionSupportStatus.Unvalidated)]
    [InlineData(true, 12, 3, 99, MySqlServerVersionSupportStatus.Supported)]
    [InlineData(true, 12, 4, 0, MySqlServerVersionSupportStatus.Future)]
    public void Server_version_release_lines_are_classified_explicitly(
        bool isMariaDb,
        int major,
        int minor,
        int patch,
        MySqlServerVersionSupportStatus expectedStatus
    )
    {
        // Arrange
        var version = new Version(major, minor, patch);

        // Act
        var serverVersion = isMariaDb
            ? MySqlServerVersion.MariaDb(version)
            : MySqlServerVersion.MySql(version);

        // Assert
        Assert.Equal(expectedStatus, serverVersion.SupportStatus);
        Assert.Equal(MySqlServerVersionCompatibilityMode.SupportedOnly, serverVersion.CompatibilityMode);
    }

    /// <summary>
    /// Verifies that explicit compatibility mode survives server-version parsing.
    /// </summary>
    [Fact]
    public void Parse_preserves_explicit_unsupported_compatibility_mode()
    {
        // Arrange
        const string input = "8.0.44";

        // Act
        var serverVersion = MySqlServerVersion.Parse(
            input,
            MySqlServerVersionCompatibilityMode.AllowUnsupported);

        // Assert
        Assert.Equal(MySqlServerVersionSupportStatus.Legacy, serverVersion.SupportStatus);
        Assert.Equal(MySqlServerVersionCompatibilityMode.AllowUnsupported, serverVersion.CompatibilityMode);
    }

    /// <summary>
    /// Verifies that undefined compatibility-mode values are rejected.
    /// </summary>
    [Fact]
    public void Factory_rejects_undefined_compatibility_mode()
    {
        // Arrange
        Action act = () =>
            MySqlServerVersion.MySql(
                new Version(8, 4, 0),
                (MySqlServerVersionCompatibilityMode)int.MaxValue);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(recordedException);
    }

    private static void AssertMySqlProviderProfile(
        MySqlServerVersion serverVersion
    )
    {
        Assert.Equal(ProviderSupportStatus.Native, serverVersion.Profile.GetSupport(ProviderCapability.JsonColumns));
        Assert.Equal(
            ProviderSupportStatus.UnsupportedByEngine,
            serverVersion.Profile.GetSupport(ProviderCapability.ReturningClause));
        Assert.Equal(ProviderSupportStatus.Native, serverVersion.Profile.GetSupport(ProviderCapability.Savepoints));
        Assert.Equal(
            ProviderSupportStatus.Native,
            serverVersion.Profile.GetSupport(ProviderCapability.GeneratedColumnNullabilityClause));
        Assert.Equal(
            ProviderSupportStatus.Native,
            serverVersion.Profile.GetSupport(ProviderCapability.VirtualGeneratedColumns));
        Assert.Equal(
            ProviderSupportStatus.Native,
            serverVersion.Profile.GetSupport(ProviderCapability.StoredGeneratedColumns));
        Assert.Equal(
            ProviderSupportStatus.Native,
            serverVersion.Profile.GetSupport(ProviderCapability.CommonTableExpressions));
        Assert.Equal(
            ProviderSupportStatus.Emulated,
            serverVersion.Profile.GetSupport(ProviderCapability.TemporalTables));
        Assert.Equal(
            ProviderSupportStatus.Native,
            serverVersion.Profile.GetSupport(ProviderCapability.SpatialColumnSridEnforcement));
        Assert.Equal(ProviderSupportStatus.Emulated, serverVersion.Profile.GetSupport(ProviderCapability.Sequences));
        Assert.Equal(ProviderSupportStatus.Native, serverVersion.Profile.GetSupport(ProviderCapability.RenameColumn));
        Assert.Equal(
            ProviderSupportStatus.Native,
            serverVersion.Profile.GetSupport(ProviderCapability.LateralDerivedTables));
        Assert.Equal(
            ProviderSupportStatus.Emulated,
            serverVersion.Profile.GetSupport(ProviderCapability.SelfReferencingMutations));
        Assert.Equal(
            ProviderSupportStatus.Native,
            serverVersion.Profile.GetSupport(ProviderCapability.FunctionalIndexScaffolding));
    }

    private static void AssertMariaDbProviderProfile(
        MySqlServerVersion serverVersion
    )
    {
        Assert.Equal(ProviderSupportStatus.Emulated, serverVersion.Profile.GetSupport(ProviderCapability.JsonColumns));
        Assert.Equal(
            ProviderSupportStatus.Native,
            serverVersion.Profile.GetSupport(ProviderCapability.ReturningClause));
        Assert.Equal(ProviderSupportStatus.Native, serverVersion.Profile.GetSupport(ProviderCapability.Savepoints));
        Assert.Equal(
            ProviderSupportStatus.UnsupportedByEngine,
            serverVersion.Profile.GetSupport(ProviderCapability.GeneratedColumnNullabilityClause));
        Assert.Equal(
            ProviderSupportStatus.Native,
            serverVersion.Profile.GetSupport(ProviderCapability.VirtualGeneratedColumns));
        Assert.Equal(
            ProviderSupportStatus.Native,
            serverVersion.Profile.GetSupport(ProviderCapability.StoredGeneratedColumns));
        Assert.Equal(
            ProviderSupportStatus.Native,
            serverVersion.Profile.GetSupport(ProviderCapability.CommonTableExpressions));
        Assert.Equal(
            ProviderSupportStatus.Native,
            serverVersion.Profile.GetSupport(ProviderCapability.TemporalTables));
        Assert.Equal(
            ProviderSupportStatus.Emulated,
            serverVersion.Profile.GetSupport(ProviderCapability.SpatialColumnSridEnforcement));
        Assert.Equal(ProviderSupportStatus.Native, serverVersion.Profile.GetSupport(ProviderCapability.Sequences));
        Assert.Equal(ProviderSupportStatus.Native, serverVersion.Profile.GetSupport(ProviderCapability.RenameColumn));
        Assert.Equal(
            ProviderSupportStatus.UnsupportedByEngine,
            serverVersion.Profile.GetSupport(ProviderCapability.LateralDerivedTables));
        Assert.Equal(
            ProviderSupportStatus.Native,
            serverVersion.Profile.GetSupport(ProviderCapability.SelfReferencingMutations));
        Assert.Equal(
            ProviderSupportStatus.UnsupportedByEngine,
            serverVersion.Profile.GetSupport(ProviderCapability.FunctionalIndexScaffolding));
    }

    private sealed class StubDbConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Open;

        public StubDbConnection(
            string serverVersion
        )
        {
            ServerVersionValue = serverVersion;
        }

        private string ServerVersionValue { get; }

        public bool WasDisposed { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "doka";

        public override string DataSource => "localhost";

        public override string ServerVersion => ServerVersionValue;

        public override ConnectionState State => _state;

        public override void ChangeDatabase(
            string databaseName
        ) => throw new NotSupportedException();

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open() => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel
        ) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();

        protected override void Dispose(
            bool disposing
        )
        {
            WasDisposed = true;

            base.Dispose(disposing);
        }
    }
}
