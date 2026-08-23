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
    private readonly RelationalTypeMapping _stringTypeMapping;

    public MySqlUpdateSqlGenerator(
        UpdateSqlGeneratorDependencies dependencies,
        IEnumerable<ISingletonOptions> singletonOptions
    ) : base(dependencies)
    {
        ArgumentNullException.ThrowIfNull(singletonOptions);

        _singletonOptions = singletonOptions
            .OfType<MySqlSingletonOptions>()
            .Single();
        _stringTypeMapping = dependencies.TypeMappingSource.FindMapping(typeof(string))
            ?? throw new InvalidOperationException(
                "The MySQL update SQL generator requires a string type mapping.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// MySQL-family engines use the active database as the provider schema
    /// boundary, so an EF schema must not become a database qualifier.
    /// </remarks>
    public override string GenerateNextSequenceValueOperation(
        string name,
        string? schema
    ) => base.GenerateNextSequenceValueOperation(name, schema: null);

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

        if (!HasReadOperations(command.ColumnModifications))
        {
            var writeOperations = CreateOperations(command.ColumnModifications, OperationKind.Write);

            AppendInsertCommand(
                commandStringBuilder,
                command.TableName,
                command.Schema,
                writeOperations,
                Array.Empty<IColumnModification>());

            requiresTransaction = false;
            return ResultSetMapping.NoResults;
        }

        return SupportsReturningClause()
            ? AppendInsertReturningOperation(commandStringBuilder, command, out requiresTransaction)
            : base.AppendInsertOperation(commandStringBuilder, command, commandPosition, out requiresTransaction);
    }

    /// <inheritdoc />
    public override ResultSetMapping AppendStoredProcedureCall(
        StringBuilder commandStringBuilder,
        IReadOnlyModificationCommand command,
        int commandPosition,
        out bool requiresTransaction
    )
    {
        ArgumentNullException.ThrowIfNull(commandStringBuilder);
        ArgumentNullException.ThrowIfNull(command);

        var storedProcedure = command.StoreStoredProcedure
            ?? throw new InvalidOperationException("A stored-procedure call requires stored-procedure metadata.");

        if (storedProcedure.ReturnValue is not null)
        {
            throw new InvalidOperationException(
                "MySQL-family stored procedures do not expose a return-value "
                + "channel compatible with EF Core's stored-procedure mapping.");
        }

        var outputModifications = command
            .ColumnModifications.Where(static modification => modification.Column is IStoreStoredProcedureParameter
            {
                Direction: ParameterDirection.Output or ParameterDirection.InputOutput,
            })
            .ToArray();

        foreach (var modification in outputModifications)
        {
            var parameter = (IStoreStoredProcedureParameter)modification.Column!;
            var commandParameterName = GetCommandParameterName(modification);

            commandStringBuilder.Append("SET ");
            SqlGenerationHelper.GenerateParameterNamePlaceholder(
                commandStringBuilder,
                GetOutputVariableName(commandParameterName));
            commandStringBuilder.Append(" = ");

            if (parameter.Direction == ParameterDirection.InputOutput)
            {
                SqlGenerationHelper.GenerateParameterNamePlaceholder(commandStringBuilder, commandParameterName);
            }
            else
            {
                commandStringBuilder.Append("NULL");
            }

            commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);
        }

        commandStringBuilder.Append("CALL ");
        SqlGenerationHelper.DelimitIdentifier(commandStringBuilder, storedProcedure.Name, storedProcedure.Schema);
        commandStringBuilder.Append('(');

        var first = true;
        foreach (var modification in command.ColumnModifications)
        {
            if (modification.Column is not IStoreStoredProcedureParameter parameter)
            {
                continue;
            }

            if (!first)
            {
                commandStringBuilder.Append(", ");
            }

            first = false;
            var commandParameterName = GetCommandParameterName(modification);
            SqlGenerationHelper.GenerateParameterNamePlaceholder(
                commandStringBuilder,
                parameter.Direction.HasFlag(ParameterDirection.Output)
                    ? GetOutputVariableName(commandParameterName)
                    : commandParameterName);
        }

        commandStringBuilder
            .Append(')')
            .AppendLine(SqlGenerationHelper.StatementTerminator);

        if (outputModifications.Length > 0)
        {
            commandStringBuilder.Append("SELECT ");
            for (var index = 0; index < outputModifications.Length; index++)
            {
                if (index > 0)
                {
                    commandStringBuilder.Append(", ");
                }

                SqlGenerationHelper.GenerateParameterNamePlaceholder(
                    commandStringBuilder,
                    GetOutputVariableName(GetCommandParameterName(outputModifications[index])));
            }

            commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);
        }

        requiresTransaction = true;
        return GetStoredProcedureResultSetMapping(command, outputModifications);
    }

    /// <inheritdoc />
    protected override void AppendInsertCommandHeader(
        StringBuilder commandStringBuilder,
        string name,
        string? schema,
        IReadOnlyList<IColumnModification> operations
    )
    {
        base.AppendInsertCommandHeader(commandStringBuilder, name, schema, operations);

        // MySQL-family engines represent an insert containing only generated
        // columns with an explicit empty column list and an empty value tuple.
        // The relational default, `DEFAULT VALUES`, is not valid MySQL syntax.
        if (operations.Count == 0)
        {
            commandStringBuilder.Append(" ()");
        }
    }

    /// <inheritdoc />
    protected override void AppendValuesHeader(
        StringBuilder commandStringBuilder,
        IReadOnlyList<IColumnModification> operations
    )
    {
        if (operations.Count == 0)
        {
            commandStringBuilder
                .AppendLine()
                .Append("VALUES ");
            return;
        }

        base.AppendValuesHeader(commandStringBuilder, operations);
    }

    /// <inheritdoc />
    protected override void AppendValues(
        StringBuilder commandStringBuilder,
        string name,
        string? schema,
        IReadOnlyList<IColumnModification> operations
    )
    {
        if (operations.Count == 0)
        {
            commandStringBuilder.Append("()");
            return;
        }

        commandStringBuilder.Append('(');

        for (var index = 0; index < operations.Count; index++)
        {
            if (index > 0)
            {
                commandStringBuilder.Append(", ");
            }

            var operation = operations[index];
            if (!operation.IsWrite)
            {
                commandStringBuilder.Append("DEFAULT");
            }
            else if (operation.UseCurrentValueParameter)
            {
                SqlGenerationHelper.GenerateParameterNamePlaceholder(
                    commandStringBuilder,
                    operation.ParameterName!);
            }
            else
            {
                AppendSqlLiteral(commandStringBuilder, operation, name, schema);
            }
        }

        commandStringBuilder.Append(')');
    }

    /// <summary>
    /// Emits a partial JSON document update when EF supplies a non-root
    /// <see cref="IColumnModification.JsonPath"/>. Scalar properties remain
    /// relational values so MySQL-family engines preserve their JSON scalar
    /// type. Serialized objects and collections pass through
    /// <c>JSON_EXTRACT(value, '$')</c> so <c>JSON_SET</c> inserts JSON instead
    /// of quoting the serialized document as a string.
    /// </summary>
    protected override void AppendUpdateColumnValue(
        ISqlGenerationHelper updateSqlGeneratorHelper,
        IColumnModification columnModification,
        StringBuilder stringBuilder,
        string name,
        string? schema
    )
    {
        if (columnModification.JsonPath is null or "$")
        {
            base.AppendUpdateColumnValue(updateSqlGeneratorHelper, columnModification, stringBuilder, name, schema);
            return;
        }

        stringBuilder.Append("JSON_SET(");
        updateSqlGeneratorHelper.DelimitIdentifier(stringBuilder, columnModification.ColumnName);
        stringBuilder.Append(", ");
        stringBuilder.Append(_stringTypeMapping.GenerateSqlLiteral(columnModification.JsonPath));
        stringBuilder.Append(", ");

        if (columnModification.Property is { IsPrimitiveCollection: false, })
        {
            base.AppendUpdateColumnValue(updateSqlGeneratorHelper, columnModification, stringBuilder, name, schema);
        }
        else
        {
            stringBuilder.Append("JSON_EXTRACT(");
            base.AppendUpdateColumnValue(updateSqlGeneratorHelper, columnModification, stringBuilder, name, schema);
            stringBuilder.Append(", '$')");
        }

        stringBuilder.Append(')');
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

        var firstModifications = modificationCommands[0].ColumnModifications;
        var readOperations = CreateOperations(firstModifications, OperationKind.Read);
        var writeOperations = CreateOperations(
            firstModifications,
            OperationKind.Write,
            out var reusableWriteOperations);

        if (readOperations.Count == 0)
        {
            return AppendInsertMultipleRowsInSingleStatementOperation(
                commandStringBuilder,
                modificationCommands,
                writeOperations,
                reusableWriteOperations,
                out requiresTransaction);
        }

        if (SupportsReturningClause())
        {
            return AppendBulkInsertReturningOperation(
                commandStringBuilder,
                modificationCommands,
                writeOperations,
                readOperations,
                reusableWriteOperations,
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
        IReadOnlyList<IColumnModification> writeOperations,
        IColumnModification[]? reusableWriteOperations,
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
            var currentWriteOperations =
                modificationCommands[index].ColumnModifications;
            if (reusableWriteOperations is not null)
            {
                PopulateOperations(
                    modificationCommands[index].ColumnModifications,
                    reusableWriteOperations,
                    OperationKind.Write);
                currentWriteOperations = reusableWriteOperations;
            }

            AppendValues(
                commandStringBuilder,
                name,
                schema,
                currentWriteOperations);
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
        IReadOnlyList<IColumnModification> writeOperations,
        IReadOnlyList<IColumnModification> readOperations,
        IColumnModification[]? reusableWriteOperations,
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

            var currentWriteOperations = modificationCommands[index].ColumnModifications;
            if (reusableWriteOperations is not null)
            {
                PopulateOperations(
                    modificationCommands[index].ColumnModifications,
                    reusableWriteOperations,
                    OperationKind.Write);
                currentWriteOperations = reusableWriteOperations;
            }

            AppendValues(
                commandStringBuilder,
                name,
                schema,
                currentWriteOperations);
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
        var writeColumns = CreateOperations(command.ColumnModifications, OperationKind.Write);
        var readColumns = CreateOperations(command.ColumnModifications, OperationKind.Read);
        var conditionColumns = CreateOperations(command.ColumnModifications, OperationKind.Condition);

        Debug.Assert(
            readColumns.Count > 0,
            "The caller routes write-only inserts before selecting the RETURNING path.");

        AppendInsertCommand(
            commandStringBuilder,
            command.TableName,
            command.Schema,
            writeColumns,
            conditionColumns);

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

    private static bool HasReadOperations(
        IReadOnlyList<IColumnModification> modifications
    )
    {
        for (var index = 0; index < modifications.Count; index++)
        {
            if (modifications[index].IsRead)
            {
                return true;
            }
        }

        return false;
    }

    private bool SupportsReturningClause()
    {
        var profile = _singletonOptions.Profile
            ?? throw new InvalidOperationException(
                "The MySQL update SQL generator requires an initialized provider profile.");

        return profile.GetSupport(ProviderCapability.ReturningClause) == ProviderSupportStatus.Native;
    }

    private static IReadOnlyList<IColumnModification> CreateOperations(
        IReadOnlyList<IColumnModification> modifications,
        OperationKind kind
    ) => CreateOperations(modifications, kind, out _);

    private static IReadOnlyList<IColumnModification> CreateOperations(
        IReadOnlyList<IColumnModification> modifications,
        OperationKind kind,
        out IColumnModification[]? reusableOperations
    )
    {
        var operationCount = 0;
        for (var index = 0; index < modifications.Count; index++)
        {
            if (IsOperation(modifications[index], kind))
            {
                operationCount++;
            }
        }

        if (operationCount == modifications.Count)
        {
            reusableOperations = null;
            return modifications;
        }

        reusableOperations = operationCount == 0
            ? []
            : new IColumnModification[operationCount];

        if (operationCount != 0)
        {
            PopulateOperations(modifications, reusableOperations, kind);
        }

        return reusableOperations;
    }

    private static void PopulateOperations(
        IReadOnlyList<IColumnModification> modifications,
        IColumnModification[] destination,
        OperationKind kind
    )
    {
        var destinationIndex = 0;
        for (var index = 0; index < modifications.Count; index++)
        {
            var modification = modifications[index];

            if (IsOperation(modification, kind))
            {
                if (destinationIndex == destination.Length)
                {
                    throw new InvalidOperationException("Modification command shapes do not match.");
                }

                destination[destinationIndex++] = modification;
            }
        }

        if (destinationIndex != destination.Length)
        {
            throw new InvalidOperationException("Modification command shapes do not match.");
        }
    }

    private static bool IsOperation(
        IColumnModification modification,
        OperationKind kind
    ) => kind switch
    {
        OperationKind.Read => modification.IsRead,
        OperationKind.Write => modification.IsWrite,
        OperationKind.Condition => modification.IsCondition,
        _ => throw new UnreachableException(),
    };

    private enum OperationKind
    {
        Read,
        Write,
        Condition,
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

        Debug.Assert(
            commandStringBuilder.Length >= terminator.Length,
            "AppendInsertCommand must emit a statement terminator before RETURNING is appended.");

        var existingTerminator = commandStringBuilder.ToString(
            commandStringBuilder.Length - terminator.Length,
            terminator.Length);

        if (existingTerminator == terminator)
        {
            commandStringBuilder.Length -= terminator.Length;
        }
    }

    private static string GetCommandParameterName(
        IColumnModification modification
    ) => modification.UseOriginalValueParameter
        ? modification.OriginalParameterName!
        : modification.ParameterName!;

    private static string GetOutputVariableName(
        string commandParameterName
    ) => "_out_" + commandParameterName;

    private static ResultSetMapping GetStoredProcedureResultSetMapping(
        IReadOnlyModificationCommand command,
        IColumnModification[] outputModifications
    )
    {
        var storedProcedure = command.StoreStoredProcedure!;
        if (!storedProcedure.ResultColumns.Any()
            && outputModifications.Length == 0)
        {
            return ResultSetMapping.NoResults;
        }

        var onlyRowsAffected = storedProcedure.ResultColumns.Any()
            ? storedProcedure.ResultColumns.All(resultColumn => ReferenceEquals(
                resultColumn,
                command.RowsAffectedColumn))
            : outputModifications.All(modification => ReferenceEquals(modification.Column, command.RowsAffectedColumn));

        return ResultSetMapping.LastInResultSet
            | (onlyRowsAffected ? ResultSetMapping.ResultSetWithRowsAffectedOnly : ResultSetMapping.NoResults);
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

    /// <inheritdoc />
    protected override bool IsIdentityOperation(
        IColumnModification modification
    ) => IsIdentityColumn(modification);

    internal static bool IsIdentityColumn(
        IColumnModification modification
    ) => modification is { IsKey: true, IsRead: true, IsWrite: false, }
        && (modification.Property is null
            || modification.Property.GetMySqlValueGenerationStrategy() == MySqlValueGenerationStrategy.AutoIncrement
            || modification.Property.ValueGenerated == ValueGenerated.Never);

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
            .AppendLine(SqlGenerationHelper.StatementTerminator)
            .AppendLine();

        return ResultSetMapping.LastInResultSet | ResultSetMapping.ResultSetWithRowsAffectedOnly;
    }

    /// <inheritdoc />
    public override void PrependEnsureAutocommit(
        StringBuilder commandStringBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(commandStringBuilder);

        commandStringBuilder.Insert(
            0,
            $"SET AUTOCOMMIT = 1{SqlGenerationHelper.StatementTerminator}" + Environment.NewLine);
    }
}
