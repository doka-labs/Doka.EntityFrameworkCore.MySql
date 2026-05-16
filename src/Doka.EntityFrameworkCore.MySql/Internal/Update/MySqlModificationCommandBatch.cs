namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// MySQL modification-command batch with multi-row INSERT consolidation. Consecutive
/// INSERTs that target the same table + write column set + read column set are buffered
/// and flushed as a single multi-row INSERT statement (or a multi-row INSERT ... RETURNING
/// statement on MariaDB 10.5+). Non-INSERT commands and INSERT-shape-changes trigger an
/// immediate flush of the buffered set; <see cref="Complete"/> flushes any remaining
/// buffered commands before completing the batch.
///
/// Two server-side safety caps run on every <see cref="TryAddCommand"/> call so the
/// emitted statement stays within MySQL/MariaDB hard limits even when the user
/// configures <c>MaxBatchSize</c> above the conservative default: the prepared-statement
/// placeholder count cap (65535 placeholders per statement) and a conservative
/// <c>max_allowed_packet</c> wire-size estimate (4 MB). When either cap would be
/// crossed, the batch closes early and the consumer opens a fresh batch for the next
/// command. The cap event is logged once per batch under
/// <see cref="MySqlEventId.BulkInsertParameterCountCapped"/> /
/// <see cref="MySqlEventId.BulkInsertPacketSizeCapped"/>.
/// </summary>
internal sealed class MySqlModificationCommandBatch : AffectedCountModificationCommandBatch
{
    private const int DefaultMaxBatchSize = 1000;

    /// <summary>
    /// MySQL/MariaDB hard limit: a prepared statement may bind at most 65535
    /// placeholders. Exceeding this raises a server-side "Prepared statement contains
    /// too many placeholders" error; the cap stops short of the limit so the batch
    /// closes deterministically instead of failing on submit.
    /// </summary>
    private const int MaxParameterCount = 65535;

    /// <summary>
    /// Conservative budget against <c>max_allowed_packet</c>. The MySQL / MariaDB
    /// default is 64 MB, but production deployments routinely lower it to 16 MB or
    /// 4 MB; 4 MB stays under the smallest commonly seen value. The per-parameter
    /// byte estimate below is intentionally generous so the cap fires well before
    /// the actual packet size would reach a tighter server configuration.
    /// </summary>
    private const int MaxPacketSizeBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Upper-bound wire-size estimate per parameter (placeholder envelope + bound
    /// value). Real values vary widely with column type; this is the heuristic
    /// figure that keeps the cap simple without per-type introspection. Values
    /// above the threshold (large BLOB / TEXT columns) can still fail server-side
    /// on submit; the cap is a guard rail, not a guarantee.
    /// </summary>
    private const int EstimatedBytesPerParameter = 256;

    private readonly List<IReadOnlyModificationCommand> _pendingBulkInsertCommands = new();
    private readonly ILogger _logger;
    private int _currentParameterCount;
    private bool _parameterCountWarningEmitted;
    private bool _packetSizeWarningEmitted;

    public MySqlModificationCommandBatch(
        ModificationCommandBatchFactoryDependencies dependencies,
        int maxBatchSize
    ) : base(dependencies, Math.Min(maxBatchSize, DefaultMaxBatchSize))
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        _logger = dependencies.UpdateLogger.Logger;
    }

    private new MySqlUpdateSqlGenerator UpdateSqlGenerator => (MySqlUpdateSqlGenerator)base.UpdateSqlGenerator;

    public override bool TryAddCommand(
        IReadOnlyModificationCommand modificationCommand
    )
    {
        ArgumentNullException.ThrowIfNull(modificationCommand);

        if (ModificationCommands.Count >= MaxBatchSize)
        {
            return false;
        }

        var commandParameterCount = CountCommandParameters(modificationCommand);
        var projectedParameterCount = _currentParameterCount + commandParameterCount;
        var projectedPacketSize = (long)projectedParameterCount * EstimatedBytesPerParameter;

        // First command is always accepted even when it alone exceeds a cap: the
        // server raises a clear error on submit rather than the batch silently
        // dropping every command. The caps fire on the SECOND-and-later additions
        // that would push the batch over the limit.
        if (ModificationCommands.Count > 0)
        {
            if (projectedParameterCount > MaxParameterCount)
            {
                if (!_parameterCountWarningEmitted)
                {
                    MySqlLoggerMessages.BulkInsertParameterCountCapped(
                        _logger,
                        ModificationCommands.Count,
                        projectedParameterCount,
                        MaxParameterCount);
                    _parameterCountWarningEmitted = true;
                }

                return false;
            }

            if (projectedPacketSize > MaxPacketSizeBytes)
            {
                if (!_packetSizeWarningEmitted)
                {
                    MySqlLoggerMessages.BulkInsertPacketSizeCapped(
                        _logger,
                        ModificationCommands.Count,
                        (int)Math.Min(int.MaxValue, projectedPacketSize),
                        MaxPacketSizeBytes);
                    _packetSizeWarningEmitted = true;
                }

                return false;
            }
        }

        var added = base.TryAddCommand(modificationCommand);
        if (added)
        {
            _currentParameterCount = projectedParameterCount;
        }

        return added;
    }

