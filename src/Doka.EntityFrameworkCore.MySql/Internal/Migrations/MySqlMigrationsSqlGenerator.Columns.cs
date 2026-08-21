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

        if (!string.IsNullOrWhiteSpace(operation.Collation))
        {
            MySqlSqlTokenValidator.ValidateIdentifier(
                operation.Collation,
                MySqlAnnotationNames.Collation);
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

    protected override void Generate(
        AddColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        TryGetTemporalMigrationContract(
            operation,
            operation.Table,
            sourceContract: true,
            out var sourceTemporalContract);
        TryGetTemporalMigrationContract(
            operation,
            operation.Table,
            sourceContract: false,
            out var targetTemporalContract);

        if (sourceTemporalContract is null
            && targetTemporalContract?.Support == ProviderSupportStatus.Native
            && (IsTemporalPeriodColumn(
                    operation.Name,
                    operation,
                    MySqlAnnotationNames.TemporalPeriodStartColumn)
                || IsTemporalPeriodColumn(
                    operation.Name,
                    operation,
                    MySqlAnnotationNames.TemporalPeriodEndColumn)))
        {
            // MariaDB requires both generated period columns, the period, and
            // SYSTEM VERSIONING to be added by the same ALTER TABLE statement.
            // The table-level transition emits that atomic native contract.
            return;
        }

        if (sourceTemporalContract is not null
            && targetTemporalContract is not null)
        {
            if (targetTemporalContract.Support == ProviderSupportStatus.Native)
            {
                ThrowNativeTemporalSchemaChangeNotSupported(
                    operation.Table,
                    $"add column '{operation.Name}'");
            }

            if (!terminate)
            {
                throw new InvalidOperationException(
                    "A temporal ADD COLUMN operation using MySQL emulation must terminate its commands.");
            }

            AppendDropTemporalTriggers(operation.Table, operation.Schema, builder);
            GenerateAddColumn(operation, model, builder, terminate: true);
            AppendTemporalHistoryColumnAddition(operation, model, targetTemporalContract, builder);
            AppendTemporalTriggersFromModel(
                operation.Table,
                operation.Schema,
                model,
                targetTemporalContract,
                builder);
            return;
        }

        GenerateAddColumn(operation, model, builder, terminate);
    }

    private void GenerateAddColumn(
        AddColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate
    )
    {
        ValidateDdlCommentSqlModeScope(operation);
        var requiresCommentSqlModeScope = RequiresDdlCommentSqlModeScope(operation.Comment);
        if (!requiresCommentSqlModeScope)
        {
            base.Generate(operation, model, builder, terminate);
            return;
        }

        if (!terminate)
        {
            throw new InvalidOperationException(
                "An ADD COLUMN operation with backslashes in its DDL comment must terminate its command.");
        }

        AppendDdlCommentSqlModeScopeStart(builder);
        base.Generate(operation, model, builder, terminate: false);
        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        AppendDdlCommentSqlModeScopeEnd(builder);
        EndStatement(builder);
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

    protected override void Generate(
        AlterColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        TryGetTemporalMigrationContract(
            operation,
            operation.Table,
            sourceContract: true,
            out var sourceTemporalContract);
        TryGetTemporalMigrationContract(
            operation,
            operation.Table,
            sourceContract: false,
            out var targetTemporalContract);

        if (sourceTemporalContract is not null
            && targetTemporalContract is not null)
        {
            if (targetTemporalContract.Support == ProviderSupportStatus.Native)
            {
                ThrowNativeTemporalSchemaChangeNotSupported(
                    operation.Table,
                    $"alter column '{operation.Name}'");
            }

            AppendDropTemporalTriggers(operation.Table, operation.Schema, builder);
            GenerateAlterColumn(operation, model, builder);
            AppendTemporalHistoryColumnAlteration(operation, model, targetTemporalContract, builder);
            AppendTemporalTriggersFromModel(
                operation.Table,
                operation.Schema,
                model,
                targetTemporalContract,
                builder);
            return;
        }

        GenerateAlterColumn(operation, model, builder);
    }

    private void GenerateAlterColumn(
        AlterColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ValidateDdlCommentSqlModeScope(operation);
        if (RequiresGeneratedColumnRecreation(operation))
        {
            builder
                .Append("ALTER TABLE ")
                .Append(DelimitMigrationIdentifier(operation.Table, operation.Schema))
                .Append(" DROP COLUMN ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);

            var requiresCommentSqlModeScope = RequiresDdlCommentSqlModeScope(operation.Comment);
            if (requiresCommentSqlModeScope)
            {
                AppendDdlCommentSqlModeScopeStart(builder);
            }

            builder
                .Append("ALTER TABLE ")
                .Append(DelimitMigrationIdentifier(operation.Table, operation.Schema))
                .Append(" ADD COLUMN ");

            ColumnDefinition(operation.Schema, operation.Table, operation.Name, operation, model, builder);

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            if (requiresCommentSqlModeScope)
            {
                AppendDdlCommentSqlModeScopeEnd(builder);
            }

            EndStatement(builder);
            return;
        }

        GenerateNullValueUpdate(operation, model, builder);

        var requiresSqlModeScope = RequiresDdlCommentSqlModeScope(operation.Comment);
        if (requiresSqlModeScope)
        {
            AppendDdlCommentSqlModeScopeStart(builder);
        }

        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(operation.Table, operation.Schema))
            .Append(" MODIFY COLUMN ");

        ColumnDefinition(operation.Schema, operation.Table, operation.Name, operation, model, builder);

        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        if (requiresSqlModeScope)
        {
            AppendDdlCommentSqlModeScopeEnd(builder);
        }

        EndStatement(builder);
    }

    protected override void Generate(
        DropColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        TryGetTemporalMigrationContract(
            operation,
            operation.Table,
            sourceContract: true,
            out var sourceTemporalContract);
        TryGetTemporalMigrationContract(
            operation,
            operation.Table,
            sourceContract: false,
            out var targetTemporalContract);

        if (sourceTemporalContract?.Support == ProviderSupportStatus.Native
            && targetTemporalContract is null
            && (IsTemporalPeriodColumn(
                    operation.Name,
                    operation,
                    MySqlAnnotationNames.TemporalSourcePeriodStartColumn)
                || IsTemporalPeriodColumn(
                    operation.Name,
                    operation,
                    MySqlAnnotationNames.TemporalSourcePeriodEndColumn)))
        {
            // MariaDB requires the system-time period and both generated period
            // columns to be removed by the same ALTER TABLE statement. The
            // table-level transition owns that atomic native operation.
            return;
        }

        if (sourceTemporalContract is not null
            && targetTemporalContract is not null)
        {
            if (targetTemporalContract.Support == ProviderSupportStatus.Native)
            {
                ThrowNativeTemporalSchemaChangeNotSupported(
                    operation.Table,
                    $"drop column '{operation.Name}'");
            }

            if (!terminate)
            {
                throw new InvalidOperationException(
                    "A temporal DROP COLUMN operation using MySQL emulation must terminate its commands.");
            }

            AppendDropTemporalTriggers(operation.Table, operation.Schema, builder);
            base.Generate(operation, model, builder, terminate: true);
            AppendTemporalHistoryColumnDrop(operation, targetTemporalContract, builder);
            AppendTemporalTriggersFromModel(
                operation.Table,
                operation.Schema,
                model,
                targetTemporalContract,
                builder);
            return;
        }

        base.Generate(operation, model, builder, terminate);
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
            .Append(operation.IsStored == true
                ? GetStoredGeneratedColumnKeyword()
                : "VIRTUAL");

        if (!operation.IsNullable
            && _mySqlSingletonOptions.Profile?.GetSupport(
                ProviderCapability.GeneratedColumnNullabilityClause) == ProviderSupportStatus.Native)
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

        DefaultValue(
            operation.DefaultValue,
            operation.DefaultValueSql,
            "longtext",
            builder);
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

        var spatialReferenceSystemId = operation.FindAnnotation(
                MySqlAnnotationNames.SpatialReferenceSystemId)
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

        DefaultValue(
            operation.DefaultValue,
            operation.DefaultValueSql,
            columnType,
            builder);
    }

    private void AppendEmulatedSpatialReferenceSystemConstraint(
        string name,
        ColumnOperation operation,
        MigrationCommandListBuilder builder
    )
    {
        var spatialReferenceSystemId = operation
            .FindAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId)
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

    private static void ValidateDdlCommentSqlModeScope(
        ColumnOperation operation,
        bool scopeRequired = false
    )
    {
        if (!scopeRequired
            && !RequiresDdlCommentSqlModeScope(operation.Comment))
        {
            return;
        }

        if (operation.DefaultValueSql?.Contains('\\') == true
            || operation.ComputedColumnSql?.Contains('\\') == true)
        {
            throw new InvalidOperationException(
                "A DDL comment containing a backslash cannot be combined with caller-authored SQL "
                + "that also contains a backslash because NO_BACKSLASH_ESCAPES would change its semantics.");
        }
    }

    private void AppendCommonColumnOptions(
        ColumnOperation operation,
        MigrationCommandListBuilder builder
    )
    {
        var isInvisible = operation.FindAnnotation(MySqlAnnotationNames.Invisible)?.Value is true;
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

        if (_mySqlSingletonOptions.Profile?.GetSupport(ProviderCapability.JsonColumns)
            == ProviderSupportStatus.Emulated)
        {
            return;
        }

        if (_mySqlSingletonOptions.Profile?.GetSupport(ProviderCapability.JsonColumns)
            == ProviderSupportStatus.Native)
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
        return _mySqlSingletonOptions.Profile?.Engine.Has(
            EngineCapability.StoredGeneratedColumnUsesPersistentKeyword) == true
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

        var defaultValueSql = operation.DefaultValueSql;
        var hasDefaultValueSql = !string.IsNullOrWhiteSpace(defaultValueSql);

        if (!hasDefaultValueSql
            && operation.DefaultValue is null)
        {
            throw new InvalidOperationException(
                $"A nullable-to-required {nameof(AlterColumnOperation)} for store type '{columnType}' "
                + "requires an explicit DefaultValue or DefaultValueSql because choosing replacement "
                + "data is an application contract.");
        }

        if (!hasDefaultValueSql
            && StoreTypeEquals(GetBaseStoreType(columnType), "timestamp"))
        {
            throw new InvalidOperationException(
                $"A nullable-to-required {nameof(AlterColumnOperation)} for store type '{columnType}' "
                + "requires DefaultValueSql because TIMESTAMP literals are interpreted using the "
                + "session time zone.");
        }

        builder
            .Append("UPDATE ")
            .Append(DelimitMigrationIdentifier(operation.Table, operation.Schema))
            .Append(" SET ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .Append(" = ");

        if (!string.IsNullOrWhiteSpace(defaultValueSql))
        {
            builder.Append(defaultValueSql);
        }
        else
        {
            var defaultValue = operation.DefaultValue!;
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

        var value = storeType
            .AsSpan()
            .Trim();
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

    private static string? NormalizeStoreTypeName(
        string? storeType
    )
    {
        if (string.IsNullOrWhiteSpace(storeType))
        {
            return null;
        }

        return GetBaseStoreType(storeType).ToString().ToLowerInvariant();
    }
}
