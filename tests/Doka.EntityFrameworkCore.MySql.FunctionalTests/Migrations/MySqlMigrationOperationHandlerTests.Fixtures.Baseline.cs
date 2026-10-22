namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationOperationHandlerTests
{
    /// <summary>
    /// Creates fresh mutable operations covering every provider-reserved migration type.
    /// </summary>
    private static IReadOnlyList<MigrationOperation> CreateStandardOperationFixtures()
    {
        var alterTable = new AlterTableOperation
        {
            Name = "Entries",
            Comment = "new comment",
            OldTable = { Comment = "old comment" },
        };

        return
        [
            new AddColumnOperation
            {
                Table = "Entries",
                Name = "Value",
                ClrType = typeof(int),
                ColumnType = "int",
                IsNullable = true,
            },
            new AddForeignKeyOperation
            {
                Name = "FK_Entries_Parents",
                Table = "Entries",
                Columns = ["ParentId"],
                PrincipalTable = "Parents",
                PrincipalColumns = ["Id"],
            },
            new AddPrimaryKeyOperation
            {
                Name = "PK_Entries",
                Table = "Entries",
                Columns = ["Id"],
            },
            new AddUniqueConstraintOperation
            {
                Name = "AK_Entries_Code",
                Table = "Entries",
                Columns = ["Code"],
            },
            new AlterColumnOperation
            {
                Table = "Entries",
                Name = "Value",
                ClrType = typeof(long),
                ColumnType = "bigint",
                IsNullable = true,
                OldColumn = new AddColumnOperation
                {
                    Table = "Entries",
                    Name = "Value",
                    ClrType = typeof(int),
                    ColumnType = "int",
                    IsNullable = true,
                },
            },
            new AlterDatabaseOperation(),
            new AlterSequenceOperation
            {
                Name = "EntrySequence",
                IncrementBy = 2,
            },
            alterTable,
            new AddCheckConstraintOperation
            {
                Name = "CK_Entries_Value",
                Table = "Entries",
                Sql = "`Value` >= 0",
            },
            new CreateIndexOperation
            {
                Name = "IX_Entries_Code",
                Table = "Entries",
                Columns = ["Code"],
            },
            new CreateSequenceOperation
            {
                Name = "EntrySequence",
                ClrType = typeof(long),
                StartValue = 1,
                IncrementBy = 1,
            },
            CreateTableFixture(),
            new DropColumnOperation
            {
                Table = "Entries",
                Name = "Value",
            },
            new DropForeignKeyOperation
            {
                Name = "FK_Entries_Parents",
                Table = "Entries",
            },
            new DropIndexOperation
            {
                Name = "IX_Entries_Code",
                Table = "Entries",
            },
            new DropPrimaryKeyOperation
            {
                Name = "PK_Entries",
                Table = "Entries",
            },
            new DropSchemaOperation { Name = "tenant_database" },
            new DropSequenceOperation { Name = "EntrySequence" },
            new DropTableOperation { Name = "Entries" },
            new DropUniqueConstraintOperation
            {
                Name = "AK_Entries_Code",
                Table = "Entries",
            },
            new DropCheckConstraintOperation
            {
                Name = "CK_Entries_Value",
                Table = "Entries",
            },
            new EnsureSchemaOperation { Name = "tenant_database" },
            new RenameColumnOperation
            {
                Table = "Entries",
                Name = "Value",
                NewName = "RenamedValue",
            },
            new RenameIndexOperation
            {
                Table = "Entries",
                Name = "IX_Entries_Code",
                NewName = "IX_Entries_Value",
            },
            new RenameSequenceOperation
            {
                Name = "EntrySequence",
                NewName = "RenamedSequence",
            },
            new RenameTableOperation
            {
                Name = "Entries",
                NewName = "RenamedEntries",
            },
            new RestartSequenceOperation
            {
                Name = "EntrySequence",
                StartValue = 5,
            },
            new SqlOperation
            {
                Sql = "SELECT 1;",
                SuppressTransaction = true,
            },
            new InsertDataOperation
            {
                Table = "Entries",
                Columns = ["Id"],
                ColumnTypes = ["int"],
                Values = new object[,] { { 1 } },
            },
            new DeleteDataOperation
            {
                Table = "Entries",
                KeyColumns = ["Id"],
                KeyColumnTypes = ["int"],
                KeyValues = new object[,] { { 1 } },
            },
            new UpdateDataOperation
            {
                Table = "Entries",
                Columns = ["Value"],
                ColumnTypes = ["int"],
                Values = new object[,] { { 2 } },
                KeyColumns = ["Id"],
                KeyColumnTypes = ["int"],
                KeyValues = new object[,] { { 1 } },
            },
        ];
    }

    private static CreateTableOperation CreateTableFixture()
    {
        var operation = new CreateTableOperation { Name = "Entries" };
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = operation.Name,
                Name = "Id",
                ClrType = typeof(int),
                ColumnType = "int",
                IsNullable = false,
            });
        operation.PrimaryKey = new AddPrimaryKeyOperation
        {
            Name = "PK_Entries",
            Table = operation.Name,
            Columns = ["Id"],
        };

        return operation;
    }
}
