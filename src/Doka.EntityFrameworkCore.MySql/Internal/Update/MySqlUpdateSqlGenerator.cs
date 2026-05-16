namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// MySQL update-SQL surface. Single-row INSERTs emit through the base generator on
/// MySQL and through INSERT ... RETURNING on MariaDB 10.5+ so the auto-increment value
/// (plus any trigger-modified column) comes back in a single round-trip instead of
/// the LAST_INSERT_ID + follow-up SELECT pattern. Multi-row INSERTs collapse into a
/// single INSERT INTO ... VALUES (...),(...),(...) statement when consecutive commands
/// target the same table + column set; on MariaDB 10.5+ the multi-row form is
/// extended with RETURNING so the generated values come back without a per-row
/// round-trip.
/// </summary>
internal sealed class MySqlUpdateSqlGenerator : UpdateAndSelectSqlGenerator
{
    private readonly MySqlSingletonOptions _singletonOptions;

    public MySqlUpdateSqlGenerator(
        UpdateSqlGeneratorDependencies dependencies,
        IEnumerable<ISingletonOptions> singletonOptions
    ) : base(dependencies)
    {
        ArgumentNullException.ThrowIfNull(singletonOptions);

        _singletonOptions = singletonOptions
            .OfType<MySqlSingletonOptions>()
            .Single();
    }

