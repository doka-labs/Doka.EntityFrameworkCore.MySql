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
    private int _pendingProviderParameters;
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

        _pendingProviderParameters = 0;

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

    protected override void RollbackLastCommand(
        IReadOnlyModificationCommand modificationCommand
    )
    {
        if (_pendingBulkInsertCommands.Count > 0)
        {
            _pendingBulkInsertCommands.RemoveAt(_pendingBulkInsertCommands.Count - 1);
        }

        for (var index = 0; index < _pendingProviderParameters; index++)
        {
            var parameterIndex = RelationalCommandBuilder.Parameters.Count - 1;
            var parameter = RelationalCommandBuilder.Parameters[parameterIndex];

            RelationalCommandBuilder.RemoveParameterAt(parameterIndex);
            ParameterValues.Remove(parameter.InvariantName);
        }

        base.RollbackLastCommand(modificationCommand);
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

    protected override void AddParameter(
        IColumnModification columnModification
    )
    {
        if (columnModification.Column is not IStoreStoredProcedureParameter storedProcedureParameter
            || !storedProcedureParameter.Direction.HasFlag(ParameterDirection.Output))
        {
            base.AddParameter(columnModification);
            return;
        }

        // CommandType.Text rejects Output and InputOutput DbParameters in
        // MySqlConnector. OUT values are carried by server session variables;
        // INOUT keeps only its input half as a regular command parameter.
        if (storedProcedureParameter.Direction == ParameterDirection.Output)
        {
            return;
        }

        var useOriginalValue = columnModification.UseOriginalValueParameter;
        var parameterName = useOriginalValue
            ? columnModification.OriginalParameterName!
            : columnModification.ParameterName!;

        var value = useOriginalValue ? columnModification.OriginalValue : columnModification.Value;

        if (value is null)
        {
            // With AllowUserVariables enabled, an unbound @p variable evaluates
            // to NULL and preserves INOUT's input semantics without registering
            // an unsupported InputOutput DbParameter.
            return;
        }

        RelationalCommandBuilder.AddParameter(
            parameterName,
            Dependencies.SqlGenerationHelper.GenerateParameterName(parameterName),
            columnModification.TypeMapping!,
            columnModification.IsNullable,
            ParameterDirection.Input);
        ParameterValues.Add(parameterName, value);
        _pendingProviderParameters++;
    }

    protected override void Consume(
        RelationalDataReader reader
    )
    {
        ArgumentNullException.ThrowIfNull(reader);

        var commandIndex = 0;

        try
        {
            bool? onResultSet = null;
            while (commandIndex < ResultSetMappings.Count)
            {
                var command = ModificationCommands[commandIndex];
                if (command.StoreStoredProcedure is not null)
                {
                    ConsumeStoredProcedure(commandIndex, command, reader, ref onResultSet);
                    commandIndex++;
                    continue;
                }

                var resultSetMapping = ResultSetMappings[commandIndex];
                if (resultSetMapping.HasFlag(ResultSetMapping.HasResultRow))
                {
                    if (onResultSet == false)
                    {
                        throw new InvalidOperationException(RelationalStrings.MissingResultSetWhenSaving);
                    }

                    var lastHandledCommandIndex =
                        resultSetMapping.HasFlag(ResultSetMapping.ResultSetWithRowsAffectedOnly)
                            ? ConsumeResultSetWithRowsAffectedOnly(commandIndex, reader)
                            : ConsumeResultSet(commandIndex, reader);

                    commandIndex = lastHandledCommandIndex + 1;
                    onResultSet = reader.DbDataReader.NextResult();
                }
                else
                {
                    commandIndex++;
                }
            }

            if (onResultSet == true)
            {
                Dependencies.UpdateLogger.UnexpectedTrailingResultSetWhenSaving();
            }

            reader.Close();
        }
        catch (Exception exception) when (exception is not DbUpdateException and not OperationCanceledException)
        {
            throw new DbUpdateException(
                RelationalStrings.UpdateStoreException,
                exception,
                ModificationCommands[Math.Min(commandIndex, ModificationCommands.Count - 1)].Entries);
        }
    }

    protected override async Task ConsumeAsync(
        RelationalDataReader reader,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(reader);

        var commandIndex = 0;

        try
        {
            bool? onResultSet = null;
            while (commandIndex < ResultSetMappings.Count)
            {
                var command = ModificationCommands[commandIndex];
                if (command.StoreStoredProcedure is not null)
                {
                    onResultSet = await ConsumeStoredProcedureAsync(
                            commandIndex,
                            command,
                            reader,
                            onResultSet,
                            cancellationToken)
                        .ConfigureAwait(false);
                    commandIndex++;
                    continue;
                }

                var resultSetMapping = ResultSetMappings[commandIndex];
                if (resultSetMapping.HasFlag(ResultSetMapping.HasResultRow))
                {
                    if (onResultSet == false)
                    {
                        throw new InvalidOperationException(RelationalStrings.MissingResultSetWhenSaving);
                    }

                    var lastHandledCommandIndex =
                        resultSetMapping.HasFlag(ResultSetMapping.ResultSetWithRowsAffectedOnly)
                            ? await ConsumeResultSetWithRowsAffectedOnlyAsync(commandIndex, reader, cancellationToken)
                                .ConfigureAwait(false)
                            : await ConsumeResultSetAsync(commandIndex, reader, cancellationToken)
                                .ConfigureAwait(false);

                    commandIndex = lastHandledCommandIndex + 1;
                    onResultSet = await reader
                        .DbDataReader.NextResultAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    commandIndex++;
                }
            }

            if (onResultSet == true)
            {
                Dependencies.UpdateLogger.UnexpectedTrailingResultSetWhenSaving();
            }

            await reader
                .CloseAsync()
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not DbUpdateException and not OperationCanceledException)
        {
            throw new DbUpdateException(
                RelationalStrings.UpdateStoreException,
                exception,
                ModificationCommands[Math.Min(commandIndex, ModificationCommands.Count - 1)].Entries);
        }
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

    private void ConsumeStoredProcedure(
        int commandIndex,
        IReadOnlyModificationCommand command,
        RelationalDataReader reader,
        ref bool? onResultSet
    )
    {
        var storedProcedure = command.StoreStoredProcedure!;
        if (storedProcedure.ResultColumns.Any())
        {
            if (onResultSet == false)
            {
                throw new InvalidOperationException(RelationalStrings.MissingResultSetWhenSaving);
            }

            var resultSetMapping = ResultSetMappings[commandIndex];
            if (resultSetMapping.HasFlag(ResultSetMapping.ResultSetWithRowsAffectedOnly))
            {
                ConsumeResultSetWithRowsAffectedOnly(commandIndex, reader);
            }
            else
            {
                ConsumeResultSet(commandIndex, reader);
            }

            onResultSet = reader.DbDataReader.NextResult();
        }

        if (!HasOutputParameters(command))
        {
            return;
        }

        MoveToReadableResultSet(reader, ref onResultSet);
        if (onResultSet == false
            || !reader.Read())
        {
            throw new InvalidOperationException(RelationalStrings.MissingResultSetWhenSaving);
        }

        ConsumeOutputParameterRow(commandIndex, command, reader);
        onResultSet = reader.DbDataReader.NextResult();
    }

    private async Task<bool?> ConsumeStoredProcedureAsync(
        int commandIndex,
        IReadOnlyModificationCommand command,
        RelationalDataReader reader,
        bool? onResultSet,
        CancellationToken cancellationToken
    )
    {
        var storedProcedure = command.StoreStoredProcedure!;
        if (storedProcedure.ResultColumns.Any())
        {
            if (onResultSet == false)
            {
                throw new InvalidOperationException(RelationalStrings.MissingResultSetWhenSaving);
            }

            var resultSetMapping = ResultSetMappings[commandIndex];
            if (resultSetMapping.HasFlag(ResultSetMapping.ResultSetWithRowsAffectedOnly))
            {
                await ConsumeResultSetWithRowsAffectedOnlyAsync(commandIndex, reader, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await ConsumeResultSetAsync(commandIndex, reader, cancellationToken)
                    .ConfigureAwait(false);
            }

            onResultSet = await reader
                .DbDataReader.NextResultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (!HasOutputParameters(command))
        {
            return onResultSet;
        }

        onResultSet = await MoveToReadableResultSetAsync(reader, onResultSet, cancellationToken).ConfigureAwait(false);
        if (onResultSet == false
            || !await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(RelationalStrings.MissingResultSetWhenSaving);
        }

        await ConsumeOutputParameterRowAsync(commandIndex, command, reader, cancellationToken)
            .ConfigureAwait(false);

        return await reader
            .DbDataReader.NextResultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private void ConsumeOutputParameterRow(
        int commandIndex,
        IReadOnlyModificationCommand command,
        RelationalDataReader reader
    )
    {
        ValidateRowsAffectedOutputParameter(commandIndex, command, reader);
        GetMySqlModificationCommand(command)
            .PropagateStoredProcedureOutputParameters(reader);
    }

    private async Task ConsumeOutputParameterRowAsync(
        int commandIndex,
        IReadOnlyModificationCommand command,
        RelationalDataReader reader,
        CancellationToken cancellationToken
    )
    {
        var rowsAffected = GetRowsAffectedOutputParameter(command, reader);
        if (rowsAffected is not null and not 1)
        {
            await ThrowAggregateUpdateConcurrencyExceptionAsync(
                    reader,
                    commandIndex + 1,
                    expectedRowsAffected: 1,
                    rowsAffected: 0,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        GetMySqlModificationCommand(command)
            .PropagateStoredProcedureOutputParameters(reader);
    }

    private void ValidateRowsAffectedOutputParameter(
        int commandIndex,
        IReadOnlyModificationCommand command,
        RelationalDataReader reader
    )
    {
        var rowsAffected = GetRowsAffectedOutputParameter(command, reader);
        if (rowsAffected is not null
            and not 1)
        {
            ThrowAggregateUpdateConcurrencyException(
                reader,
                commandIndex + 1,
                expectedRowsAffected: 1,
                rowsAffected: 0);
        }
    }

    private static int? GetRowsAffectedOutputParameter(
        IReadOnlyModificationCommand command,
        RelationalDataReader reader
    )
    {
        var readerIndex = 0;
        foreach (var modification in command.ColumnModifications)
        {
            if (modification.Column is not IStoreStoredProcedureParameter
                {
                    Direction: ParameterDirection.Output or ParameterDirection.InputOutput,
                })
            {
                continue;
            }

            if (ReferenceEquals(modification.Column, command.RowsAffectedColumn))
            {
                var value = reader.DbDataReader.GetValue(readerIndex);
                if (value is DBNull)
                {
                    throw new InvalidOperationException(
                        RelationalStrings.StoredProcedureRowsAffectedNotPopulated(
                            command.StoreStoredProcedure!.SchemaQualifiedName));
                }

                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }

            readerIndex++;
        }

        return null;
    }

    private static bool HasOutputParameters(
        IReadOnlyModificationCommand command
    ) => command.ColumnModifications.Any(static modification => modification.Column is IStoreStoredProcedureParameter
    {
        Direction: ParameterDirection.Output or ParameterDirection.InputOutput,
    });

    private static MySqlModificationCommand GetMySqlModificationCommand(
        IReadOnlyModificationCommand command
    ) => command as MySqlModificationCommand
        ?? throw new InvalidOperationException(
            "Stored-procedure result propagation requires " + nameof(MySqlModificationCommand) + ".");

    private static void MoveToReadableResultSet(
        RelationalDataReader reader,
        ref bool? onResultSet
    )
    {
        while (onResultSet != false
               && reader.DbDataReader.FieldCount == 0)
        {
            onResultSet = reader.DbDataReader.NextResult();
        }
    }

    private static async Task<bool?> MoveToReadableResultSetAsync(
        RelationalDataReader reader,
        bool? onResultSet,
        CancellationToken cancellationToken
    )
    {
        while (onResultSet != false
               && reader.DbDataReader.FieldCount == 0)
        {
            onResultSet = await reader
                .DbDataReader.NextResultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return onResultSet;
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
