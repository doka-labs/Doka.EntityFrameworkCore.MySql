namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Reconstructs the provider temporal contract from engine-owned metadata. MariaDB
/// exposes native system-versioned tables and period-column flags directly. MySQL
/// emulation is recognized only when the complete provider trigger trio, marker,
/// transactional table engines, and current/history column shapes agree. Anything
/// incomplete or modified remains an ordinary table instead of being guessed temporal.
/// </summary>
internal static class TemporalTableLoader
{
    private const string TemporalInfinity = "9999-12-31 23:59:59.999999";

    public static void Load(
        ScaffoldingPipelineContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Profile.GetSupport(ProviderCapability.TemporalTables) == ProviderSupportStatus.Native)
        {
            LoadNativeMariaDbMetadata(context);
            return;
        }

        if (context.Profile.GetSupport(ProviderCapability.TemporalTables) == ProviderSupportStatus.Emulated)
        {
            LoadMySqlEmulationMetadata(context);
        }
    }

    private static void LoadNativeMariaDbMetadata(
        ScaffoldingPipelineContext context
    )
    {
        var temporalTables = context
            .TableLookup.Values.Where(table => table.FindAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal)
                ?.Value is true)
            .ToArray();

        if (temporalTables.Length == 0)
        {
            return;
        }

        var periodColumns = context.Profile.Engine.Has(EngineCapability.TemporalPeriodCatalog)
            ? LoadCatalogPeriodColumns(context)
            : LoadColumnExpressionPeriodColumns(temporalTables);

        ApplyNativePeriodColumns(temporalTables, periodColumns);
    }

    private static Dictionary<string, NativePeriodColumns> LoadCatalogPeriodColumns(
        ScaffoldingPipelineContext context
    )
    {
        using var command = context.Connection.CreateCommand();
        command.CommandText = """
                              SELECT
                                  TABLE_NAME,
                                  COLUMN_NAME,
                                  IS_SYSTEM_TIME_PERIOD_START,
                                  IS_SYSTEM_TIME_PERIOD_END
                              FROM information_schema.COLUMNS
                              WHERE TABLE_SCHEMA = DATABASE()
                                AND (IS_SYSTEM_TIME_PERIOD_START = 'YES'
                                     OR IS_SYSTEM_TIME_PERIOD_END = 'YES')
                              ORDER BY TABLE_NAME, ORDINAL_POSITION;
                              """;

        using var reader = command.ExecuteReader();
        var periodColumns = new Dictionary<string, NativePeriodColumns>(StringComparer.Ordinal);

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!context.TableLookup.ContainsKey(tableName))
            {
                continue;
            }

            if (!periodColumns.TryGetValue(tableName, out var columns))
            {
                columns = new NativePeriodColumns();
                periodColumns[tableName] = columns;
            }

            var columnName = reader.GetString(1);

            if (string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase))
            {
                columns.StartColumns.Add(columnName);
            }

            if (string.Equals(reader.GetString(3), "YES", StringComparison.OrdinalIgnoreCase))
            {
                columns.EndColumns.Add(columnName);
            }
        }

        return periodColumns;
    }

    private static Dictionary<string, NativePeriodColumns> LoadColumnExpressionPeriodColumns(
        IEnumerable<DatabaseTable> temporalTables
    )
    {
        var periodColumns = new Dictionary<string, NativePeriodColumns>(StringComparer.Ordinal);

        // MariaDB exposed ROW START/ROW END through GENERATION_EXPRESSION
        // before the dedicated period catalogs arrived in 11.4. The values are
        // engine metadata, so this fallback remains independent of user naming.
        foreach (var table in temporalTables)
        {
            var columns = new NativePeriodColumns();

            foreach (var column in table.Columns)
            {
                var expression = column.ComputedColumnSql?.Trim();

                if (string.Equals(expression, "ROW START", StringComparison.OrdinalIgnoreCase))
                {
                    columns.StartColumns.Add(column.Name);
                }
                else if (string.Equals(expression, "ROW END", StringComparison.OrdinalIgnoreCase))
                {
                    columns.EndColumns.Add(column.Name);
                }
            }

            periodColumns[table.Name] = columns;
        }

        return periodColumns;
    }

    private static void ApplyNativePeriodColumns(
        IEnumerable<DatabaseTable> temporalTables,
        IReadOnlyDictionary<string, NativePeriodColumns> periodColumns
    )
    {
        foreach (var table in temporalTables)
        {
            if (!periodColumns.TryGetValue(table.Name, out var columns)
                || columns.StartColumns.Count != 1
                || columns.EndColumns.Count != 1
                || string.Equals(columns.StartColumns[0], columns.EndColumns[0], StringComparison.Ordinal)
                || table.Columns.All(column => !string.Equals(
                    column.Name,
                    columns.StartColumns[0],
                    StringComparison.Ordinal))
                || table.Columns.All(column => !string.Equals(
                    column.Name,
                    columns.EndColumns[0],
                    StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Native MariaDB temporal table '{table.Name}' does not expose exactly one valid "
                    + "system-time period start and end column.");
            }

            table.SetAnnotation(MySqlAnnotationNames.TemporalSourcePeriodStartColumn, columns.StartColumns[0]);
            table.SetAnnotation(MySqlAnnotationNames.TemporalSourcePeriodEndColumn, columns.EndColumns[0]);
        }
    }

    private static void LoadMySqlEmulationMetadata(
        ScaffoldingPipelineContext context
    )
    {
        var triggerGroups = LoadMarkedTriggers(context);

        if (triggerGroups.Count == 0)
        {
            return;
        }

        var candidates = new List<EmulationCandidate>();

        foreach (var (tableName, triggers) in triggerGroups)
        {
            if (!context.TableLookup.TryGetValue(tableName, out var table)
                || table is DatabaseView
                || !TryCreateCandidate(context, table, triggers, out var candidate))
            {
                continue;
            }

            candidates.Add(candidate);
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var tableMetadata = LoadPhysicalTables(context, candidates);
        var columnMetadata = LoadPhysicalColumns(context, candidates);

        foreach (var candidate in candidates)
        {
            if (!HasValidPhysicalContract(context, candidate, tableMetadata, columnMetadata))
            {
                continue;
            }

            ApplyEmulationAnnotations(candidate);
            RemoveHistoryTable(context, candidate.HistoryIdentity);
        }
    }

    private static Dictionary<string, List<TriggerMetadata>> LoadMarkedTriggers(
        ScaffoldingPipelineContext context
    )
    {
        using var command = context.Connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT
                TRIGGER_NAME,
                EVENT_MANIPULATION,
                EVENT_OBJECT_TABLE,
                ACTION_TIMING,
                ACTION_STATEMENT
            FROM information_schema.TRIGGERS
            WHERE TRIGGER_SCHEMA = DATABASE()
            """);

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, context.TableFilter, "EVENT_OBJECT_TABLE");
        sql.Append(" ORDER BY EVENT_OBJECT_TABLE, EVENT_MANIPULATION, TRIGGER_NAME;");
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();
        var triggerGroups = new Dictionary<string, List<TriggerMetadata>>(StringComparer.Ordinal);

        while (reader.Read())
        {
            var actionStatement = reader.GetString(4);

            if (!MySqlTemporalMetadata.TryParseEmulationMarker(actionStatement, out var marker))
            {
                continue;
            }

            var tableName = reader.GetString(2);

            if (!triggerGroups.TryGetValue(tableName, out var triggers))
            {
                triggers = [];
                triggerGroups[tableName] = triggers;
            }

            triggers.Add(
                new TriggerMetadata(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(3),
                    actionStatement,
                    marker));
        }

        return triggerGroups;
    }

    private static bool TryCreateCandidate(
        ScaffoldingPipelineContext context,
        DatabaseTable table,
        IReadOnlyList<TriggerMetadata> triggers,
        [NotNullWhen(true)] out EmulationCandidate? candidate
    )
    {
        candidate = null;

        if (triggers.Count != 3
            || triggers
                .Select(trigger => trigger.Marker)
                .Distinct()
                .Count()
            != 1)
        {
            return false;
        }

        var marker = triggers[0].Marker;
        var eventNames = new[]
        {
            "INSERT",
            "UPDATE",
            "DELETE",
        };

        foreach (var eventName in eventNames)
        {
            var matchingTriggers = triggers
                .Where(trigger => string.Equals(trigger.EventName, eventName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matchingTriggers.Length != 1)
            {
                return false;
            }

            var trigger = matchingTriggers[0];
            var expectedTriggerName = MySqlTemporalMetadata.CreateTriggerName(
                table.Schema,
                table.Name,
                eventName.ToLowerInvariant());

            if (!string.Equals(trigger.Timing, "BEFORE", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(trigger.Name, expectedTriggerName, StringComparison.Ordinal)
                || !HasExpectedTriggerBody(table, marker, eventName, trigger.ActionStatement))
            {
                return false;
            }
        }

        var historyDatabaseName = marker.HistorySchema ?? context.DatabaseName;

        candidate = new EmulationCandidate(
            table,
            marker,
            (context.DatabaseName, table.Name),
            (historyDatabaseName, marker.HistoryTable));

        return true;
    }

    private static bool HasExpectedTriggerBody(
        DatabaseTable table,
        MySqlTemporalEmulationMarker marker,
        string eventName,
        string actionStatement
    )
    {
        var expected = new StringBuilder()
            .Append("BEGIN ")
            .Append("/* ")
            .Append(
                MySqlTemporalMetadata.CreateEmulationMarker(
                    marker.HistorySchema,
                    marker.HistoryTable,
                    marker.PeriodStartColumn,
                    marker.PeriodEndColumn))
            .Append(" */ ");

        if (string.Equals(eventName, "INSERT", StringComparison.OrdinalIgnoreCase))
        {
            AppendSetNewPeriod(expected, marker, "UTC_TIMESTAMP(6)");
        }
        else
        {
            expected
                .Append("DECLARE __doka_temporal_timestamp datetime(6); ")
                .Append("SET __doka_temporal_timestamp = UTC_TIMESTAMP(6); ");
            AppendHistoryInsert(expected, table, marker);

            if (string.Equals(eventName, "UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                AppendSetNewPeriod(expected, marker, "__doka_temporal_timestamp");
            }
        }

        expected.Append("END");

        return string.Equals(
            NormalizeSql(actionStatement),
            NormalizeSql(expected.ToString()),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendHistoryInsert(
        StringBuilder sql,
        DatabaseTable table,
        MySqlTemporalEmulationMarker marker
    )
    {
        var columns = table
            .Columns.Where(column => string.IsNullOrWhiteSpace(column.ComputedColumnSql))
            .Select(column => column.Name)
            .ToArray();

        sql
            .Append("INSERT INTO ")
            .Append(DelimitIdentifier(marker.HistoryTable, marker.HistorySchema))
            .Append(" (")
            .AppendJoin(", ", columns.Select(DelimitIdentifier))
            .Append(") VALUES (");

        for (var index = 0; index < columns.Length; index++)
        {
            if (index > 0)
            {
                sql.Append(", ");
            }

            var columnName = columns[index];

            if (string.Equals(columnName, marker.PeriodEndColumn, StringComparison.Ordinal))
            {
                sql.Append("__doka_temporal_timestamp");
            }
            else
            {
                sql
                    .Append("OLD.")
                    .Append(DelimitIdentifier(columnName));
            }
        }

        sql.Append("); ");
    }

    private static void AppendSetNewPeriod(
        StringBuilder sql,
        MySqlTemporalEmulationMarker marker,
        string timestampExpression
    ) => sql
        .Append("SET NEW.")
        .Append(DelimitIdentifier(marker.PeriodStartColumn))
        .Append(" = ")
        .Append(timestampExpression)
        .Append("; SET NEW.")
        .Append(DelimitIdentifier(marker.PeriodEndColumn))
        .Append(" = '")
        .Append(TemporalInfinity)
        .Append("'; ");

    private static Dictionary<(string DatabaseName, string TableName), PhysicalTableMetadata> LoadPhysicalTables(
        ScaffoldingPipelineContext context,
        IReadOnlyList<EmulationCandidate> candidates
    )
    {
        var identities = GetCandidateIdentities(candidates);

        using var command = context.Connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT
                TABLE_SCHEMA,
                TABLE_NAME,
                ENGINE,
                TABLE_TYPE
            FROM information_schema.TABLES
            WHERE
            """);

        AppendIdentityPredicate(sql, command, identities);
        sql.Append(" ORDER BY TABLE_SCHEMA, TABLE_NAME;");
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();
        var tables = new Dictionary<(string DatabaseName, string TableName), PhysicalTableMetadata>();

        while (reader.Read())
        {
            var identity = (reader.GetString(0), reader.GetString(1));
            tables[identity] = new PhysicalTableMetadata(
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3));
        }

        return tables;
    }

    private static Dictionary<(string DatabaseName, string TableName), List<PhysicalColumnMetadata>>
        LoadPhysicalColumns(
            ScaffoldingPipelineContext context,
            IReadOnlyList<EmulationCandidate> candidates
        )
    {
        var identities = GetCandidateIdentities(candidates);

        using var command = context.Connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT
                TABLE_SCHEMA,
                TABLE_NAME,
                COLUMN_NAME,
                COLUMN_TYPE,
                IS_NULLABLE,
                EXTRA,
                GENERATION_EXPRESSION
            FROM information_schema.COLUMNS
            WHERE
            """);

        AppendIdentityPredicate(sql, command, identities);
        sql.Append(" ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION;");
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();
        var columns = new Dictionary<(string DatabaseName, string TableName), List<PhysicalColumnMetadata>>();

        while (reader.Read())
        {
            var identity = (reader.GetString(0), reader.GetString(1));

            if (!columns.TryGetValue(identity, out var tableColumns))
            {
                tableColumns = [];
                columns[identity] = tableColumns;
            }

            tableColumns.Add(
                new PhysicalColumnMetadata(
                    reader.GetString(2),
                    reader.GetString(3),
                    string.Equals(reader.GetString(4), "YES", StringComparison.OrdinalIgnoreCase),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return columns;
    }

    private static bool HasValidPhysicalContract(
        ScaffoldingPipelineContext context,
        EmulationCandidate candidate,
        IReadOnlyDictionary<(string DatabaseName, string TableName), PhysicalTableMetadata> tables,
        IReadOnlyDictionary<(string DatabaseName, string TableName), List<PhysicalColumnMetadata>> columns
    )
    {
        if (!tables.TryGetValue(candidate.SourceIdentity, out var sourceTable)
            || !tables.TryGetValue(candidate.HistoryIdentity, out var historyTable)
            || !string.Equals(sourceTable.Engine, "InnoDB", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(historyTable.Engine, "InnoDB", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(sourceTable.TableType, "BASE TABLE", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(historyTable.TableType, "BASE TABLE", StringComparison.OrdinalIgnoreCase)
            || !columns.TryGetValue(candidate.SourceIdentity, out var sourceColumns)
            || !columns.TryGetValue(candidate.HistoryIdentity, out var historyColumns)
            || sourceColumns.Count == 0
            || sourceColumns.Count != historyColumns.Count)
        {
            return false;
        }

        for (var index = 0; index < sourceColumns.Count; index++)
        {
            var sourceColumn = sourceColumns[index];
            var historyColumn = historyColumns[index];

            if (!string.Equals(sourceColumn.Name, historyColumn.Name, StringComparison.Ordinal)
                || !string.Equals(sourceColumn.ColumnType, historyColumn.ColumnType, StringComparison.OrdinalIgnoreCase)
                || sourceColumn.IsNullable != historyColumn.IsNullable
                || !string.Equals(
                    sourceColumn.GenerationExpression,
                    historyColumn.GenerationExpression,
                    StringComparison.Ordinal)
                || !HaveEquivalentGenerationKinds(sourceColumn.Extra, historyColumn.Extra))
            {
                return false;
            }
        }

        return IsValidPeriodColumn(sourceColumns, candidate.Marker.PeriodStartColumn)
            && IsValidPeriodColumn(sourceColumns, candidate.Marker.PeriodEndColumn)
            && !context.TemporalHistoryTables.Contains(candidate.SourceIdentity);
    }

    private static bool IsValidPeriodColumn(
        IReadOnlyList<PhysicalColumnMetadata> columns,
        string columnName
    ) =>
        columns.FirstOrDefault(column => string.Equals(column.Name, columnName, StringComparison.Ordinal)) is { } column
        && string.Equals(column.ColumnType, "datetime(6)", StringComparison.OrdinalIgnoreCase)
        && !column.IsNullable
        && string.IsNullOrWhiteSpace(column.GenerationExpression);

    private static bool HaveEquivalentGenerationKinds(
        string? sourceExtra,
        string? historyExtra
    ) => ContainsGenerationKind(sourceExtra, "VIRTUAL GENERATED")
        == ContainsGenerationKind(historyExtra, "VIRTUAL GENERATED")
        && ContainsGenerationKind(sourceExtra, "STORED GENERATED")
        == ContainsGenerationKind(historyExtra, "STORED GENERATED");

    private static bool ContainsGenerationKind(
        string? extra,
        string generationKind
    ) => extra?.Contains(generationKind, StringComparison.OrdinalIgnoreCase) == true;

    private static void ApplyEmulationAnnotations(
        EmulationCandidate candidate
    )
    {
        candidate.Table.SetAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal, true);
        candidate.Table.SetAnnotation(MySqlAnnotationNames.TemporalSourceHistoryTable, candidate.Marker.HistoryTable);
        candidate.Table.SetAnnotation(MySqlAnnotationNames.TemporalSourceHistorySchema, candidate.Marker.HistorySchema);
        candidate.Table.SetAnnotation(
            MySqlAnnotationNames.TemporalSourcePeriodStartColumn,
            candidate.Marker.PeriodStartColumn);
        candidate.Table.SetAnnotation(
            MySqlAnnotationNames.TemporalSourcePeriodEndColumn,
            candidate.Marker.PeriodEndColumn);
    }

    private static void RemoveHistoryTable(
        ScaffoldingPipelineContext context,
        (string DatabaseName, string TableName) historyIdentity
    )
    {
        context.TemporalHistoryTables.Add(historyIdentity);

        if (context.DatabaseTables.Remove(historyIdentity, out var historyTable))
        {
            context.DatabaseModel.Tables.Remove(historyTable);
        }

        if (string.Equals(historyIdentity.DatabaseName, context.DatabaseName, StringComparison.Ordinal))
        {
            context.TableLookup.Remove(historyIdentity.TableName);

            foreach (var key in context
                         .Columns.Keys.Where(key => string.Equals(
                             key.TableName,
                             historyIdentity.TableName,
                             StringComparison.Ordinal))
                         .ToArray())
            {
                context.Columns.Remove(key);
            }
        }

        foreach (var key in context
                     .DatabaseColumns.Keys.Where(key =>
                         string.Equals(key.DatabaseName, historyIdentity.DatabaseName, StringComparison.Ordinal)
                         && string.Equals(key.TableName, historyIdentity.TableName, StringComparison.Ordinal))
                     .ToArray())
        {
            context.DatabaseColumns.Remove(key);
        }
    }

    private static (string DatabaseName, string TableName)[] GetCandidateIdentities(
        IEnumerable<EmulationCandidate> candidates
    ) => candidates
        .SelectMany(candidate => new[]
        {
            candidate.SourceIdentity,
            candidate.HistoryIdentity
        })
        .Distinct()
        .ToArray();

    private static void AppendIdentityPredicate(
        StringBuilder sql,
        DbCommand command,
        (string DatabaseName, string TableName)[] identities
    )
    {
        sql.Append('(');

        for (var index = 0; index < identities.Length; index++)
        {
            if (index > 0)
            {
                sql.Append(" OR ");
            }

            var schemaParameterName = $"@temporal_schema_{index}";
            var tableParameterName = $"@temporal_table_{index}";

            sql
                .Append("(TABLE_SCHEMA = ")
                .Append(schemaParameterName)
                .Append(" AND TABLE_NAME = ")
                .Append(tableParameterName)
                .Append(')');

            AddParameter(command, schemaParameterName, identities[index].DatabaseName);
            AddParameter(command, tableParameterName, identities[index].TableName);
        }

        sql.Append(')');
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        string value
    )
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string DelimitIdentifier(
        string identifier
    ) => "`" + identifier.Replace("`", "``", StringComparison.Ordinal) + "`";

    private static string DelimitIdentifier(
        string identifier,
        string? schema
    ) => string.IsNullOrWhiteSpace(schema)
        ? DelimitIdentifier(identifier)
        : DelimitIdentifier(schema) + "." + DelimitIdentifier(identifier);

    private static string NormalizeSql(
        string sql
    )
    {
        // Collapse formatting whitespace only outside identifiers and string literals. A
        // regular expression would also alter meaningful whitespace or escaped delimiters
        // inside the trigger SQL that this strict reverse-engineering contract compares.
        var normalized = new StringBuilder(sql.Length);
        var inQuotedIdentifier = false;
        var inStringLiteral = false;
        var pendingWhitespace = false;

        for (var index = 0; index < sql.Length; index++)
        {
            var character = sql[index];

            if (!inQuotedIdentifier
                && !inStringLiteral
                && char.IsWhiteSpace(character))
            {
                pendingWhitespace = normalized.Length > 0;
                continue;
            }

            if (pendingWhitespace)
            {
                normalized.Append(' ');
                pendingWhitespace = false;
            }

            normalized.Append(character);

            if (character == '`'
                && !inStringLiteral)
            {
                if (inQuotedIdentifier
                    && index + 1 < sql.Length
                    && sql[index + 1] == '`')
                {
                    normalized.Append(sql[++index]);
                    continue;
                }

                inQuotedIdentifier = !inQuotedIdentifier;
            }
            else if (character == '\''
                     && !inQuotedIdentifier)
            {
                if (inStringLiteral
                    && index + 1 < sql.Length
                    && sql[index + 1] == '\'')
                {
                    normalized.Append(sql[++index]);
                    continue;
                }

                inStringLiteral = !inStringLiteral;
            }
        }

        return normalized
            .ToString()
            .Trim()
            .TrimEnd(';');
    }

    private sealed class NativePeriodColumns
    {
        public List<string> StartColumns { get; } = [];

        public List<string> EndColumns { get; } = [];
    }

    private sealed record TriggerMetadata(
        string Name,
        string EventName,
        string Timing,
        string ActionStatement,
        MySqlTemporalEmulationMarker Marker
    );

    private sealed record EmulationCandidate(
        DatabaseTable Table,
        MySqlTemporalEmulationMarker Marker,
        (string DatabaseName, string TableName) SourceIdentity,
        (string DatabaseName, string TableName) HistoryIdentity
    );

    private sealed record PhysicalTableMetadata(
        string? Engine,
        string TableType
    );

    private sealed record PhysicalColumnMetadata(
        string Name,
        string ColumnType,
        bool IsNullable,
        string? Extra,
        string? GenerationExpression
    );
}
