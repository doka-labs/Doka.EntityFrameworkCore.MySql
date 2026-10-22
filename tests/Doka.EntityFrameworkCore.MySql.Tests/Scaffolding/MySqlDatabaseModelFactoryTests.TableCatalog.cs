namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlDatabaseModelFactoryTests
{
    private sealed partial class ScaffoldingDbCommand
    {
        private static DataTableReader CreateTablesReader(
            string databaseName,
            ScaffoldingScenario scenario
        )
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("TABLE_COLLATION", typeof(string));
            table.Columns.Add("TABLE_COMMENT", typeof(string));
            table.Columns.Add("ENGINE", typeof(string));
            table.Columns.Add("TABLE_TYPE", typeof(string));

            if (scenario == ScaffoldingScenario.NativeMariaDbTemporal)
            {
                table.Rows.Add(
                    "audit_entries",
                    "utf8mb4_general_ci",
                    DBNull.Value,
                    "InnoDB",
                    "SYSTEM VERSIONED");
            }
            else if (scenario == ScaffoldingScenario.MariaDbJson)
            {
                table.Rows.Add(
                    "json_constraint_table",
                    "utf8mb4_general_ci",
                    DBNull.Value,
                    "InnoDB",
                    "BASE TABLE");
            }
            else if (scenario is ScaffoldingScenario.MySqlTemporalEmulation
                     or ScaffoldingScenario.IncompleteMySqlTemporalEmulation)
            {
                table.Rows.Add(
                    "audit_entries",
                    "utf8mb4_0900_ai_ci",
                    DBNull.Value,
                    "InnoDB",
                    "BASE TABLE");
                table.Rows.Add(
                    "audit_entries_history",
                    "utf8mb4_0900_ai_ci",
                    DBNull.Value,
                    "InnoDB",
                    "BASE TABLE");
            }
            else if (databaseName == "principal_database")
            {
                table.Rows.Add(
                    "principal_table",
                    "utf8mb4_0900_ai_ci",
                    DBNull.Value,
                    "InnoDB",
                    "BASE TABLE");
            }
            else if (databaseName == "dependent_database")
            {
                table.Rows.Add(
                    "dependent_table",
                    "utf8mb4_0900_ai_ci",
                    DBNull.Value,
                    "InnoDB",
                    "BASE TABLE");
            }
            else
            {
                table.Rows.Add(
                    "mixed_index_table",
                    "utf8mb4_0900_ai_ci",
                    DBNull.Value,
                    "InnoDB",
                    "BASE TABLE");
                table.Rows.Add(
                    "spatial_feature_table",
                    "utf8mb4_0900_ai_ci",
                    DBNull.Value,
                    "InnoDB",
                    "BASE TABLE");
            }

            return table.CreateDataReader();
        }

        private static DataTableReader CreateColumnsReader(
            string databaseName,
            ScaffoldingScenario scenario
        )
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("IS_NULLABLE", typeof(string));
            table.Columns.Add("COLUMN_TYPE", typeof(string));
            table.Columns.Add("DATA_TYPE", typeof(string));
            table.Columns.Add("COLUMN_DEFAULT", typeof(string));
            table.Columns.Add("EXTRA", typeof(string));
            table.Columns.Add("GENERATION_EXPRESSION", typeof(string));
            table.Columns.Add("COLUMN_COMMENT", typeof(string));
            table.Columns.Add("COLLATION_NAME", typeof(string));

            if (scenario is ScaffoldingScenario.NativeMariaDbTemporal)
            {
                AddTemporalColumns(table, "audit_entries", "timestamp(6)", "timestamp");

                return table.CreateDataReader();
            }

            if (scenario == ScaffoldingScenario.MariaDbJson)
            {
                table.Rows.Add(
                    "json_constraint_table",
                    "Payload",
                    "YES",
                    "longtext",
                    "longtext",
                    DBNull.Value,
                    DBNull.Value,
                    DBNull.Value,
                    DBNull.Value,
                    "utf8mb4_bin");

                return table.CreateDataReader();
            }

            if (scenario is ScaffoldingScenario.MySqlTemporalEmulation
                or ScaffoldingScenario.IncompleteMySqlTemporalEmulation)
            {
                AddTemporalColumns(table, "audit_entries", "datetime(6)", "datetime");
                AddTemporalColumns(table, "audit_entries_history", "datetime(6)", "datetime");

                return table.CreateDataReader();
            }

            if (databaseName == "principal_database")
            {
                AddColumn(table, "principal_table", "Id");

                return table.CreateDataReader();
            }

            if (databaseName == "dependent_database")
            {
                AddColumn(table, "dependent_table", "Id");
                AddColumn(table, "dependent_table", "PrincipalId");

                return table.CreateDataReader();
            }

            table.Rows.Add(
                "mixed_index_table",
                "First",
                "NO",
                "int",
                "int",
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                "mixed_index_table",
                "Second",
                "NO",
                "int",
                "int",
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                "spatial_feature_table",
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
                "spatial_feature_table",
                "Location",
                "NO",
                "point",
                "point",
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value);

            return table.CreateDataReader();
        }

        private static void AddColumn(
            DataTable table,
            string tableName,
            string columnName
        )
        {
            table.Rows.Add(
                tableName,
                columnName,
                "NO",
                "int",
                "int",
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value);
        }
    }
}
