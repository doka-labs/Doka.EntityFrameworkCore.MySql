namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsSqlGenerator
{
    protected override void Generate(
        CreateIndexOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        ValidateIndexShape(operation);

        if (!IsSpatialIndex(operation))
        {
            ValidateStandardIndex(operation);

            builder.Append("CREATE ");

            if (operation.IsUnique)
            {
                builder.Append("UNIQUE ");
            }

            if (IsFullTextIndex(operation))
            {
                builder.Append("FULLTEXT ");
            }

            IndexTraits(operation, model, builder);

            builder
                .Append("INDEX ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" ON ")
                .Append(DelimitMigrationIdentifier(operation.Table, operation.Schema))
                .Append(" (");

            GenerateMySqlIndexColumnList(operation, builder);

            builder.Append(")");

            IndexOptions(operation, model, builder);

            if (terminate)
            {
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                EndStatement(builder);
            }

            return;
        }

        ValidateSpatialIndex(operation);

        builder
            .Append("CREATE SPATIAL INDEX ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .Append(" ON ")
            .Append(DelimitMigrationIdentifier(operation.Table, operation.Schema))
            .Append(" (")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Columns[0]))
            .Append(")");

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    protected override void Generate(
        DropIndexOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        var table = operation.Table
            ?? throw new InvalidOperationException($"The index '{operation.Name}' does not identify its table.");

        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(table, operation.Schema))
            .Append(" DROP INDEX ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    protected override void Generate(
        RenameIndexOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        var table = operation.Table
            ?? throw new InvalidOperationException($"The index '{operation.Name}' does not identify its table.");

        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(table, operation.Schema))
            .Append(" RENAME INDEX ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .Append(" TO ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    protected override void Generate(
        DropForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(operation.Table, operation.Schema))
            .Append(" DROP FOREIGN KEY ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    protected override void Generate(
        DropPrimaryKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(operation.Table, operation.Schema))
            .Append(" DROP PRIMARY KEY");

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    /// <inheritdoc />
    protected override void ForeignKeyConstraint(
        AddForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        if (operation.Name is not null)
        {
            builder
                .Append("CONSTRAINT ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" ");
        }

        builder
            .Append("FOREIGN KEY (")
            .Append(ColumnList(operation.Columns))
            .Append(") REFERENCES ")
            .Append(DelimitMigrationIdentifier(operation.PrincipalTable, operation.PrincipalSchema));

        if (operation.PrincipalColumns is not null)
        {
            builder
                .Append(" (")
                .Append(ColumnList(operation.PrincipalColumns))
                .Append(")");
        }

        if (operation.OnUpdate != ReferentialAction.NoAction)
        {
            builder.Append(" ON UPDATE ");
            ForeignKeyAction(operation.OnUpdate, builder);
        }

        if (operation.OnDelete != ReferentialAction.NoAction)
        {
            builder.Append(" ON DELETE ");
            ForeignKeyAction(operation.OnDelete, builder);
        }
    }

    private static bool IsSpatialIndex(
        CreateIndexOperation operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        return (operation.FindAnnotation(MySqlAnnotationNames.SpatialIndex)?.Value as bool?) == true;
    }

    private static bool IsFullTextIndex(
        CreateIndexOperation operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        return (operation.FindAnnotation(MySqlAnnotationNames.FullTextIndex)
                ?.Value as bool?)
            == true;
    }

    private static int[]? GetIndexPrefixLengths(
        CreateIndexOperation operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        return operation.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength)
            ?.Value as int[];
    }

    private static void ValidateStandardIndex(
        CreateIndexOperation operation
    )
    {
        var prefixLengths = GetIndexPrefixLengths(operation);

        if (!IsFullTextIndex(operation))
        {
            return;
        }

        if (operation.IsUnique)
        {
            throw new InvalidOperationException($"The full-text index '{operation.Name}' cannot be unique.");
        }

        if (prefixLengths?.Any(prefixLength => prefixLength > 0) == true)
        {
            throw new InvalidOperationException(
                $"The full-text index '{operation.Name}' cannot declare prefix lengths.");
        }
    }

    private static void ValidateIndexShape(
        CreateIndexOperation operation
    )
    {
        var prefixLengths = GetIndexPrefixLengths(operation);

        if (prefixLengths is not null
            && prefixLengths.Length != operation.Columns.Length)
        {
            throw new InvalidOperationException(
                $"The index '{operation.Name}' must declare one prefix length per column.");
        }

        if (prefixLengths?.Any(prefixLength => prefixLength < 0) == true)
        {
            throw new InvalidOperationException($"The index '{operation.Name}' contains a negative prefix length.");
        }

        if (operation.IsDescending is { Length: > 0 } descending
            && descending.Length != operation.Columns.Length)
        {
            throw new InvalidOperationException(
                $"The index '{operation.Name}' must declare one sort direction per column.");
        }
    }

    private void GenerateMySqlIndexColumnList(
        CreateIndexOperation operation,
        MigrationCommandListBuilder builder
    )
    {
        var prefixLengths = GetIndexPrefixLengths(operation);

        for (var index = 0; index < operation.Columns.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Columns[index]));

            if (prefixLengths?[index] is > 0 and var prefixLength)
            {
                builder
                    .Append("(")
                    .Append(prefixLength.ToString(CultureInfo.InvariantCulture))
                    .Append(")");
            }

            if (operation.IsDescending is { } descending
                && (descending.Length == 0 || descending[index]))
            {
                builder.Append(" DESC");
            }
        }
    }

    private static void ValidateSpatialIndex(
        CreateIndexOperation operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.Columns.Length != 1)
        {
            throw new InvalidOperationException(
                $"The spatial index '{operation.Name}' must target exactly one column.");
        }

        if (operation.IsUnique)
        {
            throw new InvalidOperationException($"The spatial index '{operation.Name}' cannot be unique.");
        }

        if (IsFullTextIndex(operation))
        {
            throw new InvalidOperationException(
                $"The spatial index '{operation.Name}' cannot also be a full-text index.");
        }

        if (GetIndexPrefixLengths(operation)?.Any(prefixLength => prefixLength > 0) == true)
        {
            throw new InvalidOperationException(
                $"The spatial index '{operation.Name}' cannot declare prefix lengths.");
        }
    }
}
