namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Renders column definitions, defaults, and engine-specific column options.
/// </summary>
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
        ValidateNativeUuidSupport(operation);

        if (operation.Collation is { } collation)
        {
            MySqlSqlTokenValidator.ValidateIdentifier(collation, RelationalAnnotationNames.Collation);
        }

        if (TryAppendTemporalPeriodColumnDefinition(name, operation, builder))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(operation.ComputedColumnSql))
        {
            base.ColumnDefinition(schema, table, name, operation, model, builder);
            AppendCommonColumnOptions(operation, builder);
            return;
        }

        if (IsMariaDbJsonAliasColumn(operation))
        {
            AppendMariaDbJsonAliasColumnDefinition(name, operation, builder);
            AppendCommonColumnOptions(operation, builder);
            AppendMariaDbJsonAliasConstraint(name, builder);
            return;
        }

        if (IsSpatialColumn(operation))
        {
            AppendSpatialColumnDefinition(name, operation, builder);
            AppendCommonColumnOptions(operation, builder);
            AppendEmulatedSpatialReferenceSystemConstraint(name, operation, builder);
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

        AppendCommonColumnOptions(operation, builder);
    }

    private static bool IsTemporalRowVersionColumn(
        ColumnOperation operation
    )
    {
        if (!string.IsNullOrWhiteSpace(operation.ColumnType))
        {
            var storeType = operation
                .ColumnType
                .AsSpan()
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
            && defaultValueSql is null)
        {
            var mapping = columnType is null
                ? Dependencies.TypeMappingSource.GetMappingForValue(defaultValue)
                : Dependencies.TypeMappingSource.FindMapping(defaultValue.GetType(), columnType)
                ?? Dependencies.TypeMappingSource.GetMappingForValue(defaultValue);

            if (RequiresParenthesizedDefault(columnType ?? mapping.StoreType))
            {
                builder
                    .Append(" DEFAULT (")
                    .Append(mapping.GenerateSqlLiteral(defaultValue))
                    .Append(")");

                return;
            }
        }

        base.DefaultValue(defaultValue, defaultValueSql, columnType, builder);
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
                $"The computed column '{table}.{name}' must declare an explicit column type.");
        }

        var computedColumnSql = operation.ComputedColumnSql
            ?? throw new InvalidOperationException(
                $"The computed column '{table}.{name}' must declare a computed SQL expression.");

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
            .Append(operation.IsStored == true ? GetStoredGeneratedColumnKeyword() : "VIRTUAL");

        if (!operation.IsNullable
            && _mySqlSingletonOptions.Profile?.GetSupport(ProviderCapability.GeneratedColumnNullabilityClause)
            == ProviderSupportStatus.Native)
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

        DefaultValue(operation.DefaultValue, operation.DefaultValueSql, "longtext", builder);
    }

    private void AppendMariaDbJsonAliasConstraint(
        string name,
        MigrationCommandListBuilder builder
    )
    {
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

        var spatialReferenceSystemId = operation.FindAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId)
            ?.Value as int?;

        var spatialReferenceSystemIdSupport = _mySqlSingletonOptions.Profile?.GetSupport(
            ProviderCapability.SpatialColumnSridEnforcement);

        if (spatialReferenceSystemId is not null
            && spatialReferenceSystemIdSupport == ProviderSupportStatus.Native)
        {
            builder
                .Append(" SRID ")
                .Append(spatialReferenceSystemId.Value.ToString(CultureInfo.InvariantCulture));
        }

        builder.Append(operation.IsNullable ? " NULL" : " NOT NULL");

        DefaultValue(operation.DefaultValue, operation.DefaultValueSql, columnType, builder);
    }

    private void AppendEmulatedSpatialReferenceSystemConstraint(
        string name,
        ColumnOperation operation,
        MigrationCommandListBuilder builder
    )
    {
        var spatialReferenceSystemId = operation.FindAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId)
            ?.Value as int?;

        var spatialReferenceSystemIdSupport = _mySqlSingletonOptions.Profile?.GetSupport(
            ProviderCapability.SpatialColumnSridEnforcement);

        if (spatialReferenceSystemId is not null
            && spatialReferenceSystemIdSupport == ProviderSupportStatus.Emulated)
        {
            builder
                .Append(" CHECK (ST_SRID(")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
                .Append(") = ")
                .Append(spatialReferenceSystemId.Value.ToString(CultureInfo.InvariantCulture))
                .Append(")");
        }
    }

    private void AppendCommonColumnOptions(
        ColumnOperation operation,
        MigrationCommandListBuilder builder
    )
    {
        var isInvisible = operation.FindAnnotation(MySqlAnnotationNames.Invisible)
            ?.Value is true;

        var hasComment = operation.Comment is not null
            || operation is AlterColumnOperation { OldColumn.Comment: not null, };

        if (Profile.Engine.Has(EngineCapability.ColumnCommentPrecedesVisibilityAttribute))
        {
            AppendColumnComment(operation, builder, hasComment);

            if (isInvisible)
            {
                builder.Append(" INVISIBLE");
            }

            return;
        }

        if (isInvisible)
        {
            builder.Append(" INVISIBLE");
        }

        AppendColumnComment(operation, builder, hasComment);
    }

    private static void AppendColumnComment(
        ColumnOperation operation,
        MigrationCommandListBuilder builder,
        bool hasComment
    )
    {
        if (hasComment)
        {
            builder
                .Append(" COMMENT ")
                .Append(MySqlSqlLiteralGenerator.GenerateDdlComment(operation.Comment ?? string.Empty));
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

        if (_mySqlSingletonOptions.Profile?.GetSupport(ProviderCapability.JsonColumns) == ProviderSupportStatus.Emulated)
        {
            return;
        }

        if (_mySqlSingletonOptions.Profile?.GetSupport(ProviderCapability.JsonColumns) == ProviderSupportStatus.Native)
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
            ? _mySqlSingletonOptions.Profile?.Supports(ProviderCapability.StoredGeneratedColumns) == true
            : _mySqlSingletonOptions.Profile?.Supports(ProviderCapability.VirtualGeneratedColumns) == true;

        if (supportsGeneratedColumns)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The configured server version does not support {(isStored ? "stored" : "virtual")} generated columns.");
    }

    private string GetStoredGeneratedColumnKeyword()
    {
        return _mySqlSingletonOptions.Profile?.Engine.Has(EngineCapability.StoredGeneratedColumnUsesPersistentKeyword)
            == true
                ? "PERSISTENT"
                : "STORED";
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
    ) => _mySqlSingletonOptions.Profile?.GetSupport(ProviderCapability.JsonColumns) == ProviderSupportStatus.Emulated
        && IsJsonColumn(operation);

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

    private static bool IsJsonColumn(
        ColumnOperation operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        return NormalizeStoreTypeName(operation.ColumnType) == "json";
    }

    private static bool RequiresParenthesizedDefault(
        string? columnType
    )
    {
        var storeType = GetBaseStoreType(columnType);

        return StoreTypeEquals(storeType, "blob")
            || StoreTypeEquals(storeType, "tinyblob")
            || StoreTypeEquals(storeType, "mediumblob")
            || StoreTypeEquals(storeType, "longblob")
            || StoreTypeEquals(storeType, "text")
            || StoreTypeEquals(storeType, "tinytext")
            || StoreTypeEquals(storeType, "mediumtext")
            || StoreTypeEquals(storeType, "longtext")
            || StoreTypeEquals(storeType, "json")
            || StoreTypeEquals(storeType, "date")
            || StoreTypeEquals(storeType, "time")
            || StoreTypeEquals(storeType, "geometry")
            || StoreTypeEquals(storeType, "point")
            || StoreTypeEquals(storeType, "linestring")
            || StoreTypeEquals(storeType, "polygon")
            || StoreTypeEquals(storeType, "geometrycollection")
            || StoreTypeEquals(storeType, "multipoint")
            || StoreTypeEquals(storeType, "multilinestring")
            || StoreTypeEquals(storeType, "multipolygon");
    }

    private static ReadOnlySpan<char> GetBaseStoreType(
        string? storeType
    )
    {
        if (string.IsNullOrWhiteSpace(storeType))
        {
            return default;
        }

        var value = storeType.AsSpan().Trim();
        var length = 0;

        while (length < value.Length
               && value[length] != '('
               && !char.IsWhiteSpace(value[length]))
        {
            length++;
        }

        return value[..length];
    }

    private static bool StoreTypeEquals(
        ReadOnlySpan<char> storeType,
        string expected
    ) => storeType.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
