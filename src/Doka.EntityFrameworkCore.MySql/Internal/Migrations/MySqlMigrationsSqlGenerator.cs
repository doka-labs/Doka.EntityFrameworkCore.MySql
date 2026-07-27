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
            && _mySqlSingletonOptions.Profile?.Has(Capability.SupportsGeneratedColumnNullabilityClause) == true)
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
            && _mySqlSingletonOptions.Profile?.Has(Capability.SupportsSpatialColumnSridAttribute) == true)
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
        var collation = operation.FindAnnotation(MySqlAnnotationNames.Collation)?.Value as string;
        var storageEngine = operation.FindAnnotation(MySqlAnnotationNames.StorageEngine)?.Value as string;
        var comment = operation.FindAnnotation(MySqlAnnotationNames.Comment)?.Value as string;

        if (!string.IsNullOrWhiteSpace(charSet))
        {
            ValidateIdentifier(charSet, MySqlAnnotationNames.CharSet);
            builder
                .Append(" CHARACTER SET ")
                .Append(charSet);
        }

        if (!string.IsNullOrWhiteSpace(collation))
        {
            ValidateIdentifier(collation, MySqlAnnotationNames.Collation);
            builder
                .Append(" COLLATE ")
                .Append(collation);
        }

        if (!string.IsNullOrWhiteSpace(storageEngine))
        {
            ValidateIdentifier(storageEngine, MySqlAnnotationNames.StorageEngine);
            builder
                .Append(" ENGINE = ")
                .Append(storageEngine);
        }

        if (!string.IsNullOrWhiteSpace(comment))
        {
            builder
                .Append(" COMMENT = ")
                .Append(MySqlSqlLiteralEscaper.EscapeAndQuote(comment));
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

        if (_mySqlSingletonOptions.Profile?.Has(Capability.UsesJsonAliasForJsonColumns) == true)
        {
            return;
        }

        if (_mySqlSingletonOptions.Profile?.Has(Capability.SupportsNativeJsonType) == true)
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
            ? _mySqlSingletonOptions.Profile?.Has(Capability.SupportsStoredGeneratedColumns) == true
            : _mySqlSingletonOptions.Profile?.Has(Capability.SupportsVirtualGeneratedColumns) == true;

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
    ) => _mySqlSingletonOptions.Profile?.Has(Capability.UsesJsonAliasForJsonColumns) == true && IsJsonColumn(operation);

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
    /// Generates MySQL-specific column rename syntax. The two engines diverge here:
    /// MySQL 8.0+ and MariaDB 10.5.2+ accept the modern
    /// <c>ALTER TABLE ... RENAME COLUMN old TO new</c> form; older MariaDB versions
    /// require <c>ALTER TABLE ... CHANGE COLUMN old new &lt;full column definition&gt;</c>.
    /// The engine choice is read from the active <see cref="EngineProfile"/> via
    /// <see cref="Capability.SupportsRenameColumnSyntax"/>; the fallback path resolves
    /// the column definition from the post-rename <see cref="IModel"/> (post-rename
    /// because EF Core applies the operation to the model before invoking the
    /// generator, so the column entry already carries the new name).
    /// </summary>
    protected override void Generate(
        RenameColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        if (_mySqlSingletonOptions.Profile?.Has(Capability.SupportsRenameColumnSyntax) == true)
        {
            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" RENAME COLUMN ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" TO ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            builder.EndCommand();
            return;
        }

        AppendChangeColumnRename(operation, model, builder);
    }

    /// <summary>
    /// Engine fallback for MariaDB &lt; 10.5.2: emit
    /// <c>ALTER TABLE t CHANGE COLUMN old new &lt;column definition&gt;</c>. The full
    /// column definition is required by the older syntax; we recover it from the
    /// post-rename <see cref="IModel"/> entry.
    /// </summary>
    private void AppendChangeColumnRename(
        RenameColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        var column = (model
                ?.GetRelationalModel()
                .FindTable(operation.Table, operation.Schema)
                ?.Columns.FirstOrDefault(c => string.Equals(c.Name, operation.NewName, StringComparison.Ordinal)))
            ?? throw new InvalidOperationException(
                $"Could not resolve the column definition for '{operation.Table}.{operation.NewName}' from the model. "
                + "The active engine version requires the MariaDB CHANGE COLUMN form for column rename, "
                + "which needs the post-rename column definition. Ensure the model contains the renamed column or "
                + "upgrade to MariaDB 10.5.2 or later (where RENAME COLUMN works without the column definition).");

        var columnOperation = new AddColumnOperation
        {
            Schema = operation.Schema,
            Table = operation.Table,
            Name = operation.NewName,
            ClrType = column.ProviderClrType ?? column.StoreType.GetType(),
            ColumnType = column.StoreType,
            IsNullable = column.IsNullable,
            DefaultValue = column.DefaultValue,
            DefaultValueSql = column.DefaultValueSql,
            ComputedColumnSql = column.ComputedColumnSql,
            IsStored = column.IsStored,
            Comment = column.Comment,
            Collation = column.Collation,
            Precision = column.Precision,
            Scale = column.Scale,
            IsUnicode = column.IsUnicode,
            IsFixedLength = column.IsFixedLength,
            MaxLength = column.MaxLength,
        };

        foreach (var annotation in column.GetAnnotations())
        {
            columnOperation.AddAnnotation(annotation.Name, annotation.Value);
        }

        builder
            .Append("ALTER TABLE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
            .Append(" CHANGE COLUMN ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .Append(" ");

        ColumnDefinition(
            operation.Schema,
            operation.Table,
            operation.NewName,
            columnOperation,
            model,
            builder);

        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
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

        if (_mySqlSingletonOptions.Profile?.Has(Capability.SupportsNativeSequences) == true)
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
        var tableName = MySqlSequenceNaming.EmulationTableName(operation.Name);
        var delimitedTableName = Dependencies.SqlGenerationHelper.DelimitIdentifier(tableName);

        builder
            .Append("CREATE TABLE ")
            .Append(delimitedTableName)
            .AppendLine(" (")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("id"))
            .AppendLine(" TINYINT UNSIGNED NOT NULL,")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("value"))
            .AppendLine(" BIGINT NOT NULL,")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("is_called"))
            .AppendLine(" BOOLEAN NOT NULL,")
            .Append("    PRIMARY KEY (")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("id"))
            .AppendLine("),")
            .Append("    CHECK (")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("id"))
            .AppendLine(" = 1)")
            .Append(") ENGINE=InnoDB")
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        builder.EndCommand();

        builder
            .Append("INSERT INTO ")
            .Append(delimitedTableName)
            .Append(" (")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("id"))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("value"))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("is_called"))
            .Append(") VALUES (1, ")
            .Append(operation.StartValue.ToString(CultureInfo.InvariantCulture))
            .Append(", FALSE)")
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

        if (_mySqlSingletonOptions.Profile?.Has(Capability.SupportsNativeSequences) == true)
        {
            builder
                .Append("DROP SEQUENCE IF EXISTS ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            builder.EndCommand();
            return;
        }

        var tableName = MySqlSequenceNaming.EmulationTableName(operation.Name);

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

        if (_mySqlSingletonOptions.Profile?.Has(Capability.SupportsNativeSequences) == true)
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

        var oldTableName = MySqlSequenceNaming.EmulationTableName(operation.Name);
        var newTableName = MySqlSequenceNaming.EmulationTableName(operation.NewName ?? operation.Name);

        if (_mySqlSingletonOptions.Profile?.Has(Capability.SupportsNativeSequences) == true)
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
