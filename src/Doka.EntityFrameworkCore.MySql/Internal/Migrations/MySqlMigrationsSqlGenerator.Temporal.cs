namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsSqlGenerator
{
    private const string TemporalInfinity = "9999-12-31 23:59:59.999999";

    private bool TryGetTemporalMigrationContract(
        CreateTableOperation operation,
        out TemporalMigrationContract? contract
    ) => TryGetTemporalMigrationContract(operation, operation.Name, sourceContract: false, out contract);

    private bool TryGetTemporalMigrationContract(
        MigrationOperation operation,
        string tableName,
        bool sourceContract,
        out TemporalMigrationContract? contract
    )
    {
        var isTemporalAnnotation = sourceContract
            ? MySqlAnnotationNames.TemporalSourceIsTemporal
            : MySqlAnnotationNames.IsTemporal;

        if (operation.FindAnnotation(isTemporalAnnotation)
                ?.Value is not true)
        {
            contract = null;
            return false;
        }

        var historyTableAnnotation = sourceContract
            ? MySqlAnnotationNames.TemporalSourceHistoryTable
            : MySqlAnnotationNames.TemporalHistoryTable;

        var historySchemaAnnotation = sourceContract
            ? MySqlAnnotationNames.TemporalSourceHistorySchema
            : MySqlAnnotationNames.TemporalHistorySchema;

        var periodStartAnnotation = sourceContract
            ? MySqlAnnotationNames.TemporalSourcePeriodStartColumn
            : MySqlAnnotationNames.TemporalPeriodStartColumn;

        var periodEndAnnotation = sourceContract
            ? MySqlAnnotationNames.TemporalSourcePeriodEndColumn
            : MySqlAnnotationNames.TemporalPeriodEndColumn;

        var support = Profile.GetSupport(ProviderCapability.TemporalTables);
        var periodStartColumn = GetRequiredTemporalAnnotation(operation, tableName, periodStartAnnotation);
        var periodEndColumn = GetRequiredTemporalAnnotation(operation, tableName, periodEndAnnotation);

        var historyTable = operation.FindAnnotation(historyTableAnnotation)
            ?.Value as string;

        var historySchema = operation.FindAnnotation(historySchemaAnnotation)
            ?.Value as string;

        if (support == ProviderSupportStatus.Emulated
            && string.IsNullOrWhiteSpace(historyTable))
        {
            throw new InvalidOperationException(
                $"Temporal table '{tableName}' requires an external history table on MySQL.");
        }

        contract = new TemporalMigrationContract(
            support,
            historyTable,
            historySchema,
            periodStartColumn,
            periodEndColumn);

        return true;
    }

    private bool TryAppendTemporalPeriodColumnDefinition(
        string name,
        ColumnOperation operation,
        MigrationCommandListBuilder builder
    )
    {
        var isPeriodStart = IsTemporalPeriodColumn(name, operation, MySqlAnnotationNames.TemporalPeriodStartColumn);
        var isPeriodEnd = IsTemporalPeriodColumn(name, operation, MySqlAnnotationNames.TemporalPeriodEndColumn);

        if (!isPeriodStart
            && !isPeriodEnd)
        {
            return false;
        }

        var support = Profile.GetSupport(ProviderCapability.TemporalTables);

        AppendTemporalPeriodColumnDefinition(name, isPeriodStart, support, builder);

        return true;
    }

    private void AppendTemporalPeriodColumnDefinition(
        string name,
        bool isPeriodStart,
        ProviderSupportStatus support,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .Append(DelimitMigrationIdentifier(name))
            .Append(support == ProviderSupportStatus.Native ? " timestamp(6)" : " datetime(6)");

        if (support == ProviderSupportStatus.Native)
        {
            builder.Append(isPeriodStart ? " GENERATED ALWAYS AS ROW START" : " GENERATED ALWAYS AS ROW END");
        }
        else
        {
            builder
                .Append(" NOT NULL DEFAULT ")
                .Append(isPeriodStart ? "CURRENT_TIMESTAMP(6)" : $"'{TemporalInfinity}'");
        }
    }

    private static bool IsTemporalPeriodColumn(
        string columnName,
        MigrationOperation operation,
        string annotationName
    )
    {
        var annotationValue = operation.FindAnnotation(annotationName)
            ?.Value;

        // Initial CREATE TABLE operations retain the boolean column marker,
        // whereas model transitions project the table contract as the period
        // column name. Both shapes identify the same physical column contract.
        return annotationValue is true
            || (annotationValue is string annotatedColumnName
                && string.Equals(columnName, annotatedColumnName, StringComparison.Ordinal));
    }

    private void AppendNativeTemporalPeriod(
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .AppendLine(",")
            .Append("PERIOD FOR SYSTEM_TIME (")
            .Append(DelimitMigrationIdentifier(contract.PeriodStartColumn))
            .Append(", ")
            .Append(DelimitMigrationIdentifier(contract.PeriodEndColumn))
            .AppendLine(")");
    }

    private static void AppendTemporalTableOptions(
        CreateTableOperation operation,
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        if (contract.Support == ProviderSupportStatus.Native)
        {
            builder.Append(" WITH SYSTEM VERSIONING");
            return;
        }

        if (operation.FindAnnotation(MySqlAnnotationNames.StorageEngine)
                ?.Value is null)
        {
            // Trigger side effects roll back with the source statement only when
            // both the current and history tables use a transactional engine.
            builder.Append(" ENGINE = InnoDB");
        }
    }

    private void AppendTemporalEmulation(
        CreateTableOperation operation,
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        AppendTemporalHistoryTable(operation, contract, builder);
        AppendTemporalTriggers(
            operation.Name,
            operation.Schema,
            GetTemporalTriggerColumns(operation.Columns),
            contract,
            builder);
    }

    private void AppendTemporalHistoryTable(
        CreateTableOperation operation,
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .Append("CREATE TABLE ")
            .Append(DelimitMigrationIdentifier(contract.HistoryTable!, contract.HistorySchema))
            .AppendLine(" (");

        using (builder.Indent())
        {
            for (var index = 0; index < operation.Columns.Count; index++)
            {
                var column = operation.Columns[index];
                AppendTemporalHistoryColumn(column, contract, builder);

                if (index < operation.Columns.Count - 1)
                {
                    builder.AppendLine(",");
                }
                else
                {
                    builder.AppendLine();
                }
            }
        }

        builder.Append(") ENGINE = InnoDB");

        AppendCharacterSetAndCollation(operation, builder);

        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    private void AppendTemporalHistoryColumn(
        ColumnOperation column,
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        builder.Append(DelimitMigrationIdentifier(column.Name));

        if (string.Equals(column.Name, contract.PeriodStartColumn, StringComparison.Ordinal)
            || string.Equals(column.Name, contract.PeriodEndColumn, StringComparison.Ordinal))
        {
            builder.Append(" datetime(6) NOT NULL");
            return;
        }

        var storeType = column.ColumnType
            ?? Dependencies.TypeMappingSource.FindMapping(column.ClrType)
                ?.StoreType
            ?? throw new InvalidOperationException(
                $"Could not resolve the store type for temporal history column '{column.Name}'.");

        builder
            .Append(" ")
            .Append(storeType);

        var collation = column.FindAnnotation(RelationalAnnotationNames.Collation)
            ?.Value as string;

        if (!string.IsNullOrWhiteSpace(collation))
        {
            builder
                .Append(" COLLATE ")
                .Append(collation);
        }

        if (!string.IsNullOrWhiteSpace(column.ComputedColumnSql))
        {
            AppendTemporalGeneratedColumnExpression(
                column.ComputedColumnSql,
                column.IsStored,
                column.IsNullable,
                builder);
            return;
        }

        builder.Append(column.IsNullable ? " NULL" : " NOT NULL");
    }

    private void AppendTemporalEmulation(
        string tableName,
        string? schema,
        IModel? model,
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        var table = GetRequiredTemporalTable(model, tableName, schema);
        AppendTemporalHistoryTable(table, contract, builder);
        AppendTemporalTriggers(tableName, schema, GetTemporalTriggerColumns(table.Columns), contract, builder);
    }

    private void AppendTemporalHistoryTable(
        ITable table,
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        var columns = table.Columns.ToArray();

        builder
            .Append("CREATE TABLE ")
            .Append(DelimitMigrationIdentifier(contract.HistoryTable!, contract.HistorySchema))
            .AppendLine(" (");

        using (builder.Indent())
        {
            for (var index = 0; index < columns.Length; index++)
            {
                AppendTemporalHistoryColumn(columns[index], contract, builder);

                if (index < columns.Length - 1)
                {
                    builder.AppendLine(",");
                }
                else
                {
                    builder.AppendLine();
                }
            }
        }

        builder.Append(") ENGINE = InnoDB");

        if (table.FindAnnotation(MySqlAnnotationNames.CharSet)
                ?.Value is string charSet)
        {
            builder
                .Append(" CHARACTER SET ")
                .Append(charSet);
        }

        if (table.FindAnnotation(MySqlAnnotationNames.Collation)
                ?.Value is string collation)
        {
            builder
                .Append(" COLLATE ")
                .Append(collation);
        }

        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    private void AppendTemporalHistoryColumn(
        IColumn column,
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .Append(DelimitMigrationIdentifier(column.Name))
            .Append(" ");

        if (string.Equals(column.Name, contract.PeriodStartColumn, StringComparison.Ordinal)
            || string.Equals(column.Name, contract.PeriodEndColumn, StringComparison.Ordinal))
        {
            builder.Append("datetime(6) NOT NULL");
            return;
        }

        builder.Append(column.StoreType);

        var collation = column.FindAnnotation(RelationalAnnotationNames.Collation)
            ?.Value as string;

        if (!string.IsNullOrWhiteSpace(collation))
        {
            builder
                .Append(" COLLATE ")
                .Append(collation);
        }

        if (!string.IsNullOrWhiteSpace(column.ComputedColumnSql))
        {
            AppendTemporalGeneratedColumnExpression(
                column.ComputedColumnSql,
                column.IsStored,
                column.IsNullable,
                builder);
            return;
        }

        builder.Append(column.IsNullable ? " NULL" : " NOT NULL");
    }

    private void AppendTemporalGeneratedColumnExpression(
        string computedColumnSql,
        bool? isStored,
        bool isNullable,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .Append(" GENERATED ALWAYS AS (")
            .Append(computedColumnSql)
            .Append(") ")
            .Append(isStored == true ? GetStoredGeneratedColumnKeyword() : "VIRTUAL");

        if (!isNullable
            && Profile.GetSupport(ProviderCapability.GeneratedColumnNullabilityClause) == ProviderSupportStatus.Native)
        {
            builder.Append(" NOT NULL");
        }
    }

    private static string[] GetTemporalTriggerColumns(
        IEnumerable<AddColumnOperation> columns
    ) => columns
        // MySQL prohibits OLD and NEW references to generated columns. The
        // history table recomputes them from the copied historical base values.
        .Where(column => string.IsNullOrWhiteSpace(column.ComputedColumnSql))
        .Select(column => column.Name)
        .ToArray();

    private static string[] GetTemporalTriggerColumns(
        IEnumerable<IColumn> columns
    ) => columns
        .Where(column => string.IsNullOrWhiteSpace(column.ComputedColumnSql))
        .Select(column => column.Name)
        .ToArray();

    private static ITable GetRequiredTemporalTable(
        IModel? model,
        string tableName,
        string? schema
    ) => model
            ?.GetRelationalModel()
            .FindTable(tableName, schema)
        ?? throw new InvalidOperationException(
            $"Could not resolve temporal table '{tableName}' from the target relational model.");

    private void AppendTemporalTriggersFromModel(
        string tableName,
        string? schema,
        IModel? model,
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        var table = GetRequiredTemporalTable(model, tableName, schema);
        AppendTemporalTriggers(tableName, schema, GetTemporalTriggerColumns(table.Columns), contract, builder);
    }

    private void AppendTemporalHistoryColumnAddition(
        AddColumnOperation operation,
        IModel? model,
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(contract.HistoryTable!, contract.HistorySchema))
            .Append(" ADD ");

        AppendTemporalHistoryColumn(operation, contract, builder);

        var hasTemporaryDefault = string.IsNullOrWhiteSpace(operation.ComputedColumnSql)
            && (operation.DefaultValue is not null || !string.IsNullOrWhiteSpace(operation.DefaultValueSql));

        if (hasTemporaryDefault)
        {
            var storeType = operation.ColumnType
                ?? Dependencies.TypeMappingSource.FindMapping(operation.ClrType)?.StoreType
                ?? throw new InvalidOperationException(
                    $"Could not resolve the store type for temporal history column '{operation.Name}'.");

            DefaultValue(operation.DefaultValue, operation.DefaultValueSql, storeType, builder);
        }

        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);

        if (hasTemporaryDefault)
        {
            builder
                .Append("ALTER TABLE ")
                .Append(DelimitMigrationIdentifier(contract.HistoryTable!, contract.HistorySchema))
                .Append(" ALTER COLUMN ")
                .Append(DelimitMigrationIdentifier(operation.Name))
                .Append(" DROP DEFAULT")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    private void AppendTemporalHistoryColumnAlteration(
        AlterColumnOperation operation,
        IModel? model,
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        if (RequiresGeneratedColumnRecreation(operation))
        {
            builder
                .Append("ALTER TABLE ")
                .Append(DelimitMigrationIdentifier(contract.HistoryTable!, contract.HistorySchema))
                .Append(" DROP COLUMN ")
                .Append(DelimitMigrationIdentifier(operation.Name))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);

            builder
                .Append("ALTER TABLE ")
                .Append(DelimitMigrationIdentifier(contract.HistoryTable!, contract.HistorySchema))
                .Append(" ADD ");

            AppendTemporalHistoryColumn(operation, contract, builder);

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
            return;
        }

        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(contract.HistoryTable!, contract.HistorySchema))
            .Append(" MODIFY COLUMN ");

        AppendTemporalHistoryColumn(operation, contract, builder);

        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    private void AppendTemporalHistoryColumnDrop(
        DropColumnOperation operation,
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(contract.HistoryTable!, contract.HistorySchema))
            .Append(" DROP COLUMN ")
            .Append(DelimitMigrationIdentifier(operation.Name))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    private void AppendDropTemporalTriggers(
        string tableName,
        string? schema,
        MigrationCommandListBuilder builder
    )
    {
        AppendDropTemporalTrigger(tableName, schema, TemporalTriggerEvent.Insert, builder);
        AppendDropTemporalTrigger(tableName, schema, TemporalTriggerEvent.Update, builder);
        AppendDropTemporalTrigger(tableName, schema, TemporalTriggerEvent.Delete, builder);
    }

    private void AppendDropTemporalTrigger(
        string tableName,
        string? schema,
        TemporalTriggerEvent triggerEvent,
        MigrationCommandListBuilder builder
    )
    {
        var triggerName = MySqlTemporalMetadata.CreateTriggerName(
            schema,
            tableName,
            triggerEvent
                .ToString()
                .ToLowerInvariant());

        builder
            .Append("DROP TRIGGER IF EXISTS ")
            .Append(DelimitMigrationIdentifier(triggerName, schema))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    private void AppendDropTemporalHistoryTable(
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .Append("DROP TABLE ")
            .Append(DelimitMigrationIdentifier(contract.HistoryTable!, contract.HistorySchema))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    private void AppendNativeTemporalActivation(
        string tableName,
        string? schema,
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(tableName, schema))
            .Append(" ADD ");
        AppendTemporalPeriodColumnDefinition(
            contract.PeriodStartColumn,
            isPeriodStart: true,
            ProviderSupportStatus.Native,
            builder);
        builder.Append(", ADD ");
        AppendTemporalPeriodColumnDefinition(
            contract.PeriodEndColumn,
            isPeriodStart: false,
            ProviderSupportStatus.Native,
            builder);
        builder
            .Append(", ADD PERIOD FOR SYSTEM_TIME (")
            .Append(DelimitMigrationIdentifier(contract.PeriodStartColumn))
            .Append(", ")
            .Append(DelimitMigrationIdentifier(contract.PeriodEndColumn))
            .Append("), ADD SYSTEM VERSIONING")
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    private void AppendNativeTemporalDeactivation(
        string tableName,
        string? schema,
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        builder
            // MariaDB rejects structural changes to a system-versioned table in
            // its safe default mode. SET STATEMENT confines KEEP to this one
            // destructive deactivation statement and restores the session value
            // even when execution fails.
            .Append("SET STATEMENT system_versioning_alter_history=KEEP FOR ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(tableName, schema))
            .Append(" DROP SYSTEM VERSIONING, DROP PERIOD FOR SYSTEM_TIME, DROP COLUMN ")
            .Append(DelimitMigrationIdentifier(contract.PeriodStartColumn))
            .Append(", DROP COLUMN ")
            .Append(DelimitMigrationIdentifier(contract.PeriodEndColumn))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    private static void ThrowNativeTemporalSchemaChangeNotSupported(
        string tableName,
        string operationDescription
    ) => throw new InvalidOperationException(
        $"Cannot {operationDescription} on native MariaDB temporal table '{tableName}'. "
        + "MariaDB's safe system-versioning alteration mode rejects structural changes, while its permissive "
        + "mode can make retained history inaccurate. Migrate the data through an explicitly reviewed "
        + "replacement-table operation instead of weakening history correctness.");

    private static void AppendCharacterSetAndCollation(
        CreateTableOperation operation,
        MigrationCommandListBuilder builder
    )
    {
        if (operation.FindAnnotation(MySqlAnnotationNames.CharSet)
                ?.Value is string charSet)
        {
            builder
                .Append(" CHARACTER SET ")
                .Append(charSet);
        }

        if (operation.FindAnnotation(MySqlAnnotationNames.Collation)
                ?.Value is string collation)
        {
            builder
                .Append(" COLLATE ")
                .Append(collation);
        }
    }

    private void AppendTemporalTriggers(
        string tableName,
        string? schema,
        IReadOnlyList<string> columns,
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        AppendTemporalTrigger(tableName, schema, columns, contract, TemporalTriggerEvent.Insert, builder);
        AppendTemporalTrigger(tableName, schema, columns, contract, TemporalTriggerEvent.Update, builder);
        AppendTemporalTrigger(tableName, schema, columns, contract, TemporalTriggerEvent.Delete, builder);
    }

    private void AppendTemporalTrigger(
        string tableName,
        string? schema,
        IReadOnlyList<string> columns,
        TemporalMigrationContract contract,
        TemporalTriggerEvent triggerEvent,
        MigrationCommandListBuilder builder
    )
    {
        var eventName = triggerEvent
            .ToString()
            .ToLowerInvariant();

        var triggerName = MySqlTemporalMetadata.CreateTriggerName(schema, tableName, eventName);

        builder
            .Append("CREATE TRIGGER ")
            .Append(DelimitMigrationIdentifier(triggerName, schema))
            .Append(" BEFORE ")
            .Append(
                triggerEvent
                    .ToString()
                    .ToUpperInvariant())
            .Append(" ON ")
            .Append(DelimitMigrationIdentifier(tableName, schema))
            .AppendLine(" FOR EACH ROW")
            .AppendLine("BEGIN");

        using (builder.Indent())
        {
            builder
                // Reverse engineering recognizes only this provider-owned marker;
                // user triggers with similar SQL remain ordinary database objects.
                .Append("/* ")
                .Append(
                    MySqlTemporalMetadata.CreateEmulationMarker(
                        contract.HistorySchema,
                        contract.HistoryTable!,
                        contract.PeriodStartColumn,
                        contract.PeriodEndColumn))
                .AppendLine(" */");

            if (triggerEvent == TemporalTriggerEvent.Insert)
            {
                AppendSetNewPeriod(contract, "UTC_TIMESTAMP(6)", builder);
            }
            else
            {
                builder
                    .AppendLine("DECLARE __doka_temporal_timestamp datetime(6);")
                    .AppendLine("SET __doka_temporal_timestamp = UTC_TIMESTAMP(6);");
                AppendTemporalHistoryInsert(columns, contract, builder);

                if (triggerEvent == TemporalTriggerEvent.Update)
                {
                    AppendSetNewPeriod(contract, "__doka_temporal_timestamp", builder);
                }
            }
        }

        builder
            .Append("END")
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    private void AppendTemporalHistoryInsert(
        IReadOnlyList<string> columns,
        TemporalMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .Append("INSERT INTO ")
            .Append(DelimitMigrationIdentifier(contract.HistoryTable!, contract.HistorySchema))
            .Append(" (");

        for (var index = 0; index < columns.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(DelimitMigrationIdentifier(columns[index]));
        }

        builder.Append(") VALUES (");

        for (var index = 0; index < columns.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            var column = columns[index];

            if (string.Equals(column, contract.PeriodEndColumn, StringComparison.Ordinal))
            {
                builder.Append("__doka_temporal_timestamp");
            }
            else
            {
                builder
                    .Append("OLD.")
                    .Append(DelimitMigrationIdentifier(column));
            }
        }

        builder.AppendLine(");");
    }

    private void AppendSetNewPeriod(
        TemporalMigrationContract contract,
        string timestampExpression,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .Append("SET NEW.")
            .Append(DelimitMigrationIdentifier(contract.PeriodStartColumn))
            .Append(" = ")
            .Append(timestampExpression)
            .AppendLine(";")
            .Append("SET NEW.")
            .Append(DelimitMigrationIdentifier(contract.PeriodEndColumn))
            .Append(" = '")
            .Append(TemporalInfinity)
            .AppendLine("';");
    }

    private static string GetRequiredTemporalAnnotation(
        MigrationOperation operation,
        string tableName,
        string annotationName
    ) => operation.FindAnnotation(annotationName)
            ?.Value as string
        ?? throw new InvalidOperationException(
            $"Temporal table '{tableName}' is missing required annotation '{annotationName}'.");

    private enum TemporalTriggerEvent
    {
        Insert,
        Update,
        Delete,
    }

    private sealed class TemporalMigrationContract
    {
        public TemporalMigrationContract(
            ProviderSupportStatus support,
            string? historyTable,
            string? historySchema,
            string periodStartColumn,
            string periodEndColumn
        )
        {
            Support = support;
            HistoryTable = historyTable;
            HistorySchema = historySchema;
            PeriodStartColumn = periodStartColumn;
            PeriodEndColumn = periodEndColumn;
        }

        public ProviderSupportStatus Support { get; }

        public string? HistoryTable { get; }

        public string? HistorySchema { get; }

        public string PeriodStartColumn { get; }

        public string PeriodEndColumn { get; }
    }
}
