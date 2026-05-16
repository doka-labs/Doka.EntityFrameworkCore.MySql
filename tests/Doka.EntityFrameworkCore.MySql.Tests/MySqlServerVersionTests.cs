namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers server-version parsing and capability resolution.
/// </summary>
public sealed class MySqlServerVersionTests
{
    /// <summary>
    /// Verifies the default capability profile for modern MySQL versions.
    /// </summary>
    [Fact]
    public void MySql_factory_creates_mysql_capabilities()
    {
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        Assert.False(serverVersion.IsMariaDb);
        Assert.True(serverVersion.Profile.Has(Capability.SupportsCommonTableExpressions));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsWindowFunctions));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsNativeJsonType));
        Assert.False(serverVersion.Profile.Has(Capability.UsesJsonAliasForJsonColumns));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsGeneratedInvisiblePrimaryKeys));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsSavepoints));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsGeneratedColumnNullabilityClause));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsVirtualGeneratedColumns));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsStoredGeneratedColumns));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsSpatialColumnSridAttribute));
    }

    /// <summary>
    /// Verifies the default capability profile for modern MariaDB versions.
    /// </summary>
    [Fact]
    public void MariaDb_factory_creates_mariadb_capabilities()
    {
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));

        Assert.True(serverVersion.IsMariaDb);
        Assert.True(serverVersion.Profile.Has(Capability.SupportsCommonTableExpressions));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsWindowFunctions));
        Assert.False(serverVersion.Profile.Has(Capability.SupportsNativeJsonType));
        Assert.True(serverVersion.Profile.Has(Capability.UsesJsonAliasForJsonColumns));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsReturningClause));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsSavepoints));
        Assert.False(serverVersion.Profile.Has(Capability.SupportsGeneratedColumnNullabilityClause));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsVirtualGeneratedColumns));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsStoredGeneratedColumns));
        Assert.False(serverVersion.Profile.Has(Capability.SupportsSpatialColumnSridAttribute));
    }

    /// <summary>
    /// Verifies that MariaDB 11.4 uses the same approved capability profile as the newer 11.8 support line.
    /// </summary>
    [Fact]
    public void MariaDb114_factory_creates_the_expected_mariadb_capabilities()
    {
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));

        Assert.True(serverVersion.IsMariaDb);
        Assert.True(serverVersion.Profile.Has(Capability.SupportsCommonTableExpressions));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsWindowFunctions));
        Assert.False(serverVersion.Profile.Has(Capability.SupportsNativeJsonType));
        Assert.True(serverVersion.Profile.Has(Capability.UsesJsonAliasForJsonColumns));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsReturningClause));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsSavepoints));
        Assert.False(serverVersion.Profile.Has(Capability.SupportsGeneratedColumnNullabilityClause));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsVirtualGeneratedColumns));
        Assert.True(serverVersion.Profile.Has(Capability.SupportsStoredGeneratedColumns));
        Assert.False(serverVersion.Profile.Has(Capability.SupportsSpatialColumnSridAttribute));
    }

    /// <summary>
    /// Verifies that MariaDB version strings are detected correctly.
    /// </summary>
    [Fact]
    public void AutoDetect_recognizes_mariadb_version_strings()
    {
        var serverVersion = MySqlServerVersion.AutoDetect("11.8.1-MariaDB-ubu2404");

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
    public void AutoDetect_rejects_malformed_version_strings(
        string rawServerVersion
    )
    {
        var exception = Assert.Throws<ArgumentException>(() => MySqlServerVersion.AutoDetect(rawServerVersion));

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