    protected override void AddCommand(
        IReadOnlyModificationCommand modificationCommand
    )
    {
        ArgumentNullException.ThrowIfNull(modificationCommand);

        if (modificationCommand is { EntityState: EntityState.Added, StoreStoredProcedure: null })
        {
            if (_pendingBulkInsertCommands.Count > 0
                && !CanBeInsertedInSameStatement(_pendingBulkInsertCommands[0], modificationCommand))
            {
                ApplyPendingBulkInsertCommands();
                _pendingBulkInsertCommands.Clear();
            }

            _pendingBulkInsertCommands.Add(modificationCommand);
            AddParameters(modificationCommand);
        }
        else
        {
            if (_pendingBulkInsertCommands.Count > 0)
            {
                ApplyPendingBulkInsertCommands();
                _pendingBulkInsertCommands.Clear();
            }

            base.AddCommand(modificationCommand);
        }
    }

    public override void Complete(
        bool moreBatchesExpected
    )
    {
        if (_pendingBulkInsertCommands.Count > 0)
        {
            ApplyPendingBulkInsertCommands();
            _pendingBulkInsertCommands.Clear();
        }

        base.Complete(moreBatchesExpected);
    }

    /// <summary>
    /// Two INSERTs can land in the same multi-row VALUES statement only when they target
    /// the same table, the same schema, the same write-column list (in the same order),
    /// and the same read-column list. A shape mismatch forces a flush of the pending
    /// buffer before the new command joins a fresh buffer.
    /// </summary>
    private static bool CanBeInsertedInSameStatement(
        IReadOnlyModificationCommand firstCommand,
        IReadOnlyModificationCommand secondCommand
    ) => firstCommand.TableName == secondCommand.TableName
        && firstCommand.Schema == secondCommand.Schema
        && firstCommand
            .ColumnModifications.Where(o => o.IsWrite)
            .Select(o => o.ColumnName)
            .SequenceEqual(
                secondCommand
                    .ColumnModifications.Where(o => o.IsWrite)
                    .Select(o => o.ColumnName))
        && firstCommand
            .ColumnModifications.Where(o => o.IsRead)
            .Select(o => o.ColumnName)
            .SequenceEqual(
                secondCommand
                    .ColumnModifications.Where(o => o.IsRead)
                    .Select(o => o.ColumnName));

    private static int CountCommandParameters(
        IReadOnlyModificationCommand modificationCommand
    )
    {
        var count = 0;
        var modifications = modificationCommand.ColumnModifications;
        for (var index = 0; index < modifications.Count; index++)
        {
            var modification = modifications[index];
            if (modification.UseCurrentValueParameter)
            {
                count++;
            }

            if (modification.UseOriginalValueParameter)
            {
                count++;
            }
        }

        return count;
    }

    private void ApplyPendingBulkInsertCommands()
    {
        if (_pendingBulkInsertCommands.Count == 0)
        {
            return;
        }

        var commandPosition = ResultSetMappings.Count;
        var wasCachedCommandTextEmpty = IsCommandTextEmpty;

        var resultSetMapping = UpdateSqlGenerator.AppendBulkInsertOperation(
            SqlBuilder,
            _pendingBulkInsertCommands,
            commandPosition,
            out var requiresTransaction);

        SetRequiresTransaction(!wasCachedCommandTextEmpty || requiresTransaction);

        for (var index = 0; index < _pendingBulkInsertCommands.Count; index++)
        {
            ResultSetMappings.Add(resultSetMapping);
        }

        // ResultSetMapping is a [Flags] enum. Bit-flag the last entry so a multi-row result
        // set (returned as NotLastInResultSet for each row by AppendBulkInsertReturningOperation)
        // ends correctly on the final command. Cases that return NoResults skip the flip
        // because HasResultRow is not set; per-row fallback already returns LastInResultSet
        // and the flip leaves it untouched.
        if (resultSetMapping.HasFlag(ResultSetMapping.HasResultRow))
        {
            ResultSetMappings[^1] &= ~ResultSetMapping.NotLastInResultSet;
            ResultSetMappings[^1] |= ResultSetMapping.LastInResultSet;
        }
    }
}
