namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlUpdateSqlGenerator
{
    // WHY: A finalized table's trigger, engine and key contract does not change between
    // commands. Weak keys avoid retaining contexts/models through this singleton service.
    private static readonly ConditionalWeakTable<ITable, KeyedReadbackTable> s_keyedReadbackTables = [];

    /// <summary>Rejects unknown keys that the non-RETURNING insert protocol cannot correlate safely.</summary>
    private static void ValidateGeneratedKeyReadback(
        IReadOnlyModificationCommand command
    )
    {
        var modifications = command.ColumnModifications;
        var modificationCount = modifications.Count;

        // WHY: Index EF's stable column list to avoid an interface enumerator for each validation.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var modificationIndex = 0; modificationIndex < modificationCount; modificationIndex++)
        {
            var modification = modifications[modificationIndex];

            // LAST_INSERT_ID() retrieves AUTO_INCREMENT values, not arbitrary defaults.
            // Omitting an unknown key from EF's fallback predicate could select an unrelated row.
            if (modification is { IsKey: true, IsRead: true, IsWrite: false, }
                && !IsIdentityColumn(modification)
                && !TryGetLiteralGeneratedKeyValue(modification, out _))
            {
                throw new InvalidOperationException(
                    $"The configured server cannot retrieve the database-generated key "
                    + $"'{command.TableName}.{modification.ColumnName}' without INSERT ... RETURNING. "
                    + "Assign the key explicitly or configure client-side value generation. "
                    + "LAST_INSERT_ID() only retrieves AUTO_INCREMENT values.");
            }
        }
    }

    /// <summary>Recognizes an omitted key whose configured literal default is known before the insert.</summary>
    private static bool TryGetLiteralGeneratedKeyValue(
        IColumnModification modification,
        [NotNullWhen(true)] out object? value
    )
    {
        value = null;

        if (modification is not { IsKey: true, IsRead: true, IsWrite: false, Property: { } property, })
        {
            return false;
        }

        // A literal is a known key component; arbitrary SQL and computed expressions
        // are not. Never infer their result from a temporary tracking value.
        if (modification.Column is IColumn column)
        {
            // A shared column can inherit its default from another mapped property.
            var table = StoreObjectIdentifier.Table(column.Table.Name, column.Table.Schema);

            return property.GetDefaultValueSql(table) is null
                && property.GetComputedColumnSql(table) is null
                && property.TryGetDefaultValue(table, out value)
                && value is not null and not DBNull;
        }

        return property.GetDefaultValueSql() is null
            && property.GetComputedColumnSql() is null
            && property.TryGetDefaultValue(out value)
            && value is not null and not DBNull;
    }

    /// <summary>
    /// Accounts for the extra SQL and repeated key values without allocating a key array per row.
    /// </summary>
    internal bool TryGetKeyedReadbackCost(
        IReadOnlyModificationCommand command,
        out long additionalPacketBytes
    )
    {
        additionalPacketBytes = 0;

        if (SupportsReturningClause()
            || !TryGetKeyedReadbackTable(command, out var table))
        {
            return false;
        }

        // WHY: Identifiers can contain escaped delimiters and non-ASCII characters.
        // Four bytes per estimated character bounds UTF-8 SQL independently of their spelling.
        var sqlCharacters = 96 + (2L * command.TableName.Length);

        var modifications = command.ColumnModifications;
        var modificationCount = modifications.Count;

        // WHY: Costing repeats per command; indexing the stable column list avoids interface enumerators.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var modificationIndex = 0; modificationIndex < modificationCount; modificationIndex++)
        {
            var modification = modifications[modificationIndex];

            if (modification.IsRead)
            {
                sqlCharacters += 6 + (2L * modification.ColumnName.Length);
            }
        }

        var keyColumns = table.KeyColumns;
        var columnCount = keyColumns.Count;

        // WHY: Index the stable physical-key list without allocating an enumerator during costing.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            var column = keyColumns[columnIndex];

            var key = FindStableKey(command, column)!;
            sqlCharacters += 16 + (2L * column.Name.Length);

            if (key.UseCurrentValueParameter)
            {
                // EF generates bounded pN names; using the actual name also covers provider test commands.
                sqlCharacters += key.ParameterName!.Length + 1;
            }

            var value = GetProviderKeyValue(key);
            var valueBytes = value switch
            {
                string text => (2L * Encoding.UTF8.GetByteCount(text)) + 2,
                byte[] bytes => (2L * bytes.Length) + 3,
                ReadOnlyMemory<byte> bytes => (2L * bytes.Length) + 3,
                Memory<byte> bytes => (2L * bytes.Length) + 3,
                _ => 256L,
            };

            additionalPacketBytes += valueBytes;
        }

        additionalPacketBytes += 4 * sqlCharacters;

        return true;
    }

    private static bool CanUseKeyedReadback(
        IReadOnlyList<IReadOnlyModificationCommand> commands
    )
    {
        var first = commands[0];

        var commandCount = commands.Count;

        // WHY: Index the stable command list to avoid an interface enumerator during batch validation.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var commandIndex = 0; commandIndex < commandCount; commandIndex++)
        {
            var command = commands[commandIndex];

            if (!ReferenceEquals(command.Table, first.Table)
                || !TryGetKeyedReadbackTable(command, out _)
                || !HasMatchingOperationShape(
                    first.ColumnModifications,
                    command.ColumnModifications,
                    OperationKind.Write)
                || !HasMatchingOperationShape(
                    first.ColumnModifications,
                    command.ColumnModifications,
                    OperationKind.Read))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetKeyedReadbackTable(
        IReadOnlyModificationCommand command,
        [NotNullWhen(true)] out KeyedReadbackTable? table
    )
    {
        table = null;

        if (command is not { EntityState: EntityState.Added, Table: { } physicalTable, StoreStoredProcedure: null, }
            || command.TableName != physicalTable.Name
            || command.Schema != physicalTable.Schema
            || !HasReadOperations(command.ColumnModifications))
        {
            return false;
        }

        var candidate = s_keyedReadbackTables.GetValue(
            physicalTable,
            static current => CreateKeyedReadbackTable(current));

        if (!candidate.Eligible)
        {
            return false;
        }

        var keyColumns = candidate.KeyColumns;
        var columnCount = keyColumns.Count;

        // WHY: Eligibility is checked per command; index the stable key list without enumeration overhead.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            var column = keyColumns[columnIndex];

            if (FindStableKey(command, column) is null)
            {
                return false;
            }
        }

        var modifications = command.ColumnModifications;
        var modificationCount = modifications.Count;

        // WHY: Index EF's stable column list to validate physical mappings without an interface enumerator.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var modificationIndex = 0; modificationIndex < modificationCount; modificationIndex++)
        {
            var modification = modifications[modificationIndex];

            if ((modification.IsRead || modification.IsWrite)
                && (modification.Column is not IColumn column || !ReferenceEquals(column.Table, physicalTable)))
            {
                return false;
            }
        }

        table = candidate;

        return true;
    }

    private static KeyedReadbackTable CreateKeyedReadbackTable(
        ITable table
    )
    {
        var primaryKey = table.PrimaryKey;
        var eligible = primaryKey is { Columns.Count: > 0, }
            && !table.Triggers.Any()
            && MySqlTemporalMetadata.FindTableMetadata(table) is null
            && HasTransactionalEngine(table.FindAnnotation(MySqlAnnotationNames.StorageEngine)?.Value);

        foreach (var mapping in table.EntityTypeMappings)
        {
            eligible &= HasTransactionalEngine(mapping.TypeBase.FindAnnotation(MySqlAnnotationNames.StorageEngine)?.Value);
        }

        // WHY: A trigger can permute an otherwise complete set of known keys. Neither
        // row count nor ordinal validation can recover that identity; declared triggers fall back.
        return new KeyedReadbackTable(eligible, primaryKey?.Columns ?? Array.Empty<IColumn>());
    }

    private static bool HasTransactionalEngine(
        object? annotation
    ) => annotation is null
        || (annotation is string engine && string.Equals(engine, "InnoDB", StringComparison.OrdinalIgnoreCase));

    private static IColumnModification? FindStableKey(
        IReadOnlyModificationCommand command,
        IColumn column
    )
    {
        IColumnModification? key = null;

        var modifications = command.ColumnModifications;
        var modificationCount = modifications.Count;

        // WHY: Key lookup repeats within costing and generation; index the stable list without enumerators.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var modificationIndex = 0; modificationIndex < modificationCount; modificationIndex++)
        {
            var modification = modifications[modificationIndex];

            if (!ReferenceEquals(modification.Column, column))
            {
                continue;
            }

            if (key is not null
                || !modification.IsWrite
                || modification.IsRead
                || modification.Property is null
                || modification.Value is null or DBNull
                || (modification.Entry is { } entry && entry.HasTemporaryValue(modification.Property))
                || IsImplicitIdentityValue(column, modification))
            {
                return null;
            }

            key = modification;
        }

        return key;
    }

    /// <summary>Respects EF's model-value and provider-literal descriptor boundary.</summary>
    private static object? GetProviderKeyValue(
        IColumnModification modification
    )
    {
        var value = modification.Value;

        // WHY: Parameterized descriptors carry model values, while EF's literal
        // SQL generation already requires provider values. Converting those twice
        // can throw or change the physical key used for readback.
        return modification.UseCurrentValueParameter
            ? modification.TypeMapping?.Converter?.ConvertToProvider(value) ?? value
            : value;
    }

    private static bool IsImplicitIdentityValue(
        IColumn column,
        IColumnModification modification
    )
    {
        if (column.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)?.Value is not MySqlValueGenerationStrategy.AutoIncrement)
        {
            return false;
        }

        // WHY: A custom EF sentinel can make zero a written, non-temporary value.
        // MySQL still generates AUTO_INCREMENT for zero unless the session opts out.
        var value = GetProviderKeyValue(modification);

        return value is (sbyte)0 or (byte)0 or (short)0 or (ushort)0 or 0 or 0U or 0L or 0UL;
    }

    private static bool HasMatchingOperationShape(
        IReadOnlyList<IColumnModification> expected,
        IReadOnlyList<IColumnModification> actual,
        OperationKind kind
    )
    {
        var actualIndex = 0;

        var expectedOperationCount = expected.Count;

        // WHY: Compare ordered descriptors without allocating an enumerator for each shape check.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var expectedOperationIndex = 0; expectedOperationIndex < expectedOperationCount; expectedOperationIndex++)
        {
            var expectedOperation = expected[expectedOperationIndex];

            if (!IsOperation(expectedOperation, kind))
            {
                continue;
            }

            while (actualIndex < actual.Count
                   && !IsOperation(actual[actualIndex], kind))
            {
                actualIndex++;
            }

            if (actualIndex == actual.Count
                || !ReferenceEquals(expectedOperation.Column, actual[actualIndex].Column))
            {
                return false;
            }

            actualIndex++;
        }

        // WHY: Resume at the comparison cursor to check only unmatched descriptors without a second enumeration.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (; actualIndex < actual.Count; actualIndex++)
        {
            if (IsOperation(actual[actualIndex], kind))
            {
                return false;
            }
        }

        return true;
    }

    private ResultSetMapping AppendBulkInsertKeyedReadbackOperation(
        StringBuilder builder,
        IReadOnlyList<IReadOnlyModificationCommand> commands,
        IReadOnlyList<IColumnModification> writeOperations,
        IReadOnlyList<IColumnModification> readOperations,
        IColumnModification[]? reusableWriteOperations,
        out bool requiresTransaction
    )
    {
        AppendInsertMultipleRowsInSingleStatementOperation(
            builder,
            commands,
            writeOperations,
            reusableWriteOperations,
            out _);

        // One compound SELECT keeps ROW_COUNT() tied to the preceding INSERT across all UNION branches.
        // WHY: Emit each command's correlation ordinal directly without a separate counter or iterator pipeline.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var ordinal = 0; ordinal < commands.Count; ordinal++)
        {
            if (ordinal > 0)
            {
                builder.AppendLine("UNION ALL");
            }

            builder.Append("SELECT ");

            // WHY: The read-column index controls separators while preserving the positional result schema.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var column = 0; column < readOperations.Count; column++)
            {
                if (column > 0)
                {
                    builder.Append(", ");
                }

                SqlGenerationHelper.DelimitIdentifier(builder, readOperations[column].ColumnName);
            }

            builder
                .Append(", ")
                .Append(ordinal.ToString(CultureInfo.InvariantCulture))
                .AppendLine()
                .Append("FROM ");
            SqlGenerationHelper.DelimitIdentifier(builder, commands[ordinal].TableName, commands[ordinal].Schema);
            builder
                .AppendLine()
                .Append("WHERE ");
            AppendRowsAffectedWhereCondition(builder, commands.Count);

            var keyColumns = commands[ordinal].Table!.PrimaryKey!.Columns;
            var columnCount = keyColumns.Count;

            // WHY: Index stable physical keys without allocating an enumerator for each generated row.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var column = keyColumns[columnIndex];

                builder.Append(" AND ");
                AppendWhereCondition(builder, FindStableKey(commands[ordinal], column)!, useOriginalValue: false);
            }

            if (ordinal + 1 < commands.Count)
            {
                builder.AppendLine();
            }
        }

        builder.AppendLine(SqlGenerationHelper.StatementTerminator);
        requiresTransaction = true;

        // WHY: Physical key predicates preserve database collation/converter semantics;
        // the explicit relative ordinal correlates rows even when the server changes their order.
        return ResultSetMapping.NotLastInResultSet | ResultSetMapping.IsPositionalResultMappingEnabled;
    }

    private sealed record KeyedReadbackTable(
        bool Eligible,
        IReadOnlyList<IColumn> KeyColumns
    );
}
