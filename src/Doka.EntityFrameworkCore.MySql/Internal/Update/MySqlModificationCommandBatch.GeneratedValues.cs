namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlModificationCommandBatch
{
    /// <inheritdoc />
    protected override int ConsumeResultSet(
        int startCommandIndex,
        RelationalDataReader reader
    )
    {
        if (!ResultSetMappings[startCommandIndex].HasFlag(ResultSetMapping.IsPositionalResultMappingEnabled))
        {
            return base.ConsumeResultSet(startCommandIndex, reader);
        }

        try
        {
            var readback = CreateGeneratedValueReadback(startCommandIndex, reader.DbDataReader);
            for (var index = 0; index < readback.CommandCount; index++)
            {
                if (!reader.Read())
                {
                    throw InvalidGeneratedValueReadback("A generated-value row is missing.");
                }

                PropagateGeneratedValue(startCommandIndex, reader, ref readback);
            }

            // WHY: Checking EOF rejects duplicate trailing rows instead of advancing
            // silently to the next command group.
            if (reader.Read())
            {
                throw InvalidGeneratedValueReadback("The result contains an unexpected generated-value row.");
            }

            return readback.LastCommandIndex;
        }
        catch (Exception exception) when (exception is not DbUpdateException and not OperationCanceledException)
        {
            throw GeneratedValueReadbackException(startCommandIndex, exception);
        }
    }

    /// <inheritdoc />
    protected override Task<int> ConsumeResultSetAsync(
        int startCommandIndex,
        RelationalDataReader reader,
        CancellationToken cancellationToken
    ) => ResultSetMappings[startCommandIndex].HasFlag(ResultSetMapping.IsPositionalResultMappingEnabled)
        ? ConsumeGeneratedValueResultSetAsync(startCommandIndex, reader, cancellationToken)
        : base.ConsumeResultSetAsync(startCommandIndex, reader, cancellationToken);

    /// <summary>
    /// Validates positional output asynchronously while leaving ordinary results on EF's original task path.
    /// </summary>
    private async Task<int> ConsumeGeneratedValueResultSetAsync(
        int startCommandIndex,
        RelationalDataReader reader,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var readback = CreateGeneratedValueReadback(startCommandIndex, reader.DbDataReader);
            for (var index = 0; index < readback.CommandCount; index++)
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw InvalidGeneratedValueReadback("A generated-value row is missing.");
                }

                PropagateGeneratedValue(startCommandIndex, reader, ref readback);
            }

            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw InvalidGeneratedValueReadback("The result contains an unexpected generated-value row.");
            }

            return readback.LastCommandIndex;
        }
        catch (Exception exception) when (exception is not DbUpdateException and not OperationCanceledException)
        {
            throw GeneratedValueReadbackException(startCommandIndex, exception);
        }
    }

    /// <summary>
    /// Bounds one positional result group and validates its schema before any output reaches an entry.
    /// </summary>
    private GeneratedValueReadback CreateGeneratedValueReadback(
        int startCommandIndex,
        DbDataReader reader
    )
    {
        var lastCommandIndex = startCommandIndex;
        while (ResultSetMappings[lastCommandIndex].HasFlag(ResultSetMapping.NotLastInResultSet))
        {
            lastCommandIndex++;
            if (lastCommandIndex >= ResultSetMappings.Count
                || !ResultSetMappings[lastCommandIndex].HasFlag(ResultSetMapping.IsPositionalResultMappingEnabled))
            {
                throw InvalidGeneratedValueReadback("The positional command group is incomplete.");
            }
        }

        var commandCount = lastCommandIndex - startCommandIndex + 1;
        if (commandCount > DefaultMaxBatchSize)
        {
            throw InvalidGeneratedValueReadback("The positional command group exceeds the batch limit.");
        }

        var readColumnCount = 0;
        foreach (var modification in ModificationCommands[startCommandIndex].ColumnModifications)
        {
            if (modification.IsRead)
            {
                readColumnCount++;
            }
        }

        if (readColumnCount == 0
            || reader.FieldCount != readColumnCount + 1)
        {
            throw InvalidGeneratedValueReadback("The generated-value column count does not match the command.");
        }

        var ordinalType = reader.GetFieldType(readColumnCount);
        if (ordinalType != typeof(int)
            && ordinalType != typeof(long))
        {
            throw InvalidGeneratedValueReadback("The generated-value slot must be a signed integral column.");
        }

        return new GeneratedValueReadback(lastCommandIndex, commandCount, readColumnCount, ordinalType == typeof(long));
    }

    /// <summary>
    /// Validates one relative slot before using EF's existing converter-aware propagation path.
    /// </summary>
    private void PropagateGeneratedValue(
        int startCommandIndex,
        RelationalDataReader reader,
        ref GeneratedValueReadback readback
    )
    {
        var storeReader = reader.DbDataReader;
        if (storeReader.IsDBNull(readback.OrdinalColumn))
        {
            throw InvalidGeneratedValueReadback("The generated-value slot is null.");
        }

        // WHY: MySQL exposes literal ordinals as Int64. Typed reads avoid boxing and string or fractional coercion.
        var ordinal = readback.HasInt64Ordinal
            ? storeReader.GetInt64(readback.OrdinalColumn)
            : storeReader.GetInt32(readback.OrdinalColumn);

        if (ordinal < 0
            || ordinal >= readback.CommandCount)
        {
            throw InvalidGeneratedValueReadback("The generated-value slot is outside its command group.");
        }

        if (!readback.MarkSeen((int)ordinal))
        {
            throw InvalidGeneratedValueReadback("The generated-value slot occurs more than once.");
        }

        ModificationCommands[startCommandIndex + (int)ordinal].PropagateResults(reader);
    }

    /// <summary>
    /// Associates a rejected result with every affected entry in its group, without allocating on successful reads.
    /// </summary>
    private DbUpdateException GeneratedValueReadbackException(
        int startCommandIndex,
        Exception exception
    )
    {
        var entries = new List<IUpdateEntry>();
        var seenEntries = new HashSet<IUpdateEntry>(ReferenceEqualityComparer.Instance);
        var commandIndex = startCommandIndex;
        do
        {
            foreach (var entry in ModificationCommands[commandIndex].Entries)
            {
                if (seenEntries.Add(entry))
                {
                    entries.Add(entry);
                }
            }
        } while (ResultSetMappings[commandIndex++].HasFlag(ResultSetMapping.NotLastInResultSet)
                 && commandIndex < ResultSetMappings.Count);

        return new DbUpdateException(RelationalStrings.UpdateStoreException, exception, entries);
    }

    /// <summary>
    /// Creates a non-suppressible contract error rather than a suppressible optimistic-concurrency error.
    /// </summary>
    private static InvalidOperationException InvalidGeneratedValueReadback(
        string detail
    ) => new("Invalid generated-value readback. " + detail);

    /// <summary>
    /// Tracks at most 1,000 result slots; the usual group of at most 64 rows needs no bitmap allocation.
    /// </summary>
    private struct GeneratedValueReadback
    {
        private readonly ulong[]? _additionalSeenWords;
        private ulong _firstSeenWord;

        public GeneratedValueReadback(
            int lastCommandIndex,
            int commandCount,
            int ordinalColumn,
            bool hasInt64Ordinal
        )
        {
            LastCommandIndex = lastCommandIndex;
            CommandCount = commandCount;
            OrdinalColumn = ordinalColumn;
            HasInt64Ordinal = hasInt64Ordinal;
            _firstSeenWord = 0;
            _additionalSeenWords = commandCount > 64 ? new ulong[(commandCount - 1) / 64] : null;
        }

        public int LastCommandIndex { get; }

        public int CommandCount { get; }

        public int OrdinalColumn { get; }

        public bool HasInt64Ordinal { get; }

        /// <summary>
        /// Records a validated slot and reports whether it is new.
        /// </summary>
        public bool MarkSeen(
            int slot
        )
        {
            var bit = 1UL << (slot & 63);
            ref var seenWord = ref (slot < 64 ? ref _firstSeenWord : ref _additionalSeenWords![(slot / 64) - 1]);
            if ((seenWord & bit) != 0)
            {
                return false;
            }

            seenWord |= bit;

            return true;
        }
    }
}
