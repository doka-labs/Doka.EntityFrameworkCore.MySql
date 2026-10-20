namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlUpdateSqlGenerator
{
    // WHY: A finalized table's trigger, engine and key contract does not change between
    // commands. Weak keys avoid retaining contexts/models through this singleton service.
    private static readonly ConditionalWeakTable<ITable, KeyedReadbackTable> s_keyedReadbackTables = new();

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

        foreach (var modification in command.ColumnModifications)
        {
            if (modification.IsRead)
            {
                sqlCharacters += 6 + (2L * modification.ColumnName.Length);
            }
        }

        foreach (var column in table.KeyColumns)
        {
            var key = FindStableKey(command, column)!;
            sqlCharacters += 16 + (2L * column.Name.Length);

            if (key.UseCurrentValueParameter)
            {
                // EF generates bounded pN names; using the actual name also covers provider test commands.
                sqlCharacters += key.ParameterName!.Length + 1;
            }

            var value = key.TypeMapping?.Converter?.ConvertToProvider(key.Value) ?? key.Value;
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

        foreach (var command in commands)
        {
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

        foreach (var column in candidate.KeyColumns)
        {
            if (FindStableKey(command, column) is null)
            {
                return false;
            }
        }

        foreach (var modification in command.ColumnModifications)
        {
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

        foreach (var modification in command.ColumnModifications)
        {
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
        var value = modification.TypeMapping?.Converter?.ConvertToProvider(modification.Value) ?? modification.Value;

        return value is (sbyte)0 or (byte)0 or (short)0 or (ushort)0 or 0 or 0U or 0L or 0UL;
    }

    private static bool HasMatchingOperationShape(
        IReadOnlyList<IColumnModification> expected,
        IReadOnlyList<IColumnModification> actual,
        OperationKind kind
    )
    {
        var actualIndex = 0;

        foreach (var expectedOperation in expected)
        {
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

        // WHY: ROW_COUNT() must refer to the preceding INSERT for every row.
        // One compound SELECT keeps all UNION branches under the same affected-row guard.
        for (var ordinal = 0; ordinal < commands.Count; ordinal++)
        {
            if (ordinal > 0)
            {
                builder.AppendLine("UNION ALL");
            }

            builder.Append("SELECT ");

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

            foreach (var column in commands[ordinal].Table!.PrimaryKey!.Columns)
            {
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
