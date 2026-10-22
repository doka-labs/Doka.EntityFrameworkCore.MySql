namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlDatabaseModelFactoryTests
{
    private sealed partial class ScaffoldingDbCommand
    {
        private static DataTableReader CreateNativeTemporalPeriodColumnsReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("IS_SYSTEM_TIME_PERIOD_START", typeof(string));
            table.Columns.Add("IS_SYSTEM_TIME_PERIOD_END", typeof(string));
            table.Rows.Add("audit_entries", "ValidFrom", "YES", "NO");
            table.Rows.Add("audit_entries", "ValidTo", "NO", "YES");

            return table.CreateDataReader();
        }

        private static DataTableReader CreateTemporalTriggersReader(
            ScaffoldingScenario scenario
        )
        {
            var table = new DataTable();
            table.Columns.Add("TRIGGER_NAME", typeof(string));
            table.Columns.Add("EVENT_MANIPULATION", typeof(string));
            table.Columns.Add("EVENT_OBJECT_TABLE", typeof(string));
            table.Columns.Add("ACTION_TIMING", typeof(string));
            table.Columns.Add("ACTION_STATEMENT", typeof(string));

            if (scenario is not ScaffoldingScenario.MySqlTemporalEmulation
                and not ScaffoldingScenario.IncompleteMySqlTemporalEmulation)
            {
                return table.CreateDataReader();
            }

            var marker = MySqlTemporalMetadata.CreateEmulationMarker(
                null,
                "audit_entries_history",
                "ValidFrom",
                "ValidTo");

            var insertBody =
                $"BEGIN /* {marker} */ "
                + "SET NEW.`ValidFrom` = UTC_TIMESTAMP(6); "
                + "SET NEW.`ValidTo` = '9999-12-31 23:59:59.999999'; END";

            var updateBody =
                $"BEGIN /* {marker} */ "
                + "DECLARE __doka_temporal_timestamp datetime(6); "
                + "SET __doka_temporal_timestamp = UTC_TIMESTAMP(6); "
                + "INSERT INTO `audit_entries_history` "
                + "(`Id`, `Name`, `ValidFrom`, `ValidTo`) "
                + "VALUES (OLD.`Id`, OLD.`Name`, OLD.`ValidFrom`, __doka_temporal_timestamp); "
                + "SET NEW.`ValidFrom` = __doka_temporal_timestamp; "
                + "SET NEW.`ValidTo` = '9999-12-31 23:59:59.999999'; END";

            table.Rows.Add(
                MySqlTemporalMetadata.CreateTriggerName(null, "audit_entries", "insert"),
                "INSERT",
                "audit_entries",
                "BEFORE",
                insertBody);
            table.Rows.Add(
                MySqlTemporalMetadata.CreateTriggerName(null, "audit_entries", "update"),
                "UPDATE",
                "audit_entries",
                "BEFORE",
                updateBody);

            if (scenario == ScaffoldingScenario.MySqlTemporalEmulation)
            {
                var deleteBody =
                    $"BEGIN /* {marker} */ "
                    + "DECLARE __doka_temporal_timestamp datetime(6); "
                    + "SET __doka_temporal_timestamp = UTC_TIMESTAMP(6); "
                    + "INSERT INTO `audit_entries_history` "
                    + "(`Id`, `Name`, `ValidFrom`, `ValidTo`) "
                    + "VALUES (OLD.`Id`, OLD.`Name`, OLD.`ValidFrom`, __doka_temporal_timestamp); END";

                table.Rows.Add(
                    MySqlTemporalMetadata.CreateTriggerName(null, "audit_entries", "delete"),
                    "DELETE",
                    "audit_entries",
                    "BEFORE",
                    deleteBody);
            }

            return table.CreateDataReader();
        }

        private static DataTableReader CreatePhysicalTemporalTablesReader(
            string databaseName
        )
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_SCHEMA", typeof(string));
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("ENGINE", typeof(string));
            table.Columns.Add("TABLE_TYPE", typeof(string));
            table.Rows.Add(databaseName, "audit_entries", "InnoDB", "BASE TABLE");
            table.Rows.Add(databaseName, "audit_entries_history", "InnoDB", "BASE TABLE");

            return table.CreateDataReader();
        }

        private static DataTableReader CreatePhysicalTemporalColumnsReader(
            string databaseName
        )
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_SCHEMA", typeof(string));
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("COLUMN_TYPE", typeof(string));
            table.Columns.Add("IS_NULLABLE", typeof(string));
            table.Columns.Add("EXTRA", typeof(string));
            table.Columns.Add("GENERATION_EXPRESSION", typeof(string));

            AddPhysicalTemporalColumns(table, databaseName, "audit_entries");
            AddPhysicalTemporalColumns(table, databaseName, "audit_entries_history");

            return table.CreateDataReader();
        }

        private static void AddTemporalColumns(
            DataTable table,
            string tableName,
            string periodColumnType,
            string periodDataType
        )
        {
            table.Rows.Add(
                tableName,
                "Id",
                "NO",
                "int",
                "int",
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                tableName,
                "Name",
                "NO",
                "varchar(64)",
                "varchar",
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                "utf8mb4_0900_ai_ci");
            table.Rows.Add(
                tableName,
                "ValidFrom",
                "NO",
                periodColumnType,
                periodDataType,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                tableName,
                "ValidTo",
                "NO",
                periodColumnType,
                periodDataType,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value);
        }

        private static void AddPhysicalTemporalColumns(
            DataTable table,
            string databaseName,
            string tableName
        )
        {
            table.Rows.Add(databaseName, tableName, "Id", "int", "NO", DBNull.Value, DBNull.Value);
            table.Rows.Add(
                databaseName,
                tableName,
                "Name",
                "varchar(64)",
                "NO",
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                databaseName,
                tableName,
                "ValidFrom",
                "datetime(6)",
                "NO",
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                databaseName,
                tableName,
                "ValidTo",
                "datetime(6)",
                "NO",
                DBNull.Value,
                DBNull.Value);
        }
    }
}
