namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlDatabaseModelFactoryTests
{
    private sealed partial class ScaffoldingDbCommand
    {
        private static DataTableReader CreateJsonCheckConstraintsReader(
            ScaffoldingScenario scenario
        )
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("CHECK_CLAUSE", typeof(string));

            if (scenario == ScaffoldingScenario.MariaDbJson)
            {
                table.Rows.Add("json_constraint_table", "json_valid(`Payload`)");
                table.Rows.Add(
                    "json_constraint_table",
                    "`Payload` IS NULL OR json_valid(`Payload`)");
            }

            return table.CreateDataReader();
        }

        private static DataTableReader CreateNativeSequenceNamesReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));

            return table.CreateDataReader();
        }

        private static DataTableReader CreateCheckConstraintsReader(
            ScaffoldingScenario scenario
        )
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("CONSTRAINT_NAME", typeof(string));
            table.Columns.Add("CHECK_CLAUSE", typeof(string));

            if (scenario == ScaffoldingScenario.MariaDbSpatial)
            {
                table.Rows.Add("spatial_feature_table", "Location", "srid(`Location`) = 4326");
                table.Rows.Add(
                    "spatial_feature_table",
                    "CK_spatial_location_not_null",
                    "`Location` IS NOT NULL");
            }
            else if (scenario == ScaffoldingScenario.MariaDbJson)
            {
                table.Rows.Add(
                    "json_constraint_table",
                    "Payload",
                    "json_valid(`Payload`)");
                table.Rows.Add(
                    "json_constraint_table",
                    "CK_JsonConstraintTable_Payload",
                    "`Payload` IS NULL OR json_valid(`Payload`)");
            }

            return table.CreateDataReader();
        }

        private static DataTableReader CreateApplicationTimePeriodsReader(
            string commandText
        )
        {
            // PERIODS also contains MariaDB's native SYSTEM_TIME dimension. The
            // application-time loader must leave that row to TemporalTableLoader.
            Assert.Contains(
                "periods.PERIOD <> 'SYSTEM_TIME'",
                commandText,
                StringComparison.Ordinal);

            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("PERIOD", typeof(string));
            table.Columns.Add("START_COLUMN_NAME", typeof(string));
            table.Columns.Add("END_COLUMN_NAME", typeof(string));

            return table.CreateDataReader();
        }

        private static DataTableReader CreateApplicationTimeConstraintsReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("CONSTRAINT_NAME", typeof(string));
            table.Columns.Add("PERIOD_NAME", typeof(string));

            return table.CreateDataReader();
        }

        private static DataTableReader CreateSpatialGeometryColumnsReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("SRS_ID", typeof(int));

            table.Rows.Add("spatial_feature_table", "Location", 4326);

            return table.CreateDataReader();
        }
    }
}
