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
        Assert.True(serverVersion.Capabilities.SupportsCommonTableExpressions);
        Assert.True(serverVersion.Capabilities.SupportsWindowFunctions);
        Assert.True(serverVersion.Capabilities.SupportsNativeJsonType);
        Assert.False(serverVersion.Capabilities.UsesJsonAliasForJsonColumns);
        Assert.True(serverVersion.Capabilities.SupportsGeneratedInvisiblePrimaryKeys);
        Assert.True(serverVersion.Capabilities.SupportsSavepoints);
        Assert.True(serverVersion.Capabilities.SupportsGeneratedColumnNullabilityClause);
        Assert.True(serverVersion.Capabilities.SupportsVirtualGeneratedColumns);
        Assert.True(serverVersion.Capabilities.SupportsStoredGeneratedColumns);
        Assert.True(serverVersion.Capabilities.SupportsSpatialColumnSridAttribute);
    }

    /// <summary>
    /// Verifies the default capability profile for modern MariaDB versions.
    /// </summary>
    [Fact]
    public void MariaDb_factory_creates_mariadb_capabilities()
    {
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));

        Assert.True(serverVersion.IsMariaDb);
        Assert.True(serverVersion.Capabilities.SupportsCommonTableExpressions);
        Assert.True(serverVersion.Capabilities.SupportsWindowFunctions);
        Assert.False(serverVersion.Capabilities.SupportsNativeJsonType);
        Assert.True(serverVersion.Capabilities.UsesJsonAliasForJsonColumns);
        Assert.True(serverVersion.Capabilities.SupportsReturningClause);
        Assert.True(serverVersion.Capabilities.SupportsSavepoints);
        Assert.False(serverVersion.Capabilities.SupportsGeneratedColumnNullabilityClause);
        Assert.True(serverVersion.Capabilities.SupportsVirtualGeneratedColumns);
        Assert.True(serverVersion.Capabilities.SupportsStoredGeneratedColumns);
        Assert.False(serverVersion.Capabilities.SupportsSpatialColumnSridAttribute);
    }

    /// <summary>
    /// Verifies that MariaDB 11.4 uses the same approved capability profile as the newer 11.8 support line.
    /// </summary>
    [Fact]
    public void MariaDb114_factory_creates_the_expected_mariadb_capabilities()
    {
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));

        Assert.True(serverVersion.IsMariaDb);
        Assert.True(serverVersion.Capabilities.SupportsCommonTableExpressions);
        Assert.True(serverVersion.Capabilities.SupportsWindowFunctions);
        Assert.False(serverVersion.Capabilities.SupportsNativeJsonType);
        Assert.True(serverVersion.Capabilities.UsesJsonAliasForJsonColumns);
        Assert.True(serverVersion.Capabilities.SupportsReturningClause);
        Assert.True(serverVersion.Capabilities.SupportsSavepoints);
        Assert.False(serverVersion.Capabilities.SupportsGeneratedColumnNullabilityClause);
        Assert.True(serverVersion.Capabilities.SupportsVirtualGeneratedColumns);
        Assert.True(serverVersion.Capabilities.SupportsStoredGeneratedColumns);
        Assert.False(serverVersion.Capabilities.SupportsSpatialColumnSridAttribute);
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
