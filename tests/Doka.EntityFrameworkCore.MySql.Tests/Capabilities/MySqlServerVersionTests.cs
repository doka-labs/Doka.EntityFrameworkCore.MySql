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
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        Assert.False(serverVersion.IsMariaDb);
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
            serverVersion.Profile.GetSupport(ProviderCapability.SpatialColumnSridAttribute));
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

    /// <summary>
    /// Verifies the provider support profile for the supported MariaDB 11.8 line.
    /// </summary>
    [Fact]
    public void MariaDb_factory_creates_mariadb_capabilities()
    {
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));

        Assert.True(serverVersion.IsMariaDb);
        AssertMariaDbProviderProfile(serverVersion);
    }

    /// <summary>
    /// Verifies that MariaDB 11.4 uses the same provider support profile as MariaDB 11.8.
    /// </summary>
    [Fact]
    public void MariaDb114_factory_creates_the_expected_mariadb_capabilities()
    {
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));

        Assert.True(serverVersion.IsMariaDb);
        AssertMariaDbProviderProfile(serverVersion);
    }

    /// <summary>
    /// Verifies that MariaDB version strings are detected correctly.
    /// </summary>
    [Fact]
    public void Parse_recognizes_mariadb_version_strings()
    {
        var serverVersion = MySqlServerVersion.Parse("11.8.1-MariaDB-ubu2404");

        Assert.True(serverVersion.IsMariaDb);
        Assert.Equal(new Version(11, 8, 1), serverVersion.Version);
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
        var exception = Assert.Throws<ArgumentException>(() => MySqlServerVersion.Parse(rawServerVersion));

        Assert.Equal("serverVersion", exception.ParamName);
    }

    /// <summary>
    /// Verifies that connection-based auto-detect fails when the connection exposes no version.
    /// </summary>
    [Fact]
    public void AutoDetect_connection_rejects_missing_server_version()
    {
        var connection = new StubDbConnection(string.Empty);

        var exception = Assert.Throws<InvalidOperationException>(() => MySqlServerVersion.AutoDetect(connection));

        Assert.Equal("The supplied connection did not expose a server version.", exception.Message);
    }

    /// <summary>
    /// Verifies every support-policy boundary by release line rather than patch.
    /// </summary>
    [Theory]
    [InlineData(false, 8, 3, 99, MySqlServerVersionSupportStatus.Legacy)]
    [InlineData(false, 8, 4, 99, MySqlServerVersionSupportStatus.Supported)]
    [InlineData(false, 8, 5, 0, MySqlServerVersionSupportStatus.Future)]
    [InlineData(true, 11, 3, 99, MySqlServerVersionSupportStatus.Legacy)]
    [InlineData(true, 11, 4, 99, MySqlServerVersionSupportStatus.Supported)]
    [InlineData(true, 11, 5, 0, MySqlServerVersionSupportStatus.Unvalidated)]
    [InlineData(true, 11, 7, 99, MySqlServerVersionSupportStatus.Unvalidated)]
    [InlineData(true, 11, 8, 99, MySqlServerVersionSupportStatus.Supported)]
    [InlineData(true, 11, 9, 0, MySqlServerVersionSupportStatus.Future)]
    public void Server_version_release_lines_are_classified_explicitly(
        bool isMariaDb,
        int major,
        int minor,
        int patch,
        MySqlServerVersionSupportStatus expectedStatus
    )
    {
        var version = new Version(major, minor, patch);
        var serverVersion = isMariaDb
            ? MySqlServerVersion.MariaDb(version)
            : MySqlServerVersion.MySql(version);

        Assert.Equal(expectedStatus, serverVersion.SupportStatus);
        Assert.Equal(MySqlServerVersionCompatibilityMode.SupportedOnly, serverVersion.CompatibilityMode);
    }

    /// <summary>
    /// Verifies that explicit compatibility mode survives server-version parsing.
    /// </summary>
    [Fact]
    public void Parse_preserves_explicit_unsupported_compatibility_mode()
    {
        var serverVersion = MySqlServerVersion.Parse(
            "8.0.44",
            MySqlServerVersionCompatibilityMode.AllowUnsupported);

        Assert.Equal(MySqlServerVersionSupportStatus.Legacy, serverVersion.SupportStatus);
        Assert.Equal(MySqlServerVersionCompatibilityMode.AllowUnsupported, serverVersion.CompatibilityMode);
    }

    /// <summary>
    /// Verifies that undefined compatibility-mode values are rejected.
    /// </summary>
    [Fact]
    public void Factory_rejects_undefined_compatibility_mode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MySqlServerVersion.MySql(
                new Version(8, 4, 0),
                (MySqlServerVersionCompatibilityMode)int.MaxValue));
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
            ProviderSupportStatus.UnsupportedByEngine,
            serverVersion.Profile.GetSupport(ProviderCapability.SpatialColumnSridAttribute));
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
        public StubDbConnection(
            string serverVersion
        )
        {
            ServerVersionValue = serverVersion;
        }

        private string ServerVersionValue { get; }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "doka";

        public override string DataSource => "localhost";

        public override string ServerVersion => ServerVersionValue;

        public override ConnectionState State => ConnectionState.Closed;

        public override void ChangeDatabase(
            string databaseName
        ) => throw new NotSupportedException();

        public override void Close() { }

        public override void Open() => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel
        ) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
