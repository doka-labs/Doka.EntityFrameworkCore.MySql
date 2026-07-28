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

        if (operation.IsRowVersion)
        {
            if (operation.DefaultValue is null
                && string.IsNullOrWhiteSpace(operation.DefaultValueSql))
            {
                builder.Append(" DEFAULT CURRENT_TIMESTAMP(6)");
            }

            builder.Append(" ON UPDATE CURRENT_TIMESTAMP(6)");
        }

        if (IsAutoIncrementColumn(operation))
        {
            builder.Append(" AUTO_INCREMENT");
        }

        if (operation.FindAnnotation(MySqlAnnotationNames.Invisible)?.Value is true)
        {
            builder.Append(" INVISIBLE");
        }

        if (operation.Comment is not null
            || operation is AlterColumnOperation { OldColumn.Comment: not null, })
        {
            builder
                .Append(" COMMENT ")
                .Append(MySqlSqlLiteralEscaper.EscapeAndQuote(operation.Comment ?? string.Empty));
        }
    }

    protected override void DefaultValue(
        object? defaultValue,
        string? defaultValueSql,
        string? columnType,
        MigrationCommandListBuilder builder
    )
    {
        if (defaultValue is not null
            && defaultValueSql is null
            && RequiresParenthesizedDefault(columnType))
        {
            var mapping = columnType is null
                ? Dependencies.TypeMappingSource.GetMappingForValue(defaultValue)
                : Dependencies.TypeMappingSource.FindMapping(defaultValue.GetType(), columnType)
                ?? Dependencies.TypeMappingSource.GetMappingForValue(defaultValue);

            builder
                .Append(" DEFAULT (")
                .Append(mapping.GenerateSqlLiteral(defaultValue))
                .Append(")");
            return;
        }

        base.DefaultValue(defaultValue, defaultValueSql, columnType, builder);
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
            .Append(DelimitMigrationIdentifier(operation.Name))
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
            builder.Append("CREATE ");

            if (operation.IsUnique)
            {
                builder.Append("UNIQUE ");
            }

            IndexTraits(operation, model, builder);

            builder
                .Append("INDEX ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" ON ")
                .Append(DelimitMigrationIdentifier(operation.Table))
                .Append(" (");

            GenerateIndexColumnList(operation, model, builder);

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
            .Append(DelimitMigrationIdentifier(operation.Table))
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
        AlterColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        if (RequiresGeneratedColumnRecreation(operation))
        {
            builder
                .Append("ALTER TABLE ")
                .Append(DelimitMigrationIdentifier(operation.Table))
                .Append(" DROP COLUMN ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);

            builder
                .Append("ALTER TABLE ")
                .Append(DelimitMigrationIdentifier(operation.Table))
                .Append(" ADD COLUMN ");

            ColumnDefinition(operation.Schema, operation.Table, operation.Name, operation, model, builder);

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
            return;
        }

        GenerateNullValueUpdate(operation, model, builder);

        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(operation.Table))
            .Append(" MODIFY COLUMN ");

        ColumnDefinition(operation.Schema, operation.Table, operation.Name, operation, model, builder);

        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    protected override void Generate(
        AlterTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        if (string.Equals(operation.Comment, operation.OldTable.Comment, StringComparison.Ordinal))
        {
            return;
        }

        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(operation.Name))
            .Append(" COMMENT = ")
            .Append(MySqlSqlLiteralEscaper.EscapeAndQuote(operation.Comment ?? string.Empty))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        EndStatement(builder);
    }

    protected override void Generate(
        EnsureSchemaOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        // MySQL-family databases are the schema boundary. EF schema annotations
        // are intentionally ignored while table and sequence names remain usable.
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
            .Append(DelimitMigrationIdentifier(table))
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
            .Append(DelimitMigrationIdentifier(table))
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
            .Append(DelimitMigrationIdentifier(operation.Table))
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
            .Append(DelimitMigrationIdentifier(operation.Table))
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
            .Append(DelimitMigrationIdentifier(operation.PrincipalTable));

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
            .Append(columnType);

        if (!string.IsNullOrWhiteSpace(operation.Collation))
        {
            builder
                .Append(" COLLATE ")
                .Append(operation.Collation);
        }

        builder
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
        var comment = operation.Comment;

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

        // EF Core uses null when the application leaves the storage variant to
        // the provider. Both MySQL and MariaDB define virtual generated columns
        // as the native default, so null follows the same capability path as
        // an explicitly configured virtual column.
        var isStored = operation.IsStored == true;
        var supportsGeneratedColumns = isStored
            ? _mySqlSingletonOptions.Profile?.Has(Capability.SupportsStoredGeneratedColumns) == true
            : _mySqlSingletonOptions.Profile?.Has(Capability.SupportsVirtualGeneratedColumns) == true;

        if (supportsGeneratedColumns)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The configured server version does not support {(isStored ? "stored" : "virtual")} generated columns.");
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

    private static bool RequiresGeneratedColumnRecreation(
        AlterColumnOperation operation
    )
    {
        var hadComputedExpression = !string.IsNullOrWhiteSpace(operation.OldColumn.ComputedColumnSql);
        var hasComputedExpression = !string.IsNullOrWhiteSpace(operation.ComputedColumnSql);

        return hadComputedExpression != hasComputedExpression
            || (hadComputedExpression && operation.OldColumn.IsStored != operation.IsStored);
    }

    private void GenerateNullValueUpdate(
        AlterColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        if (!operation.OldColumn.IsNullable
            || operation.IsNullable
            || !string.IsNullOrWhiteSpace(operation.ComputedColumnSql))
        {
            return;
        }

        var columnType = operation.ColumnType
            ?? GetColumnType(operation.Schema, operation.Table, operation.Name, operation, model);
        var defaultValue = operation.DefaultValue ?? GetClrDefaultValue(operation.ClrType);

        builder
            .Append("UPDATE ")
            .Append(DelimitMigrationIdentifier(operation.Table))
            .Append(" SET ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .Append(" = ");

        if (!string.IsNullOrWhiteSpace(operation.DefaultValueSql))
        {
            builder.Append(operation.DefaultValueSql);
        }
        else
        {
            var mapping = Dependencies.TypeMappingSource.FindMapping(defaultValue.GetType(), columnType)
                ?? Dependencies.TypeMappingSource.GetMappingForValue(defaultValue);
            builder.Append(mapping.GenerateSqlLiteral(defaultValue));
        }

        builder
            .Append(" WHERE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .Append(" IS NULL")
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        EndStatement(builder);
    }

    private static object GetClrDefaultValue(
        Type clrType
    )
    {
        var valueType = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (valueType.IsEnum)
        {
            valueType = Enum.GetUnderlyingType(valueType);
        }

        if (valueType == typeof(string))
        {
            return string.Empty;
        }

        if (valueType == typeof(Guid))
        {
            return Guid.Empty;
        }

        if (valueType == typeof(DateOnly))
        {
            return DateOnly.MinValue;
        }

        if (valueType == typeof(TimeOnly))
        {
            return TimeOnly.MinValue;
        }

        if (valueType == typeof(TimeSpan))
        {
            return TimeSpan.Zero;
        }

        if (valueType == typeof(byte[]))
        {
            return Array.Empty<byte>();
        }

        return Type.GetTypeCode(valueType) switch
        {
            TypeCode.Boolean => false,
            TypeCode.Byte => (byte)0,
            TypeCode.Char => '\0',
            TypeCode.DateTime => default(DateTime),
            TypeCode.Decimal => 0m,
            TypeCode.Double => 0d,
            TypeCode.Int16 => (short)0,
            TypeCode.Int32 => 0,
            TypeCode.Int64 => 0L,
            TypeCode.SByte => (sbyte)0,
            TypeCode.Single => 0f,
            TypeCode.UInt16 => (ushort)0,
            TypeCode.UInt32 => 0U,
            TypeCode.UInt64 => 0UL,
            _ => throw new InvalidOperationException(
                $"No non-null store default is available for " + $"'{clrType.FullName ?? clrType.Name}'."),
        };
    }

    private static bool RequiresParenthesizedDefault(
        string? columnType
    ) => NormalizeStoreTypeName(columnType) is "blob"
        or "tinyblob"
        or "mediumblob"
        or "longblob"
        or "text"
        or "tinytext"
        or "mediumtext"
        or "longtext"
        or "json"
        or "geometry";

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

        if (string.Equals(operation.Name, newName, StringComparison.Ordinal))
        {
            return;
        }

        builder
            .Append("RENAME TABLE ")
            .Append(DelimitMigrationIdentifier(operation.Name))
            .Append(" TO ")
            .Append(DelimitMigrationIdentifier(newName))
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
                .Append(DelimitMigrationIdentifier(operation.Table))
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
            .Append(DelimitMigrationIdentifier(operation.Table))
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
                .Append(DelimitMigrationIdentifier(operation.Name));

            if (_mySqlSingletonOptions.Profile.Version.CompareTo(new Version(11, 5, 0)) >= 0)
            {
                builder
                    .Append(" AS ")
                    .Append(GetSequenceTypeInfo(operation.ClrType).StoreType);
            }

            builder
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

            builder.Append(operation.IsCyclic ? " CYCLE" : " NOCYCLE");
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            builder.EndCommand();
            return;
        }

        var typeInfo = GetSequenceTypeInfo(operation.ClrType);
        ValidateSequenceIncrement(typeInfo, operation.IncrementBy);

        var minimumValue = operation.MinValue
            ?? GetDefaultSequenceMinimum(typeInfo, operation.IncrementBy);
        var maximumValue = operation.MaxValue
            ?? GetDefaultSequenceMaximum(typeInfo, operation.IncrementBy);
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
            .Append(" ")
            .Append(typeInfo.StoreType)
            .AppendLine(" NOT NULL,")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("start_value"))
            .Append(" ")
            .Append(typeInfo.StoreType)
            .AppendLine(" NOT NULL,")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("increment_by"))
            .AppendLine(" INT NOT NULL,")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("min_value"))
            .Append(" ")
            .Append(typeInfo.StoreType)
            .AppendLine(" NOT NULL,")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("max_value"))
            .Append(" ")
            .Append(typeInfo.StoreType)
            .AppendLine(" NOT NULL,")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("is_cyclic"))
            .AppendLine(" BOOLEAN NOT NULL,")
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
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("start_value"))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("increment_by"))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("min_value"))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("max_value"))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("is_cyclic"))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("is_called"))
            .Append(") VALUES (1, ")
            .Append(operation.StartValue.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(operation.StartValue.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(operation.IncrementBy.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(minimumValue.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(maximumValue.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(operation.IsCyclic ? "TRUE" : "FALSE")
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
                .Append(DelimitMigrationIdentifier(operation.Name))
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
                .Append(DelimitMigrationIdentifier(operation.Name))
                .Append(" INCREMENT BY ")
                .Append(operation.IncrementBy.ToString(CultureInfo.InvariantCulture));

            builder.Append(
                operation.MinValue.HasValue
                    ? " MINVALUE " + operation.MinValue.Value.ToString(CultureInfo.InvariantCulture)
                    : " NO MINVALUE");
            builder.Append(
                operation.MaxValue.HasValue
                    ? " MAXVALUE " + operation.MaxValue.Value.ToString(CultureInfo.InvariantCulture)
                    : " NO MAXVALUE");
            builder.Append(operation.IsCyclic ? " CYCLE" : " NOCYCLE");

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            builder.EndCommand();
            return;
        }

        var clrType = model
                ?.FindSequence(operation.Name, operation.Schema)
                ?.Type
            ?? (operation.OldSequence as CreateSequenceOperation)?.ClrType
            ?? typeof(long);
        var typeInfo = GetSequenceTypeInfo(clrType);
        ValidateSequenceIncrement(typeInfo, operation.IncrementBy);

        var minimumValue = operation.MinValue
            ?? GetDefaultSequenceMinimum(typeInfo, operation.IncrementBy);
        var maximumValue = operation.MaxValue
            ?? GetDefaultSequenceMaximum(typeInfo, operation.IncrementBy);
        var tableName = MySqlSequenceNaming.EmulationTableName(operation.Name);

        builder
            .Append("UPDATE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(tableName))
            .Append(" SET ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("increment_by"))
            .Append(" = ")
            .Append(operation.IncrementBy.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("min_value"))
            .Append(" = ")
            .Append(minimumValue.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("max_value"))
            .Append(" = ")
            .Append(maximumValue.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("is_cyclic"))
            .Append(" = ")
            .Append(operation.IsCyclic ? "TRUE" : "FALSE")
            .Append(" WHERE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("id"))
            .Append(" = 1")
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        builder.EndCommand();
    }

    /// <summary>
    /// Renames a native MariaDB sequence or the MySQL emulation table.
    /// </summary>
    protected override void Generate(
        RenameSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        var newName = operation.NewName ?? operation.Name;

        if (string.Equals(operation.Name, newName, StringComparison.Ordinal))
        {
            return;
        }

        if (_mySqlSingletonOptions.Profile?.Has(Capability.SupportsNativeSequences) == true)
        {
            builder
                .Append("RENAME TABLE ")
                .Append(DelimitMigrationIdentifier(operation.Name))
                .Append(" TO ")
                .Append(DelimitMigrationIdentifier(newName))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            builder.EndCommand();
            return;
        }

        var oldTableName = MySqlSequenceNaming.EmulationTableName(operation.Name);
        var newTableName = MySqlSequenceNaming.EmulationTableName(newName);

        builder
            .Append("RENAME TABLE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(oldTableName))
            .Append(" TO ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(newTableName))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        builder.EndCommand();
    }

    /// <summary>
    /// Restarts a native MariaDB sequence or the MySQL emulation row.
    /// </summary>
    protected override void Generate(
        RestartSequenceOperation operation,
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
                .Append(DelimitMigrationIdentifier(operation.Name))
                .Append(" ");

            if (operation.StartValue.HasValue)
            {
                builder
                    .Append("START WITH ")
                    .Append(operation.StartValue.Value.ToString(CultureInfo.InvariantCulture))
                    .Append(" RESTART WITH ")
                    .Append(operation.StartValue.Value.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append("RESTART");
            }

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
            return;
        }

        var tableName = MySqlSequenceNaming.EmulationTableName(operation.Name);

        builder
            .Append("UPDATE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(tableName))
            .Append(" SET ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("value"))
            .Append(" = ")
            .Append(
                operation.StartValue?.ToString(CultureInfo.InvariantCulture)
                ?? Dependencies.SqlGenerationHelper.DelimitIdentifier("start_value"));

        if (operation.StartValue.HasValue)
        {
            builder
                .Append(", ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("start_value"))
                .Append(" = ")
                .Append(operation.StartValue.Value.ToString(CultureInfo.InvariantCulture));
        }

        builder
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("is_called"))
            .Append(" = FALSE WHERE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("id"))
            .Append(" = 1")
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    private string DelimitMigrationIdentifier(
        string identifier
    ) =>
        // EF schemas have no same-database namespace equivalent on MySQL-family
        // engines. Migration SQL therefore keeps the active database boundary.
        Dependencies.SqlGenerationHelper.DelimitIdentifier(identifier);

    private static SequenceTypeInfo GetSequenceTypeInfo(
        Type? clrType
    )
    {
        // EF Core's non-generic CreateSequence API defaults to Int64. Operations
        // constructed directly can omit ClrType and must retain the same contract.
        clrType ??= typeof(long);

        clrType = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (clrType == typeof(sbyte))
        {
            return new SequenceTypeInfo("TINYINT", sbyte.MinValue, sbyte.MaxValue, false);
        }

        if (clrType == typeof(byte))
        {
            return new SequenceTypeInfo("TINYINT UNSIGNED", byte.MinValue, byte.MaxValue, true);
        }

        if (clrType == typeof(short))
        {
            return new SequenceTypeInfo("SMALLINT", short.MinValue, short.MaxValue, false);
        }

        if (clrType == typeof(ushort))
        {
            return new SequenceTypeInfo("SMALLINT UNSIGNED", ushort.MinValue, ushort.MaxValue, true);
        }

        if (clrType == typeof(int))
        {
            return new SequenceTypeInfo("INT", int.MinValue, int.MaxValue, false);
        }

        if (clrType == typeof(uint))
        {
            return new SequenceTypeInfo("INT UNSIGNED", uint.MinValue, uint.MaxValue, true);
        }

        if (clrType == typeof(long))
        {
            return new SequenceTypeInfo("BIGINT", long.MinValue, long.MaxValue, false);
        }

        if (clrType == typeof(ulong))
        {
            return new SequenceTypeInfo("BIGINT UNSIGNED", 0, long.MaxValue, true);
        }

        throw new InvalidOperationException(
            $"The CLR type '{clrType.ShortDisplayName()}' cannot back a MySQL-family sequence.");
    }

    private static long GetDefaultSequenceMinimum(
        SequenceTypeInfo typeInfo,
        int increment
    ) => increment > 0 ? 1 : checked(typeInfo.MinimumValue + 1);

    private static long GetDefaultSequenceMaximum(
        SequenceTypeInfo typeInfo,
        int increment
    ) => increment > 0 ? checked(typeInfo.MaximumValue - 1) : -1;

    private static void ValidateSequenceIncrement(
        SequenceTypeInfo typeInfo,
        int increment
    )
    {
        if (increment == 0)
        {
            throw new InvalidOperationException("A sequence increment cannot be zero.");
        }

        if (increment < 0
            && typeInfo.IsUnsigned)
        {
            throw new InvalidOperationException(
                $"The unsigned sequence store type '{typeInfo.StoreType}' cannot use a negative increment.");
        }
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

    private readonly record struct SequenceTypeInfo(
        string StoreType,
        long MinimumValue,
        long MaximumValue,
        bool IsUnsigned
    );
}
