namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsSqlGenerator
{
    protected override void Generate(
        AlterDatabaseOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        var charSet = GetValidatedOptionalIdentifierAnnotation(operation, MySqlAnnotationNames.CharSet);
        var collation = GetValidatedDatabaseCollation(operation);
        var sourceCharSet = GetValidatedOptionalIdentifierAnnotation(
            operation.OldDatabase,
            MySqlAnnotationNames.CharSet);
        var sourceCollation = ValidateOptionalIdentifier(
            operation.OldDatabase.Collation,
            RelationalAnnotationNames.Collation);

        ValidateCharacterSetAndCollation(charSet, collation);
        ValidateCharacterSetAndCollation(sourceCharSet, sourceCollation);

        if (charSet is null && collation is null)
        {
            if (sourceCharSet is not null || sourceCollation is not null)
            {
                throw new InvalidOperationException(
                    "Removing explicit database character-set or collation metadata requires an explicit target value.");
            }

            return;
        }

        builder.Append("ALTER DATABASE");

        if (charSet is not null)
        {
            builder
                .Append(" CHARACTER SET = ")
                .Append(charSet);
        }

        if (collation is not null)
        {
            builder
                .Append(" COLLATE = ")
                .Append(collation);
        }

        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

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

        TryGetTemporalMigrationContract(operation, out var temporalContract);
        TryGetApplicationTimeMigrationContract(
            operation,
            operation.Name,
            sourceContract: false,
            out var applicationTimeContract);

        if (temporalContract?.Support == ProviderSupportStatus.Emulated && !terminate)
        {
            throw new InvalidOperationException(
                "A temporal CREATE TABLE operation using MySQL emulation must terminate its commands.");
        }

        ValidateDdlCommentSqlModeScope(operation);
        var requiresCommentSqlModeScope = RequiresDdlCommentSqlModeScope(operation);
        if (requiresCommentSqlModeScope)
        {
            if (!terminate)
            {
                throw new InvalidOperationException(
                    "A CREATE TABLE operation with backslashes in DDL comments must terminate its command.");
            }

            AppendDdlCommentSqlModeScopeStart(builder);
        }

        builder
            .Append("CREATE TABLE ")
            .Append(DelimitMigrationIdentifier(operation.Name, operation.Schema))
            .AppendLine(" (");

        using (builder.Indent())
        {
            CreateTableColumns(operation, model, builder);
            CreateTableConstraints(operation, model, builder);
            AppendAutoIncrementSupportingIndex(operation, builder);

            if (applicationTimeContract is not null)
            {
                AppendApplicationTimePeriod(applicationTimeContract, builder);
            }

            if (temporalContract?.Support == ProviderSupportStatus.Native)
            {
                AppendNativeTemporalPeriod(temporalContract, builder);
            }
        }

        builder.Append(")");

        AppendTableOptions(operation, builder);

        if (temporalContract is not null)
        {
            AppendTemporalTableOptions(operation, temporalContract, builder);
        }

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            if (requiresCommentSqlModeScope)
            {
                AppendDdlCommentSqlModeScopeEnd(builder);
            }

            EndStatement(builder);

            if (temporalContract?.Support == ProviderSupportStatus.Emulated)
            {
                AppendTemporalEmulation(operation, temporalContract, builder);
            }
        }
    }

    protected override void Generate(
        AlterTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        TryGetTemporalMigrationContract(
            operation,
            operation.Name,
            sourceContract: true,
            out var sourceTemporalContract);
        TryGetTemporalMigrationContract(
            operation,
            operation.Name,
            sourceContract: false,
            out var targetTemporalContract);

        var generatedTemporalTransition = false;

        if (sourceTemporalContract is null && targetTemporalContract is not null)
        {
            if (targetTemporalContract.Support == ProviderSupportStatus.Native)
            {
                AppendNativeTemporalActivation(
                    operation.Name,
                    operation.Schema,
                    targetTemporalContract,
                    builder);
            }
            else
            {
                AppendTemporalEmulation(
                    operation.Name,
                    operation.Schema,
                    model,
                    targetTemporalContract,
                    builder);
            }

            generatedTemporalTransition = true;
        }
        else if (sourceTemporalContract is not null && targetTemporalContract is null)
        {
            if (sourceTemporalContract.Support == ProviderSupportStatus.Native)
            {
                AppendNativeTemporalDeactivation(
                    operation.Name,
                    operation.Schema,
                    sourceTemporalContract,
                    builder);
            }
            else
            {
                AppendDropTemporalTriggers(operation.Name, operation.Schema, builder);
                AppendDropTemporalHistoryTable(sourceTemporalContract, builder);
            }

            generatedTemporalTransition = true;
        }

        var generatedApplicationTimeTransition = AppendApplicationTimeTransition(operation, builder);
        AppendTableOptionTransition(operation, builder);

        if (generatedTemporalTransition || generatedApplicationTimeTransition)
        {
            return;
        }

        if (sourceTemporalContract?.Support == ProviderSupportStatus.Native
            && targetTemporalContract?.Support == ProviderSupportStatus.Native
            && !string.Equals(operation.Comment, operation.OldTable.Comment, StringComparison.Ordinal))
        {
            ThrowNativeTemporalSchemaChangeNotSupported(
                operation.Name,
                "alter its table comment");
        }

        if (string.Equals(operation.Comment, operation.OldTable.Comment, StringComparison.Ordinal))
        {
            return;
        }

        var requiresCommentSqlModeScope = RequiresDdlCommentSqlModeScope(operation.Comment);
        if (requiresCommentSqlModeScope)
        {
            AppendDdlCommentSqlModeScopeStart(builder);
        }

        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(operation.Name, operation.Schema))
            .Append(" COMMENT = ")
            .Append(MySqlSqlLiteralGenerator.GenerateDdlComment(operation.Comment ?? string.Empty))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        if (requiresCommentSqlModeScope)
        {
            AppendDdlCommentSqlModeScopeEnd(builder);
        }

        EndStatement(builder);
    }

    private void AppendTableOptionTransition(
        AlterTableOperation operation,
        MigrationCommandListBuilder builder
    )
    {
        var targetCharSet = GetValidatedOptionalIdentifierAnnotation(
            operation,
            MySqlAnnotationNames.CharSet);
        var sourceCharSet = GetValidatedOptionalIdentifierAnnotation(
            operation.OldTable,
            MySqlAnnotationNames.CharSet);
        var targetCollation = GetValidatedTableCollation(operation);
        var sourceCollation = GetValidatedTableCollation(operation.OldTable);
        var targetStorageEngine = GetValidatedOptionalIdentifierAnnotation(
            operation,
            MySqlAnnotationNames.StorageEngine);
        var sourceStorageEngine = GetValidatedOptionalIdentifierAnnotation(
            operation.OldTable,
            MySqlAnnotationNames.StorageEngine);

        ValidateCharacterSetAndCollation(targetCharSet, targetCollation);
        ValidateCharacterSetAndCollation(sourceCharSet, sourceCollation);

        var charSetChanged = !string.Equals(
            targetCharSet,
            sourceCharSet,
            StringComparison.OrdinalIgnoreCase);
        var collationChanged = !string.Equals(
            targetCollation,
            sourceCollation,
            StringComparison.OrdinalIgnoreCase);
        var storageEngineChanged = !string.Equals(
            targetStorageEngine,
            sourceStorageEngine,
            StringComparison.OrdinalIgnoreCase);
        var appendCollation = collationChanged
            || (charSetChanged && targetCollation is not null);

        if (!charSetChanged && !collationChanged && !storageEngineChanged)
        {
            return;
        }

        if (charSetChanged && targetCharSet is null)
        {
            throw new InvalidOperationException(
                $"Removing an explicit table '{MySqlAnnotationNames.CharSet}' requires an explicit target value.");
        }

        if (collationChanged && targetCollation is null)
        {
            throw new InvalidOperationException(
                $"Removing an explicit table '{RelationalAnnotationNames.Collation}' requires an explicit target value.");
        }

        if (storageEngineChanged && targetStorageEngine is null)
        {
            throw new InvalidOperationException(
                $"Removing an explicit table '{MySqlAnnotationNames.StorageEngine}' requires an explicit target value.");
        }

        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(operation.Name, operation.Schema));

        if (charSetChanged)
        {
            builder
                .Append(" DEFAULT CHARACTER SET = ")
                .Append(targetCharSet!);
        }

        if (appendCollation)
        {
            builder
                .Append(" DEFAULT COLLATE = ")
                .Append(targetCollation!);
        }

        if (storageEngineChanged)
        {
            builder
                .Append(" ENGINE = ")
                .Append(targetStorageEngine!);
        }

        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
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

        builder
            .Append("CREATE DATABASE IF NOT EXISTS ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        EndStatement(builder);
    }

    private void AppendTableOptions(
        CreateTableOperation operation,
        MigrationCommandListBuilder builder
    )
    {
        var charSet = GetValidatedOptionalIdentifierAnnotation(operation, MySqlAnnotationNames.CharSet);
        var collation = GetValidatedTableCollation(operation);
        var storageEngine = GetValidatedOptionalIdentifierAnnotation(
            operation,
            MySqlAnnotationNames.StorageEngine);
        var comment = operation.Comment;

        ValidateCharacterSetAndCollation(charSet, collation);

        if (charSet is not null)
        {
            builder
                .Append(" CHARACTER SET ")
                .Append(charSet);
        }

        if (collation is not null)
        {
            builder
                .Append(" COLLATE ")
                .Append(collation);
        }

        if (storageEngine is not null)
        {
            builder
                .Append(" ENGINE = ")
                .Append(storageEngine);
        }

        if (!string.IsNullOrWhiteSpace(comment))
        {
            builder
                .Append(" COMMENT = ")
                .Append(MySqlSqlLiteralGenerator.GenerateDdlComment(comment));
        }
    }

    private static string? GetValidatedDatabaseCollation(
        AlterDatabaseOperation operation
    )
    {
        if (operation.FindAnnotation(MySqlAnnotationNames.Collation) is not null)
        {
            throw new InvalidOperationException(
                $"Database collation must use '{nameof(AlterDatabaseOperation.Collation)}' rather than "
                + $"the table-level '{MySqlAnnotationNames.Collation}' annotation.");
        }

        return ValidateOptionalIdentifier(operation.Collation, RelationalAnnotationNames.Collation);
    }

    private static string? GetValidatedTableCollation(
        IReadOnlyAnnotatable annotatable
    )
    {
        var relationalCollation = GetValidatedOptionalIdentifierAnnotation(
            annotatable,
            RelationalAnnotationNames.Collation);
        var providerCollation = GetValidatedOptionalIdentifierAnnotation(
            annotatable,
            MySqlAnnotationNames.Collation);

        if (relationalCollation is not null
            && providerCollation is not null
            && !string.Equals(relationalCollation, providerCollation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Table collation metadata conflicts between '{RelationalAnnotationNames.Collation}' and "
                + $"'{MySqlAnnotationNames.Collation}'.");
        }

        return relationalCollation ?? providerCollation;
    }

    private static string? GetValidatedOptionalIdentifierAnnotation(
        IReadOnlyAnnotatable annotatable,
        string annotationName
    )
    {
        var annotation = annotatable.FindAnnotation(annotationName);
        if (annotation is null)
        {
            return null;
        }

        if (annotation.Value is not string value)
        {
            throw new InvalidOperationException(
                $"The '{annotationName}' annotation must contain a non-empty string.");
        }

        return ValidateOptionalIdentifier(value, annotationName);
    }

    private static string? ValidateOptionalIdentifier(
        string? value,
        string metadataName
    ) => value is null
        ? null
        : MySqlSqlTokenValidator.ValidateIdentifier(value, metadataName);

    private void ValidateCharacterSetAndCollation(
        string? charSet,
        string? collation
    )
    {
        if (charSet is null || collation is null)
        {
            return;
        }

        var separator = collation.IndexOf('_');
        var collationCharSet = separator > 0 ? collation[..separator] : collation;

        if (string.Equals(charSet, collationCharSet, StringComparison.OrdinalIgnoreCase)
            || IsUtf8Mb3AliasPair(charSet, collationCharSet)
            || IsMariaDbSharedUca1400Pair(charSet, collation))
        {
            return;
        }

        throw new InvalidOperationException(
            $"The configured '{MySqlAnnotationNames.CharSet}' and '{RelationalAnnotationNames.Collation}' "
            + "values do not identify the same MySQL character set.");
    }

    private bool IsMariaDbSharedUca1400Pair(
        string charSet,
        string collation
    )
    {
        if (!Profile.Engine.Has(EngineCapability.SharedUca1400Collations)
            || !collation.StartsWith("uca1400_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // MariaDB exposes unprefixed UCA-14 collations with no single Charset
        // because the same collation is available to multiple Unicode sets.
        return charSet.Equals("ucs2", StringComparison.OrdinalIgnoreCase)
            || charSet.Equals("utf8", StringComparison.OrdinalIgnoreCase)
            || charSet.Equals("utf8mb3", StringComparison.OrdinalIgnoreCase)
            || charSet.Equals("utf8mb4", StringComparison.OrdinalIgnoreCase)
            || charSet.Equals("utf16", StringComparison.OrdinalIgnoreCase)
            || charSet.Equals("utf32", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUtf8Mb3AliasPair(
        string charSet,
        string collationCharSet
    ) => (string.Equals(charSet, "utf8", StringComparison.OrdinalIgnoreCase)
            && string.Equals(collationCharSet, "utf8mb3", StringComparison.OrdinalIgnoreCase))
        || (string.Equals(charSet, "utf8mb3", StringComparison.OrdinalIgnoreCase)
            && string.Equals(collationCharSet, "utf8", StringComparison.OrdinalIgnoreCase));

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

        var newSchema = operation.NewSchema ?? operation.Schema;

        if (string.Equals(operation.Name, newName, StringComparison.Ordinal)
            && string.Equals(operation.Schema, newSchema, StringComparison.Ordinal))
        {
            return;
        }

        TryGetTemporalMigrationContract(
            operation,
            operation.Name,
            sourceContract: true,
            out var sourceTemporalContract);
        TryGetTemporalMigrationContract(
            operation,
            newName,
            sourceContract: false,
            out var targetTemporalContract);

        if (sourceTemporalContract is not null
            && targetTemporalContract?.Support == ProviderSupportStatus.Native)
        {
            ThrowNativeTemporalSchemaChangeNotSupported(
                operation.Name,
                $"rename the table to '{newName}'");
        }

        if (sourceTemporalContract?.Support == ProviderSupportStatus.Emulated
            && targetTemporalContract?.Support == ProviderSupportStatus.Emulated)
        {
            AppendDropTemporalTriggers(operation.Name, operation.Schema, builder);
            AppendRenameTable(
                operation.Name,
                operation.Schema,
                newName,
                newSchema,
                builder);

            if (!string.Equals(
                    sourceTemporalContract.HistoryTable,
                    targetTemporalContract.HistoryTable,
                    StringComparison.Ordinal)
                || !string.Equals(
                    sourceTemporalContract.HistorySchema,
                    targetTemporalContract.HistorySchema,
                    StringComparison.Ordinal))
            {
                AppendRenameTable(
                    sourceTemporalContract.HistoryTable!,
                    sourceTemporalContract.HistorySchema,
                    targetTemporalContract.HistoryTable!,
                    targetTemporalContract.HistorySchema,
                    builder);
            }

            AppendTemporalTriggersFromModel(
                newName,
                newSchema,
                model,
                targetTemporalContract,
                builder);
            return;
        }

        AppendRenameTable(
            operation.Name,
            operation.Schema,
            newName,
            newSchema,
            builder);
    }

    private void AppendRenameTable(
        string name,
        string? schema,
        string newName,
        string? newSchema,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .Append("RENAME TABLE ")
            .Append(DelimitMigrationIdentifier(name, schema))
            .Append(" TO ")
            .Append(DelimitMigrationIdentifier(newName, newSchema))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        builder.EndCommand();
    }

    protected override void Generate(
        DropTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        TryGetTemporalMigrationContract(
            operation,
            operation.Name,
            sourceContract: true,
            out var sourceTemporalContract);

        if (sourceTemporalContract?.Support == ProviderSupportStatus.Emulated && !terminate)
        {
            throw new InvalidOperationException(
                "A temporal DROP TABLE operation using MySQL emulation must terminate its commands.");
        }

        base.Generate(operation, model, builder, terminate);

        if (terminate && sourceTemporalContract?.Support == ProviderSupportStatus.Emulated)
        {
            // MySQL drops the provider-owned triggers together with the current
            // table, but the external history table has its own lifecycle.
            AppendDropTemporalHistoryTable(sourceTemporalContract, builder);
        }
    }

    /// <summary>
    /// Generates MySQL-specific column rename syntax. The two engines diverge here:
    /// MySQL 8.0+ and MariaDB 10.5.2+ accept the modern
    /// <c>ALTER TABLE ... RENAME COLUMN old TO new</c> form; older MariaDB versions
    /// require <c>ALTER TABLE ... CHANGE COLUMN old new &lt;full column definition&gt;</c>.
    /// The engine choice is read from the active <see cref="ProviderProfile"/> via
    /// <see cref="ProviderCapability.RenameColumn"/>; the fallback path resolves
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
                    $"rename column '{operation.Name}' to '{operation.NewName}'");
            }

            AppendDropTemporalTriggers(operation.Table, operation.Schema, builder);
            GenerateRenameColumn(operation, model, builder);

            builder
                .Append("ALTER TABLE ")
                .Append(DelimitMigrationIdentifier(
                    targetTemporalContract.HistoryTable!,
                    targetTemporalContract.HistorySchema))
                .Append(" RENAME COLUMN ")
                .Append(DelimitMigrationIdentifier(operation.Name))
                .Append(" TO ")
                .Append(DelimitMigrationIdentifier(operation.NewName))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);

            AppendTemporalTriggersFromModel(
                operation.Table,
                operation.Schema,
                model,
                targetTemporalContract,
                builder);
            return;
        }

        GenerateRenameColumn(operation, model, builder);
    }

    private void GenerateRenameColumn(
        RenameColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {

        if (Profile.GetSupport(ProviderCapability.RenameColumn) == ProviderSupportStatus.Native)
        {
            builder
                .Append("ALTER TABLE ")
                .Append(DelimitMigrationIdentifier(operation.Table, operation.Schema))
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

        var requiresCommentSqlModeScope = RequiresDdlCommentSqlModeScope(columnOperation.Comment);
        ValidateDdlCommentSqlModeScope(columnOperation);
        if (requiresCommentSqlModeScope)
        {
            AppendDdlCommentSqlModeScopeStart(builder);
        }

        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(operation.Table, operation.Schema))
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

        if (requiresCommentSqlModeScope)
        {
            AppendDdlCommentSqlModeScopeEnd(builder);
        }

        builder.EndCommand();
    }

    private static void ValidateDdlCommentSqlModeScope(
        CreateTableOperation operation
    )
    {
        if (!RequiresDdlCommentSqlModeScope(operation))
        {
            return;
        }

        foreach (var column in operation.Columns)
        {
            ValidateDdlCommentSqlModeScope(column, scopeRequired: true);
        }

        if (operation.CheckConstraints.Any(
                static constraint => constraint.Sql.Contains('\\')))
        {
            throw new InvalidOperationException(
                "DDL comments containing backslashes cannot be combined with a check constraint "
                + "whose caller-authored SQL also contains a backslash.");
        }
    }
}
