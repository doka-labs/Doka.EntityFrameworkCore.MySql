namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsSqlGenerator
{
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

        if (operation.IsRowVersion
            && IsTemporalRowVersionColumn(operation))
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

    private static bool IsTemporalRowVersionColumn(
        ColumnOperation operation
    )
    {
        if (!string.IsNullOrWhiteSpace(operation.ColumnType))
        {
            var storeType = operation
                .ColumnType.AsSpan()
                .TrimStart();

            return storeType.StartsWith("timestamp", StringComparison.OrdinalIgnoreCase)
                || storeType.StartsWith("datetime", StringComparison.OrdinalIgnoreCase);
        }

        // Migrations generated from a model carry ColumnType. The CLR fallback
        // preserves hand-authored migration operations that rely on the provider's
        // conventional byte[] row-version or temporal mappings.
        var clrType = Nullable.GetUnderlyingType(operation.ClrType) ?? operation.ClrType;

        return clrType == typeof(byte[]) || clrType == typeof(DateTime) || clrType == typeof(DateTimeOffset);
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
                .Append(DelimitMigrationIdentifier(operation.Table, operation.Schema))
                .Append(" DROP COLUMN ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);

            builder
                .Append("ALTER TABLE ")
                .Append(DelimitMigrationIdentifier(operation.Table, operation.Schema))
                .Append(" ADD COLUMN ");

            ColumnDefinition(operation.Schema, operation.Table, operation.Name, operation, model, builder);

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
            return;
        }

        GenerateNullValueUpdate(operation, model, builder);

        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(operation.Table, operation.Schema))
            .Append(" MODIFY COLUMN ");

        ColumnDefinition(operation.Schema, operation.Table, operation.Name, operation, model, builder);

        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
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

    private static bool IsAutoIncrementColumn(
        ColumnOperation operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        return operation.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
            ?.Value is MySqlValueGenerationStrategy.AutoIncrement;
    }

    /// <summary>
    /// Adds the leading index required for a generated column that appears
    /// after an owner foreign key in an EF composite primary key.
    /// </summary>
    /// <remarks>
    /// The logical primary-key order remains unchanged because nested owned
    /// types can reference that exact order. MySQL and MariaDB only require
    /// the <c>AUTO_INCREMENT</c> column to lead any index.
    ///
    /// Primary sources, retrieved 2026-07-29:
    /// https://dev.mysql.com/doc/refman/8.4/en/example-auto-increment.html
    /// https://mariadb.com/docs/server/reference/data-types/auto_increment
    /// </remarks>
    private void AppendAutoIncrementSupportingIndex(
        CreateTableOperation operation,
        MigrationCommandListBuilder builder
    )
    {
        var autoIncrementColumns = operation
            .Columns.Where(IsAutoIncrementColumn)
            .ToArray();

        if (autoIncrementColumns.Length > 1)
        {
            throw new InvalidOperationException(
                $"Table '{operation.Name}' declares more than one AUTO_INCREMENT column.");
        }

        if (autoIncrementColumns.Length == 0)
        {
            return;
        }

        var columnName = autoIncrementColumns[0].Name;
        var alreadyLeadsIndex = operation.PrimaryKey?.Columns.FirstOrDefault() == columnName
            || operation.UniqueConstraints.Any(constraint => constraint.Columns.FirstOrDefault() == columnName)
            || operation.ForeignKeys.Any(foreignKey => foreignKey.Columns.FirstOrDefault() == columnName);

        if (alreadyLeadsIndex)
        {
            return;
        }

        builder
            .AppendLine(",")
            .Append("INDEX (")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(columnName))
            .Append(")");
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
            .Append(DelimitMigrationIdentifier(operation.Table, operation.Schema))
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
