namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlDatabaseModelFactoryTests
{
    private sealed partial class ScaffoldingDbCommand : DbCommand
    {
        private readonly ScaffoldingDbConnection _connection;
        private readonly ScaffoldingDbParameterCollection _parameters = new();

        public ScaffoldingDbCommand(
            ScaffoldingDbConnection connection
        )
        {
            _connection = connection;
        }

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection
        {
            get => _connection;
            set => throw new NotSupportedException();
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        public override object ExecuteScalar()
        {
            return CommandText switch
            {
                var sql when sql.Contains("SELECT VERSION()", StringComparison.Ordinal) =>
                    _connection.ServerVersion,
                var sql when sql.Contains("SELECT DATABASE()", StringComparison.Ordinal)
                    && !sql.Contains("SCHEMATA", StringComparison.Ordinal) => _connection.Database,
                var sql when sql.Contains("FROM information_schema.SCHEMATA", StringComparison.Ordinal) =>
                    "utf8mb4_0900_ai_ci",
                _ => throw new InvalidOperationException($"Unexpected scalar command: {CommandText}"),
            };
        }

        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => new ScaffoldingDbParameter();

        protected override DbDataReader ExecuteDbDataReader(
            CommandBehavior behavior
        )
        {
            _connection.ReaderCommands.Add(CommandText);

            return CommandText switch
            {
                var sql when sql.Contains("IS_SYSTEM_TIME_PERIOD_START", StringComparison.Ordinal) =>
                    CreateNativeTemporalPeriodColumnsReader(),
                var sql when sql.Contains("FROM information_schema.TRIGGERS", StringComparison.Ordinal) =>
                    CreateTemporalTriggersReader(_connection.Scenario),
                var sql when sql.Contains("TABLE_SCHEMA,", StringComparison.Ordinal)
                    && sql.Contains("FROM information_schema.TABLES", StringComparison.Ordinal) =>
                    CreatePhysicalTemporalTablesReader(_connection.Database),
                var sql when sql.Contains("TABLE_SCHEMA,", StringComparison.Ordinal)
                    && sql.Contains("FROM information_schema.COLUMNS", StringComparison.Ordinal) =>
                    CreatePhysicalTemporalColumnsReader(_connection.Database),
                var sql when sql.Contains("FROM information_schema.CHECK_CONSTRAINTS", StringComparison.Ordinal)
                    && sql.Contains("JSON_VALID", StringComparison.OrdinalIgnoreCase) =>
                    CreateJsonCheckConstraintsReader(_connection.Scenario),
                var sql when sql.Contains("FROM information_schema.CHECK_CONSTRAINTS", StringComparison.Ordinal) =>
                    CreateCheckConstraintsReader(_connection.Scenario),
                var sql when sql.Contains("TABLE_TYPE = 'SEQUENCE'", StringComparison.Ordinal) =>
                    CreateNativeSequenceNamesReader(),
                var sql when sql.Contains("FROM information_schema.TABLES", StringComparison.Ordinal) =>
                    CreateTablesReader(_connection.Database, _connection.Scenario),
                var sql when sql.Contains("FROM information_schema.COLUMNS", StringComparison.Ordinal) =>
                    CreateColumnsReader(_connection.Database, _connection.Scenario),
                var sql when sql.Contains("CONSTRAINT_NAME = 'PRIMARY'", StringComparison.Ordinal) =>
                    CreatePrimaryKeysReader(_connection.Database),
                var sql when sql.Contains("CONSTRAINT_TYPE = 'UNIQUE'", StringComparison.Ordinal) =>
                    CreateUniqueConstraintsReader(),
                var sql when sql.Contains("CONSTRAINT_TYPE = 'CHECK'", StringComparison.Ordinal) =>
                    CreateCheckConstraintsReader(_connection.Scenario),
                var sql when sql.Contains("FROM information_schema.ST_GEOMETRY_COLUMNS", StringComparison.Ordinal) =>
                    CreateSpatialGeometryColumnsReader(),
                var sql when sql.Contains("FROM information_schema.STATISTICS", StringComparison.Ordinal) =>
                    CreateIndexesReader(),
                var sql when sql.Contains("FROM information_schema.PERIODS AS periods", StringComparison.Ordinal) =>
                    CreateApplicationTimePeriodsReader(sql),
                var sql when sql.Contains(
                    "FROM information_schema.KEY_PERIOD_USAGE AS period_keys",
                    StringComparison.Ordinal) => CreateApplicationTimeConstraintsReader(),
                var sql when sql.Contains(
                    "FROM information_schema.KEY_COLUMN_USAGE AS source",
                    StringComparison.Ordinal) => CreateForeignKeysReader(_connection.Database),
                _ => throw new InvalidOperationException($"Unexpected reader command: {CommandText}"),
            };
        }
    }
}
