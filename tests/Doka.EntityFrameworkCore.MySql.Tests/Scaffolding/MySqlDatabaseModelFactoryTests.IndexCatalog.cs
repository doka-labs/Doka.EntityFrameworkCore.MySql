namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlDatabaseModelFactoryTests
{
    private sealed partial class ScaffoldingDbCommand
    {
        private static DataTableReader CreatePrimaryKeysReader(
            string databaseName
        )
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("CONSTRAINT_NAME", typeof(string));
            table.Columns.Add("ORDINAL_POSITION", typeof(long));

            if (databaseName == "principal_database")
            {
                table.Rows.Add("principal_table", "Id", "PRIMARY", 1L);
            }
            else if (databaseName == "dependent_database")
            {
                table.Rows.Add("dependent_table", "Id", "PRIMARY", 1L);
            }

            return table.CreateDataReader();
        }

        private static DataTableReader CreateUniqueConstraintsReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("CONSTRAINT_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("ORDINAL_POSITION", typeof(long));

            return table.CreateDataReader();
        }

        private static DataTableReader CreateIndexesReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("INDEX_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("NON_UNIQUE", typeof(long));
            table.Columns.Add("COLLATION", typeof(string));
            table.Columns.Add("SEQ_IN_INDEX", typeof(long));
            table.Columns.Add("INDEX_TYPE", typeof(string));
            table.Columns.Add("SUB_PART", typeof(long));
            table.Columns.Add("EXPRESSION", typeof(string));

            table.Rows.Add(
                "mixed_index_table",
                "IX_Mixed",
                "First",
                1L,
                "A",
                1L,
                "BTREE",
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                "mixed_index_table",
                "IX_Mixed",
                "Second",
                1L,
                "D",
                2L,
                "BTREE",
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                "spatial_feature_table",
                "IX_Spatial_Location",
                "Location",
                1L,
                "A",
                1L,
                "SPATIAL",
                DBNull.Value,
                DBNull.Value);

            return table.CreateDataReader();
        }

        private static DataTableReader CreateForeignKeysReader(
            string databaseName
        )
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_SCHEMA", typeof(string));
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("CONSTRAINT_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("ORDINAL_POSITION", typeof(long));
            table.Columns.Add("REFERENCED_TABLE_SCHEMA", typeof(string));
            table.Columns.Add("REFERENCED_TABLE_NAME", typeof(string));
            table.Columns.Add("REFERENCED_COLUMN_NAME", typeof(string));
            table.Columns.Add("DELETE_RULE", typeof(string));

            if (databaseName == "dependent_database")
            {
                table.Rows.Add(
                    "dependent_database",
                    "dependent_table",
                    "FK_Dependent_Principal",
                    "PrincipalId",
                    1L,
                    "principal_database",
                    "principal_table",
                    "Id",
                    "CASCADE");
            }

            return table.CreateDataReader();
        }
    }
}
