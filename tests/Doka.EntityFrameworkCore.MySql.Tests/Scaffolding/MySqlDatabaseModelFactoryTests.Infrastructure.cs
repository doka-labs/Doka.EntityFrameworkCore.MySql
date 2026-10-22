namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlDatabaseModelFactoryTests
{
    private enum ScaffoldingScenario
    {
        Default,
        NativeMariaDbTemporal,
        MariaDbSpatial,
        MariaDbJson,
        MySqlTemporalEmulation,
        IncompleteMySqlTemporalEmulation,
    }

    private sealed class StubDriverFacade : IMySqlDriverFacade
    {
        public string DriverName => "Stub";

        public DbConnection CreateConnection(
            string connectionString
        ) => throw new NotSupportedException("This test uses the DbConnection overload directly.");
    }

    private sealed class ScaffoldingDbConnection : DbConnection
    {
        private string _database = "phase2";
        private ConnectionState _state = ConnectionState.Closed;

        public ScaffoldingDbConnection(
            ScaffoldingScenario scenario = ScaffoldingScenario.Default
        )
        {
            Scenario = scenario;
        }

        [AllowNull]
        public override string ConnectionString { get; set; } = "Server=localhost;Database=phase2;";

        public override string Database => _database;

        public List<string> DatabaseChanges { get; } = [];

        public List<string> ReaderCommands { get; } = [];

        public ScaffoldingScenario Scenario { get; }

        public override string DataSource => "localhost";

        public override string ServerVersion => Scenario is ScaffoldingScenario.NativeMariaDbTemporal
            or ScaffoldingScenario.MariaDbSpatial
            or ScaffoldingScenario.MariaDbJson
            ? "11.4.7-MariaDB"
            : "8.4.6";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(
            string databaseName
        )
        {
            _database = databaseName;
            DatabaseChanges.Add(databaseName);
        }

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open() => _state = ConnectionState.Open;

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel
        ) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new ScaffoldingDbCommand(this);
    }
}
