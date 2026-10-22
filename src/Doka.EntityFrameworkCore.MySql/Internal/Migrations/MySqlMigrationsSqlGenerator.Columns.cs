namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsSqlGenerator
{
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
        ValidateRequiredColumnAddition(operation);
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
        ValidateGuidStorageFormatTransition(operation);
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

    private static void ValidateGuidStorageFormatTransition(
        AlterColumnOperation operation
    )
    {
        var sourceNativeFormat = GetNativeGuidFormat(operation.OldColumn);
        var targetNativeFormat = GetNativeGuidFormat(operation);

        if (sourceNativeFormat is null
            && targetNativeFormat is null)
        {
            return;
        }

        var sourceFormat = sourceNativeFormat ?? InferGuidStorageFormat(operation.OldColumn);
        var targetFormat = targetNativeFormat ?? InferGuidStorageFormat(operation);

        if (sourceNativeFormat is not null
            && targetNativeFormat is not null
            && sourceFormat == targetFormat)
        {
            return;
        }

        // The documented text migration path is limited to canonical
        // 36-character values. An unannotated binary converter carries no
        // byte-order contract, so an equal binary(16) store type cannot prove
        // representation equivalence.
        if ((sourceNativeFormat == MySqlGuidFormat.Char36
                && targetNativeFormat is null
                && targetFormat == MySqlGuidFormat.Char36)
            || (targetNativeFormat == MySqlGuidFormat.Char36
                && sourceNativeFormat is null
                && sourceFormat == MySqlGuidFormat.Char36))
        {
            return;
        }

        throw new InvalidOperationException(
            "A transition between provider-owned GUID storage and a different or unproven value representation requires "
            + "an explicit data migration. Add a nullable destination column, backfill and verify every value, "
            + "then replace the source column and restore its constraints.");
    }

    private void ValidateNativeUuidSupport(
        ColumnOperation operation
    )
    {
        if (GetNativeGuidFormat(operation) == MySqlGuidFormat.Uuid
            || operation.ColumnType.AsSpan().Trim().Equals("uuid", StringComparison.OrdinalIgnoreCase))
        {
            MySqlMariaDbUuidSupport.EnsureSupported(Profile);
        }
    }

    private static MySqlGuidFormat? GetNativeGuidFormat(
        ColumnOperation operation
    ) => operation.FindAnnotation(MySqlAnnotationNames.GuidFormat)?.Value is MySqlGuidFormat format
        ? format
        : null;

    private static MySqlGuidFormat? InferGuidStorageFormat(
        ColumnOperation operation
    )
    {
        if (MatchesGuidStoreType(operation, "char", 36)
            || MatchesGuidStoreType(operation, "varchar", 36))
        {
            return MySqlGuidFormat.Char36;
        }

        if (MatchesGuidStoreType(operation, "binary", 16)
            || MatchesGuidStoreType(operation, "varbinary", 16))
        {
            return MySqlGuidFormat.Binary16;
        }

        if (operation.ColumnType.AsSpan().Trim().Equals("uuid", StringComparison.OrdinalIgnoreCase))
        {
            return MySqlGuidFormat.Uuid;
        }

        return null;
    }

    private static bool MatchesGuidStoreType(
        ColumnOperation operation,
        string typeName,
        int expectedSize
    )
    {
        var storeType = operation.ColumnType.AsSpan().Trim();

        if (!storeType.StartsWith(typeName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = storeType[typeName.Length..];

        if (!suffix.IsEmpty
            && !char.IsWhiteSpace(suffix[0])
            && suffix[0] != '(')
        {
            return false;
        }

        suffix = suffix.TrimStart();

        if (suffix.IsEmpty
            || suffix[0] != '(')
        {
            return operation.MaxLength == expectedSize;
        }

        var closingParenthesis = suffix.IndexOf(')');

        return closingParenthesis > 1
            && int.TryParse(
                suffix[1..closingParenthesis].Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var declaredSize)
            && declaredSize == expectedSize;
    }

    private static void ValidateRequiredColumnAddition(
        AddColumnOperation operation
    )
    {
        if (operation.FindAnnotation(MySqlAnnotationNames.RequiresExplicitBackfill)?.Value is not true
            || operation.IsNullable
            || operation.DefaultValue is not null
            || !string.IsNullOrWhiteSpace(operation.DefaultValueSql)
            || !string.IsNullOrWhiteSpace(operation.ComputedColumnSql)
            || IsAutoIncrementColumn(operation)
            || IsTemporalPeriodColumn(
                operation.Name,
                operation,
                MySqlAnnotationNames.TemporalPeriodStartColumn)
            || IsTemporalPeriodColumn(
                operation.Name,
                operation,
                MySqlAnnotationNames.TemporalPeriodEndColumn)
            || (operation.IsRowVersion && IsTemporalRowVersionColumn(operation)))
        {
            return;
        }

        throw new InvalidOperationException(
            $"A required {nameof(AddColumnOperation)} requires an explicit DefaultValue or DefaultValueSql "
            + "because choosing replacement data for existing rows is an application contract.");
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
