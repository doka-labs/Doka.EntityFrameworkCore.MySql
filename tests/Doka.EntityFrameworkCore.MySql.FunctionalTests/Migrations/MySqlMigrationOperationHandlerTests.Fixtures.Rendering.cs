namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationOperationHandlerTests
{
    private static MigrationOperation CreateCommentWrapperOperation(
        string path
    )
    {
        const string comment = "path\\segment";

        return path switch
        {
            "create" => CreateCommentedTable(comment),
            "add" => new AddColumnOperation
            {
                Table = "Entries",
                Name = "Value",
                ClrType = typeof(string),
                ColumnType = "varchar(64)",
                IsNullable = true,
                Comment = comment,
            },
            "alter-column" => CreateCommentedAlterColumn(comment),
            "alter-table" => new AlterTableOperation
            {
                Name = "Entries",
                Comment = comment,
            },
            "rename-fallback" => new RenameColumnOperation
            {
                Table = "HandlerRenameEntries",
                Name = "OldValue",
                NewName = "RenamedValue",
            },
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, "Unknown comment-wrapper path."),
        };
    }

    private static CreateTableOperation CreateCommentedTable(
        string comment
    )
    {
        var operation = new CreateTableOperation
        {
            Name = "Entries",
            Comment = comment,
        };

        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = operation.Name,
                Name = "Id",
                ClrType = typeof(int),
                ColumnType = "int",
                IsNullable = false,
            });

        return operation;
    }

    private static AlterColumnOperation CreateCommentedAlterColumn(
        string comment
    )
    {

        return new AlterColumnOperation
        {
            Table = "Entries",
            Name = "Value",
            ClrType = typeof(string),
            ColumnType = "varchar(64)",
            IsNullable = true,
            Comment = comment,
            OldColumn =
            {
                ClrType = typeof(string),
                ColumnType = "varchar(64)",
                IsNullable = true,
            },
        };
    }

    private sealed class CustomRenderingHandler : IMySqlMigrationOperationHandler
    {
        /// <inheritdoc />
        public string HandlerId => "tests.custom_rendering";

        /// <inheritdoc />
        public Type OperationType => typeof(FirstCustomOperation);

        /// <inheritdoc />
        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            _ = context.RenderStandardOperation(new SecondCustomOperation());
            throw new InvalidOperationException("The custom rendering guard did not run.");
        }
    }
}
