namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlMigrationsSqlGenerator : MigrationsSqlGenerator
{
    private readonly MySqlSingletonOptions _mySqlSingletonOptions;

    public MySqlMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        IEnumerable<ISingletonOptions> singletonOptions
    ) : base(dependencies)
    {
        ArgumentNullException.ThrowIfNull(singletonOptions);

        _mySqlSingletonOptions = singletonOptions
            .OfType<MySqlSingletonOptions>()
            .Single();
    }

    protected override void ColumnDefinition(
        string? schema,
        string table,
        string name,
        ColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        ValidateJsonSupport(operation);
        ValidateGeneratedColumnSupport(operation);
        ValidateSpatialColumnSupport(operation);

        if (IsMariaDbJsonAliasColumn(operation))
        {
            AppendMariaDbJsonAliasColumnDefinition(name, operation, builder);
            return;
        }

        if (IsSpatialColumn(operation))
        {
            AppendSpatialColumnDefinition(name, operation, builder);
            return;
        }

        base.ColumnDefinition(schema, table, name, operation, model, builder);

        if (IsAutoIncrementColumn(operation))
        {
            builder.Append(" AUTO_INCREMENT");
        }

        if (operation.FindAnnotation(MySqlAnnotationNames.Invisible)?.Value is true)
        {
            builder.Append(" INVISIBLE");
        }
    }

    protected override void Generate(
        AlterDatabaseOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        var charSet = operation.FindAnnotation(MySqlAnnotationNames.CharSet)?.Value as string;

        if (string.IsNullOrWhiteSpace(charSet))
        {
            return;
        }

        ValidateIdentifier(charSet, MySqlAnnotationNames.CharSet);

        builder
            .Append("ALTER DATABASE CHARACTER SET = ")
            .Append(charSet)
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        EndStatement(builder);
    }

    protected override void Generate(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        builder
            .Append("CREATE TABLE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
            .AppendLine(" (");

        using (builder.Indent())
        {
            CreateTableColumns(operation, model, builder);
            CreateTableConstraints(operation, model, builder);
        }

        builder.Append(")");

        AppendTableOptions(operation, builder);

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    protected override void Generate(
        CreateIndexOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        if (!IsSpatialIndex(operation))
        {
            base.Generate(operation, model, builder, terminate);
            return;
        }

        ValidateSpatialIndex(operation);

        builder
            .Append("CREATE SPATIAL INDEX ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .Append(" ON ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
            .Append(" (")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Columns[0]))
            .Append(")");

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    protected override void ComputedColumnDefinition(
        string? schema,
        string table,
        string name,
        ColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        ValidateGeneratedColumnSupport(operation);

        var columnType = operation.ColumnType;

        if (string.IsNullOrWhiteSpace(columnType))
        {
            throw new InvalidOperationException(
                $"The computed column '{table ?? "<unknown-table>"}.{name}' must declare an explicit column type.");
        }

        var computedColumnSql = operation.ComputedColumnSql
            ?? throw new InvalidOperationException(
                $"The computed column '{table ?? "<unknown-table>"}.{name}' must declare a computed SQL expression.");

        builder
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
            .Append(" ")
            .Append(columnType)
            .Append(" GENERATED ALWAYS AS (")
            .Append(computedColumnSql)
            .Append(") ")
            .Append(operation.IsStored == true ? "STORED" : "VIRTUAL");

        if (!operation.IsNullable
            && _mySqlSingletonOptions.Capabilities?.SupportsGeneratedColumnNullabilityClause == true)
        {
            builder.Append(" NOT NULL");
        }
    }

    private void AppendMariaDbJsonAliasColumnDefinition(
        string name,
        ColumnOperation operation,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
            .Append(" longtext COLLATE utf8mb4_bin");

        if (!operation.IsNullable)
        {
            builder.Append(" NOT NULL");
        }

        builder.Append(" CHECK (JSON_VALID(");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name));
        builder.Append("))");
    }

    private void AppendSpatialColumnDefinition(
        string name,
        ColumnOperation operation,
        MigrationCommandListBuilder builder
    )
    {
        var columnType = operation.ColumnType!;

        builder
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
            .Append(" ")
            .Append(columnType);

        if (operation.FindAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId)
                ?.Value is int spatialReferenceSystemId
            && _mySqlSingletonOptions.Capabilities?.SupportsSpatialColumnSridAttribute == true)
        {
            builder
                .Append(" SRID ")
                .Append(spatialReferenceSystemId.ToString(CultureInfo.InvariantCulture));
        }

        builder.Append(operation.IsNullable ? " NULL" : " NOT NULL");
    }

    private static void AppendTableOptions(
        CreateTableOperation operation,
        MigrationCommandListBuilder builder
    )
    {
        var charSet = operation.FindAnnotation(MySqlAnnotationNames.CharSet)?.Value as string;
        var storageEngine = operation.FindAnnotation(MySqlAnnotationNames.StorageEngine)?.Value as string;

        if (!string.IsNullOrWhiteSpace(charSet))
        {
            ValidateIdentifier(charSet, MySqlAnnotationNames.CharSet);
            builder
                .Append(" CHARACTER SET ")
                .Append(charSet);
        }

        if (!string.IsNullOrWhiteSpace(storageEngine))
        {
            ValidateIdentifier(storageEngine, MySqlAnnotationNames.StorageEngine);
            builder
                .Append(" ENGINE = ")
                .Append(storageEngine);
        }
    }

    /// <summary>
    /// Validates that the annotation value contains only characters that are safe to
    /// embed verbatim into generated DDL. MySQL charset and storage-engine identifiers
    /// are ASCII letters, digits, and underscores (e.g. <c>utf8mb4</c>, <c>InnoDB</c>).
    /// Values originate from developer-controlled model configuration, but we guard
    /// against accidental injection of unexpected characters at DDL generation time.
    /// </summary>
    private static void ValidateIdentifier(
        string value,
        string annotationName
    )
    {
        foreach (var ch in value)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
            {
                throw new InvalidOperationException(
                    $"The value configured for '{annotationName}' ('{value}') contains invalid characters. "
                    + "MySQL charset and storage-engine identifiers must use ASCII letters, digits, or underscores only.");
            }
        }
    }

    private void ValidateJsonSupport(
        ColumnOperation operation
    )
    {
        if (!IsJsonColumn(operation))
        {
            return;
        }

        if (_mySqlSingletonOptions.Capabilities?.UsesJsonAliasForJsonColumns == true)
        {
            return;
        }

        if (_mySqlSingletonOptions.Capabilities?.SupportsNativeJsonType == true)
        {
            return;
        }

        throw new InvalidOperationException(
            "JSON columns require a server version that supports native JSON or MariaDB JSON alias semantics.");
    }

    private void ValidateGeneratedColumnSupport(
        ColumnOperation operation
    )
    {
        if (string.IsNullOrWhiteSpace(operation.ComputedColumnSql))
        {
            return;
        }

        if (operation.IsStored is null)
        {
            throw new InvalidOperationException(
                "Generated columns must explicitly choose either the virtual or stored variant.");
        }

        var supportsGeneratedColumns = operation.IsStored.Value
            ? _mySqlSingletonOptions.Capabilities?.SupportsStoredGeneratedColumns == true
            : _mySqlSingletonOptions.Capabilities?.SupportsVirtualGeneratedColumns == true;

        if (supportsGeneratedColumns)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The configured server version does not support {(operation.IsStored.Value ? "stored" : "virtual")} generated columns.");
    }

    private static void ValidateSpatialColumnSupport(
        ColumnOperation operation
    )
    {
        if (!IsSpatialColumn(operation))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(operation.ColumnType))
        {
            throw new InvalidOperationException("Spatial columns must declare an explicit MySQL-family store type.");
        }
    }

    private bool IsMariaDbJsonAliasColumn(
        ColumnOperation operation
    ) => _mySqlSingletonOptions.Capabilities?.UsesJsonAliasForJsonColumns == true && IsJsonColumn(operation);

    private static bool IsSpatialColumn(
        ColumnOperation operation
    ) => MySqlSpatialTypeSupport.IsSpatialStoreType(operation.ColumnType);

    private static bool IsSpatialIndex(
        CreateIndexOperation operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        return (operation.FindAnnotation(MySqlAnnotationNames.SpatialIndex)?.Value as bool?) == true;
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
    }

    private static bool IsAutoIncrementColumn(
        ColumnOperation operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        return operation.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
            ?.Value is MySqlValueGenerationStrategy.AutoIncrement;
    }

    private static bool IsJsonColumn(
        ColumnOperation operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        return NormalizeStoreTypeName(operation.ColumnType) == "json";
    }

    /// <summary>
    /// Generates MySQL-specific RENAME TABLE syntax.
    /// MySQL uses <c>RENAME TABLE old TO new</c> instead of <c>ALTER TABLE ... RENAME TO</c>.
    /// </summary>
    protected override void Generate(
        RenameTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        var newName = operation.NewName ?? operation.Name;

        builder
            .Append("RENAME TABLE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
            .Append(" TO ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(newName, operation.NewSchema))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        builder.EndCommand();
    }

    /// <summary>
    /// Generates MySQL-specific column rename syntax.
    /// MySQL 8.0+ supports <c>ALTER TABLE ... RENAME COLUMN old TO new</c>.
    /// Older MySQL uses <c>ALTER TABLE ... CHANGE COLUMN old new column_definition</c>,
    /// but since we target MySQL 8.0+ the simpler syntax is used.
    /// </summary>
    protected override void Generate(
        RenameColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        builder
            .Append("ALTER TABLE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
            .Append(" RENAME COLUMN ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .Append(" TO ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        builder.EndCommand();
    }

    /// <summary>
    /// Generates CREATE SEQUENCE DDL -- native on MariaDB 10.3+, table-based emulation on MySQL.
    /// </summary>
    protected override void Generate(
        CreateSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        if (_mySqlSingletonOptions.Capabilities?.SupportsNativeSequences == true)
        {
            builder
                .Append("CREATE SEQUENCE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
                .Append(" START WITH ")
                .Append(operation.StartValue.ToString(CultureInfo.InvariantCulture))
                .Append(" INCREMENT BY ")
                .Append(operation.IncrementBy.ToString(CultureInfo.InvariantCulture));

            if (operation.MinValue.HasValue)
            {
                builder
                    .Append(" MINVALUE ")
                    .Append(operation.MinValue.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (operation.MaxValue.HasValue)
            {
                builder
                    .Append(" MAXVALUE ")
                    .Append(operation.MaxValue.Value.ToString(CultureInfo.InvariantCulture));
            }

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            builder.EndCommand();
            return;
        }

        // MySQL table-based sequence emulation.
        var tableName = $"__efsequence_{operation.Name}";
        var delimitedTableName = Dependencies.SqlGenerationHelper.DelimitIdentifier(tableName);

        builder
            .Append("CREATE TABLE ")
            .Append(delimitedTableName)
            .AppendLine(" (")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("value"))
            .AppendLine(" BIGINT NOT NULL")
            .Append(") ENGINE=InnoDB")
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        builder.EndCommand();

        builder
            .Append("INSERT INTO ")
            .Append(delimitedTableName)
            .Append(" (")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("value"))
            .Append(") VALUES (")
            .Append(operation.StartValue.ToString(CultureInfo.InvariantCulture))
            .Append(")")
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        builder.EndCommand();
    }

    /// <summary>
    /// Generates DROP SEQUENCE DDL -- native on MariaDB 10.3+, drops emulation table on MySQL.
    /// </summary>
    protected override void Generate(
        DropSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        if (_mySqlSingletonOptions.Capabilities?.SupportsNativeSequences == true)
        {
            builder
                .Append("DROP SEQUENCE IF EXISTS ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            builder.EndCommand();
            return;
        }

        var tableName = $"__efsequence_{operation.Name}";

        builder
            .Append("DROP TABLE IF EXISTS ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(tableName))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        builder.EndCommand();
    }

    /// <summary>
    /// Generates ALTER SEQUENCE DDL -- native on MariaDB 10.3+, updates emulation table on MySQL.
    /// </summary>
    protected override void Generate(
        AlterSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        if (_mySqlSingletonOptions.Capabilities?.SupportsNativeSequences == true)
        {
            builder
                .Append("ALTER SEQUENCE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
                .Append(" INCREMENT BY ")
                .Append(operation.IncrementBy.ToString(CultureInfo.InvariantCulture));

            if (operation.MinValue.HasValue)
            {
                builder
                    .Append(" MINVALUE ")
                    .Append(operation.MinValue.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (operation.MaxValue.HasValue)
            {
                builder
                    .Append(" MAXVALUE ")
                    .Append(operation.MaxValue.Value.ToString(CultureInfo.InvariantCulture));
            }

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            builder.EndCommand();
            return;
        }

        // For MySQL emulation, ALTER means updating the increment behavior -- the emulation
        // table stores only the current value; increment is applied at fetch time.
        // No DDL action needed for the emulation table itself.
        builder.AppendLine(
            "-- Sequence alter: increment changes are applied at value-fetch time in table-based emulation.");
        builder.EndCommand();
    }

    /// <summary>
    /// Generates RENAME SEQUENCE DDL -- renames emulation table on MySQL, not natively supported on MariaDB.
    /// </summary>
    protected override void Generate(
        RenameSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        var oldTableName = $"__efsequence_{operation.Name}";
        var newTableName = $"__efsequence_{operation.NewName ?? operation.Name}";

        if (_mySqlSingletonOptions.Capabilities?.SupportsNativeSequences == true)
        {
            // MariaDB does not support RENAME SEQUENCE -- drop and recreate.
            builder
                .Append("-- MariaDB does not support RENAME SEQUENCE; manual migration required.")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            builder.EndCommand();
            return;
        }

        builder
            .Append("RENAME TABLE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(oldTableName))
            .Append(" TO ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(newTableName))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        builder.EndCommand();
    }

    private static string? NormalizeStoreTypeName(
        string? storeType
    )
    {
        if (string.IsNullOrWhiteSpace(storeType))
        {
            return null;
        }

        var parenthesisIndex = storeType.IndexOf('(');

        return (parenthesisIndex >= 0 ? storeType[..parenthesisIndex] : storeType)
            .Trim()
            .ToLowerInvariant();
    }
}