    /// <inheritdoc />
    public override ResultSetMapping AppendInsertOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyModificationCommand command,
        int commandPosition,
        out bool requiresTransaction
    )
    {
        ArgumentNullException.ThrowIfNull(commandStringBuilder);
        ArgumentNullException.ThrowIfNull(command);

        return _singletonOptions.Profile?.Has(Capability.SupportsReturningClause) == true
            ? AppendInsertReturningOperation(commandStringBuilder, command, out requiresTransaction)
            : base.AppendInsertOperation(commandStringBuilder, command, commandPosition, out requiresTransaction);
    }

    /// <summary>
    /// Multi-row INSERT entry point invoked by <see cref="MySqlModificationCommandBatch"/>.
    /// Routes to one of three paths: single-command delegation, multi-row VALUES list
    /// (write-only), or multi-row VALUES list with RETURNING (MariaDB 10.5+). When the
    /// engine does not support RETURNING and the commands need read-back, falls back
    /// to per-command single-row INSERTs because <c>LAST_INSERT_ID()</c> reports only
    /// the first auto-increment value across a multi-row batch and the remaining ids
    /// would need server-side correlation the provider does not perform today.
    /// </summary>
    public ResultSetMapping AppendBulkInsertOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyList<IReadOnlyModificationCommand> modificationCommands,
        int commandPosition,
        out bool requiresTransaction
    )
    {
        ArgumentNullException.ThrowIfNull(commandStringBuilder);
        ArgumentNullException.ThrowIfNull(modificationCommands);

        if (modificationCommands.Count == 1)
        {
            return AppendInsertOperation(
                commandStringBuilder,
                modificationCommands[0],
                commandPosition,
                out requiresTransaction);
        }

        var firstCommand = modificationCommands[0];
        var readOperations = firstCommand
            .ColumnModifications.Where(o => o.IsRead)
            .ToList();
        var writeOperations = firstCommand
            .ColumnModifications.Where(o => o.IsWrite)
            .ToList();

        if (readOperations.Count == 0)
        {
            return AppendInsertMultipleRowsInSingleStatementOperation(
                commandStringBuilder,
                modificationCommands,
                writeOperations,
                out requiresTransaction);
        }

        if (_singletonOptions.Profile?.Has(Capability.SupportsReturningClause) == true)
        {
            return AppendBulkInsertReturningOperation(
                commandStringBuilder,
                modificationCommands,
                writeOperations,
                readOperations,
                out requiresTransaction);
        }

        // Fallback: per-row INSERT loop. MySQL's LAST_INSERT_ID() returns only the first
        // auto-increment value of a multi-row INSERT; correlating subsequent generated
        // values without RETURNING requires server-side hops the provider does not take.
        requiresTransaction = modificationCommands.Count > 1;
        foreach (var modification in modificationCommands)
        {
            AppendInsertOperation(
                commandStringBuilder,
                modification,
                commandPosition,
                out var localRequiresTransaction);
            requiresTransaction = requiresTransaction || localRequiresTransaction;
        }

        return ResultSetMapping.LastInResultSet;
    }

    /// <summary>
    /// Emits a single <c>INSERT INTO t (cols) VALUES (...),(...),(...);</c> statement for
    /// write-only batches. <see cref="ResultSetMapping.NoResults"/> because the caller
    /// already knows the row count and no server-generated columns need to come back.
    /// </summary>
    private ResultSetMapping AppendInsertMultipleRowsInSingleStatementOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyList<IReadOnlyModificationCommand> modificationCommands,
        List<IColumnModification> writeOperations,
        out bool requiresTransaction
    )
    {
        var name = modificationCommands[0].TableName;
        var schema = modificationCommands[0].Schema;

        AppendInsertCommandHeader(commandStringBuilder, name, schema, writeOperations);
        AppendValuesHeader(commandStringBuilder, writeOperations);
        AppendValues(commandStringBuilder, name, schema, writeOperations);

        for (var index = 1; index < modificationCommands.Count; index++)
        {
            commandStringBuilder
                .Append(',')
                .AppendLine();
            AppendValues(
                commandStringBuilder,
                name,
                schema,
                modificationCommands[index]
                    .ColumnModifications.Where(o => o.IsWrite)
                    .ToList());
        }

        commandStringBuilder
            .Append(SqlGenerationHelper.StatementTerminator)
            .AppendLine();

        requiresTransaction = false;
        return ResultSetMapping.NoResults;
    }

    /// <summary>
    /// MariaDB 10.5+ multi-row INSERT with RETURNING: collapses a multi-row write +
    /// per-row read-back into a single statement whose result set contains one row per
    /// inserted row, in INSERT order. Returns <see cref="ResultSetMapping.NotLastInResultSet"/>;
    /// <see cref="MySqlModificationCommandBatch.ApplyPendingBulkInsertCommands"/> flips
    /// the last command's mapping to <see cref="ResultSetMapping.LastInResultSet"/> so the
    /// batch reader consumes N rows from one result set, each propagated to the
    /// corresponding command in INSERT order.
    /// </summary>
    private ResultSetMapping AppendBulkInsertReturningOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyList<IReadOnlyModificationCommand> modificationCommands,
        List<IColumnModification> writeOperations,
        List<IColumnModification> readOperations,
        out bool requiresTransaction
    )
    {
        var name = modificationCommands[0].TableName;
        var schema = modificationCommands[0].Schema;

        AppendInsertCommandHeader(commandStringBuilder, name, schema, writeOperations);
        AppendValuesHeader(commandStringBuilder, writeOperations);
        AppendValues(commandStringBuilder, name, schema, writeOperations);

        for (var index = 1; index < modificationCommands.Count; index++)
        {
            commandStringBuilder
                .Append(',')
                .AppendLine();
            AppendValues(
                commandStringBuilder,
                name,
                schema,
                modificationCommands[index]
                    .ColumnModifications.Where(o => o.IsWrite)
                    .ToList());
        }

        commandStringBuilder.AppendLine();
        commandStringBuilder.Append("RETURNING ");

        for (var index = 0; index < readOperations.Count; index++)
        {
            if (index > 0)
            {
                commandStringBuilder.Append(", ");
            }

            SqlGenerationHelper.DelimitIdentifier(commandStringBuilder, readOperations[index].ColumnName);
        }

        commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);
        requiresTransaction = false;
        return ResultSetMapping.NotLastInResultSet;
    }

    /// <summary>
    /// MariaDB 10.5+ INSERT ... RETURNING: collapses the standard EF Core
    /// INSERT + SELECT LAST_INSERT_ID round-trip pair into a single statement
    /// whose result set carries the generated columns (auto-increment, computed,
    /// trigger-modified). Reads the columns to project from
    /// <see cref="IReadOnlyModificationCommand.ColumnModifications"/> -- every
    /// column that the database produces (IsRead) is emitted in the RETURNING
    /// list in declaration order.
    /// </summary>
    private ResultSetMapping AppendInsertReturningOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyModificationCommand command,
        out bool requiresTransaction
    )
    {
        var writeColumns = command
            .ColumnModifications.Where(c => c.IsWrite)
            .ToList();
        var readColumns = command
            .ColumnModifications.Where(c => c.IsRead)
            .ToList();

        AppendInsertCommand(
            commandStringBuilder,
            command.TableName,
            command.Schema,
            writeColumns,
            command
                .ColumnModifications.Where(c => c.IsCondition)
                .ToList());

        if (readColumns.Count == 0)
        {
            requiresTransaction = false;
            return ResultSetMapping.NoResults;
        }

        // Strip the trailing terminator the base AppendInsertCommand wrote so we can
        // splice in the RETURNING clause before the statement terminator.
        TrimTrailingStatementTerminator(commandStringBuilder);

        commandStringBuilder.AppendLine();
        commandStringBuilder.Append("RETURNING ");

        for (var index = 0; index < readColumns.Count; index++)
        {
            if (index > 0)
            {
                commandStringBuilder.Append(", ");
            }

            SqlGenerationHelper.DelimitIdentifier(commandStringBuilder, readColumns[index].ColumnName);
        }

        commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);
        requiresTransaction = false;
        return ResultSetMapping.LastInResultSet;
    }

    private void TrimTrailingStatementTerminator(
        StringBuilder commandStringBuilder
    )
    {
        var terminator = SqlGenerationHelper.StatementTerminator;
        while (commandStringBuilder.Length > 0)
        {
            var last = commandStringBuilder[^1];
            if (last is '\r' or '\n')
            {
                commandStringBuilder.Length--;
                continue;
            }

            break;
        }

        if (commandStringBuilder.Length >= terminator.Length
            && commandStringBuilder.ToString(commandStringBuilder.Length - terminator.Length, terminator.Length) == terminator)
        {
            commandStringBuilder.Length -= terminator.Length;
        }
    }

    protected override void AppendIdentityWhereCondition(
        StringBuilder commandStringBuilder,
        IColumnModification columnModification
    )
    {
        ArgumentNullException.ThrowIfNull(commandStringBuilder);
        ArgumentNullException.ThrowIfNull(columnModification);

        SqlGenerationHelper.DelimitIdentifier(commandStringBuilder, columnModification.ColumnName);
        commandStringBuilder.Append(" = LAST_INSERT_ID()");
    }

    protected override void AppendRowsAffectedWhereCondition(
        StringBuilder commandStringBuilder,
        int expectedRowsAffected
    )
    {
        ArgumentNullException.ThrowIfNull(commandStringBuilder);

        commandStringBuilder.Append("ROW_COUNT() = ");
        commandStringBuilder.Append(expectedRowsAffected.ToString(CultureInfo.InvariantCulture));
    }

    protected override ResultSetMapping AppendSelectAffectedCountCommand(
        StringBuilder commandStringBuilder,
        string name,
        string? schema,
        int commandPosition
    )
    {
        ArgumentNullException.ThrowIfNull(commandStringBuilder);

        commandStringBuilder
            .Append("SELECT ROW_COUNT()")
            .AppendLine(SqlGenerationHelper.StatementTerminator);

        return ResultSetMapping.LastInResultSet | ResultSetMapping.ResultSetWithRowsAffectedOnly;
    }
}
