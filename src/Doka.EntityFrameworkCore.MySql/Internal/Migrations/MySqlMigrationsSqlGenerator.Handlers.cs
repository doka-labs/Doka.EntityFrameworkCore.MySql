namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsSqlGenerator
{
    /// <inheritdoc />
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default
    )
    {
        ArgumentNullException.ThrowIfNull(operations);

        Options = options;
        _operationOrdinal = -1;
        var builder = new MySqlMigrationCommandListBuilder(Dependencies);

        try
        {
            foreach (var operation in operations)
            {
                Generate(operation, model, builder);
            }
        }
        finally
        {
            Options = MigrationsSqlGenerationOptions.Default;
            _operationOrdinal = -1;
        }

        return builder.GetCommandList();
    }

    /// <summary>
    /// Extends EF Core's exact-type dispatch with a validated custom-operation
    /// registry. Built-in operations remain exclusively provider-owned.
    /// </summary>
    protected override void Generate(
        MigrationOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        _operationOrdinal++;
        var operationType = operation.GetType();

        if (_operationHandlerRegistry.TryGet(operationType, out var registration))
        {
            GenerateCustomOperation(operation, model, builder, registration);
            return;
        }

        if (MySqlStandardMigrationOperations.Contains(operationType))
        {
            base.Generate(operation, model, builder);
            return;
        }

        ThrowUnknownOperation(operationType);
    }

    private void GenerateCustomOperation(
        MigrationOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        MySqlMigrationOperationHandlerRegistry.Registration registration
    )
    {
        var serverVersion = _mySqlSingletonOptions.ServerVersion
            ?? throw new InvalidOperationException(
                "The server version must be initialized before migration SQL generation.");

        var generationMode = GetGenerationMode(Options);
        var operationType = registration.OperationType.FullName ?? registration.OperationType.Name;

        MySqlLoggerMessages.MigrationOperationHandlerSelected(
            Dependencies.MigrationsLogger.Logger,
            registration.HandlerId,
            operationType,
            generationMode,
            _operationOrdinal);

        using var activity = MySqlActivitySource.StartMigrationOperationHandler(
            registration.HandlerId,
            operationType,
            generationMode,
            serverVersion.Profile.Engine.Family);

        var context = new MySqlMigrationOperationContext(
            operation,
            model,
            Options,
            serverVersion,
            _migrationFeatures,
            _operationOrdinal,
            registration.HandlerId,
            standardOperation => RenderStandardOperation(standardOperation, model));

        MySqlMigrationOperationResult? result;
        var startedAt = Stopwatch.GetTimestamp();
        double handlerDurationSeconds;

        try
        {
            result = registration.Handler.Generate(context);
            handlerDurationSeconds = Stopwatch.GetElapsedTime(startedAt)
                .TotalSeconds;
        }
        catch (MySqlMigrationOperationHandlerException exception) when (exception.FailureCode is
                                                                            MySqlMigrationHandlerFailureCode
                                                                                .InvalidHandlerResult
                                                                            or MySqlMigrationHandlerFailureCode
                                                                                .UnknownOperationType
                                                                            or MySqlMigrationHandlerFailureCode
                                                                                .RecursiveProviderRendering)
        {
            handlerDurationSeconds = Stopwatch.GetElapsedTime(startedAt)
                .TotalSeconds;
            RecordContractViolation(
                activity,
                registration,
                operationType,
                generationMode,
                serverVersion.Profile.Engine.Family,
                exception.FailureCode,
                handlerDurationSeconds);
            throw;
        }
        catch (Exception exception)
        {
            handlerDurationSeconds = Stopwatch.GetElapsedTime(startedAt)
                .TotalSeconds;
            const MySqlMigrationHandlerFailureCode failureCode = MySqlMigrationHandlerFailureCode.HandlerFailed;
            const string outcome = "handler_failed";

            activity?.SetTag(MySqlDiagnosticTags.MigrationHandlerOutcome, outcome);
            activity?.SetTag(MySqlDiagnosticTags.ErrorType, failureCode.ToString());
            activity?.SetStatus(ActivityStatusCode.Error);
            MySqlLoggerMessages.MigrationOperationHandlerFailed(
                Dependencies.MigrationsLogger.Logger,
                registration.HandlerId,
                operationType,
                generationMode,
                _operationOrdinal,
                failureCode,
                exception);

            var failureTags = CreateHandlerMetricTags(
                registration.HandlerId,
                operationType,
                generationMode,
                outcome,
                serverVersion.Profile.Engine.Family,
                failureCode.ToString());

            RecordHandlerCompletion(failureTags, handlerDurationSeconds);
            MySqlMeter.MigrationOperationHandlerFailuresTotal.Add(1, failureTags);

            throw new MySqlMigrationOperationHandlerException(
                failureCode,
                registration.HandlerId,
                registration.OperationType,
                Options,
                _operationOrdinal,
                exception);
        }
        finally
        {
            context.Deactivate();
        }

        IReadOnlyList<MySqlMigrationCommandSpec> commands;

        try
        {
            commands = ValidateResult(result, registration);
        }
        catch (MySqlMigrationOperationHandlerException exception)
        {
            RecordContractViolation(
                activity,
                registration,
                operationType,
                generationMode,
                serverVersion.Profile.Engine.Family,
                exception.FailureCode,
                handlerDurationSeconds);
            throw;
        }

        activity?.SetTag(MySqlDiagnosticTags.MigrationHandlerOutcome, result!.OutcomeCode);
        var successTags = CreateHandlerMetricTags(
            registration.HandlerId,
            operationType,
            generationMode,
            result.OutcomeCode,
            serverVersion.Profile.Engine.Family);

        RecordHandlerCompletion(successTags, handlerDurationSeconds);

        foreach (var command in commands)
        {
            GetProviderCommandBuilder(builder).AppendCommandSpec(command);
        }
    }

    private System.Collections.ObjectModel.ReadOnlyCollection<MySqlMigrationCommandSpec> RenderStandardOperation(
        MigrationOperation operation,
        IModel? model
    )
    {
        var builder = new MySqlMigrationCommandListBuilder(Dependencies);

        // Calling the base implementation is the intentional bypass boundary:
        // EF Core still reaches Doka's typed overrides, while this custom
        // exact-type dispatcher cannot recursively select another plugin.
        base.Generate(operation, model, builder);

        return builder.GetCommandSpecs();
    }

    private System.Collections.ObjectModel.ReadOnlyCollection<MySqlMigrationCommandSpec> ValidateResult(
        MySqlMigrationOperationResult? result,
        MySqlMigrationOperationHandlerRegistry.Registration registration
    )
    {
        if (result?.Commands is null
            || string.IsNullOrWhiteSpace(result.OutcomeCode)
            || !MySqlMigrationOperationResult.IsValidOutcomeCode(result.OutcomeCode))
        {
            throw CreateInvalidResultException(registration);
        }

        MySqlMigrationCommandSpec[] commands;

        try
        {
            // Copy the foreign collection before inspecting it. A plugin may
            // expose a mutable or stateful IReadOnlyList, so validating one
            // enumeration and appending another would be a time-of-check to
            // time-of-use boundary violation.
            commands = result.Commands.ToArray();
        }
        catch (Exception exception)
        {
            throw CreateInvalidResultException(registration, exception);
        }

        if (commands.Length == 0
            || commands.Any(command => command is null || string.IsNullOrWhiteSpace(command.CommandText)))
        {
            throw CreateInvalidResultException(registration);
        }

        return Array.AsReadOnly(commands);
    }

    private MySqlMigrationOperationHandlerException CreateInvalidResultException(
        MySqlMigrationOperationHandlerRegistry.Registration registration,
        Exception? innerException = null
    ) => new(
        MySqlMigrationHandlerFailureCode.InvalidHandlerResult,
        registration.HandlerId,
        registration.OperationType,
        Options,
        _operationOrdinal,
        innerException);

    private void RecordContractViolation(
        Activity? activity,
        MySqlMigrationOperationHandlerRegistry.Registration registration,
        string operationType,
        string generationMode,
        EngineFamily engineFamily,
        MySqlMigrationHandlerFailureCode failureCode,
        double handlerDurationSeconds
    )
    {
        var outcome = failureCode switch
        {
            MySqlMigrationHandlerFailureCode.InvalidHandlerResult => "invalid_handler_result",
            MySqlMigrationHandlerFailureCode.UnknownOperationType => "unknown_operation",
            MySqlMigrationHandlerFailureCode.RecursiveProviderRendering => "recursive_provider_rendering",
            _ => throw new UnreachableException(),
        };

        activity?.SetTag(MySqlDiagnosticTags.MigrationHandlerOutcome, outcome);
        activity?.SetTag(MySqlDiagnosticTags.ErrorType, failureCode.ToString());
        activity?.SetStatus(ActivityStatusCode.Error);

        MySqlLoggerMessages.MigrationOperationHandlerContractViolation(
            Dependencies.MigrationsLogger.Logger,
            registration.HandlerId,
            operationType,
            generationMode,
            _operationOrdinal,
            failureCode);

        var tags = CreateHandlerMetricTags(
            registration.HandlerId,
            operationType,
            generationMode,
            outcome,
            engineFamily,
            failureCode.ToString());

        RecordHandlerCompletion(tags, handlerDurationSeconds);
        MySqlMeter.MigrationOperationHandlerContractViolationsTotal.Add(1, tags);
    }

    private static void RecordHandlerCompletion(
        in TagList tags,
        double handlerDurationSeconds
    )
    {
        MySqlMeter.MigrationOperationHandlerCallsTotal.Add(1, tags);
        MySqlMeter.MigrationOperationHandlerDuration.Record(handlerDurationSeconds, tags);
    }

    [DoesNotReturn]
    private void ThrowUnknownOperation(
        Type operationType
    )
    {
        var generationMode = GetGenerationMode(Options);
        var operationTypeName = operationType.FullName ?? operationType.Name;

        MySqlLoggerMessages.UnknownMigrationOperation(
            Dependencies.MigrationsLogger.Logger,
            operationTypeName,
            generationMode,
            _operationOrdinal);

        var profile = Profile;
        var tags = CreateHandlerMetricTags(
            "none",
            operationTypeName,
            generationMode,
            "unknown_operation",
            profile.Engine.Family,
            nameof(MySqlMigrationHandlerFailureCode.UnknownOperationType));

        MySqlMeter.MigrationOperationHandlerContractViolationsTotal.Add(1, tags);

        throw new MySqlMigrationOperationHandlerException(
            MySqlMigrationHandlerFailureCode.UnknownOperationType,
            null,
            operationType,
            Options,
            _operationOrdinal);
    }

    private static TagList CreateHandlerMetricTags(
        string handlerId,
        string operationType,
        string generationMode,
        string outcome,
        EngineFamily engineFamily,
        string? errorType = null
    )
    {
        var tags = new TagList
        {
            { MySqlDiagnosticTags.MigrationHandlerId, handlerId },
            { MySqlDiagnosticTags.MigrationOperationType, operationType },
            { MySqlDiagnosticTags.MigrationGenerationMode, generationMode },
            { MySqlDiagnosticTags.MigrationHandlerOutcome, outcome },
            { MySqlDiagnosticTags.Engine, MySqlDiagnosticTags.GetDatabaseSystem(engineFamily) },
        };

        if (errorType is not null)
        {
            tags.Add(MySqlDiagnosticTags.ErrorType, errorType);
        }

        return tags;
    }

    private static string GetGenerationMode(
        MigrationsSqlGenerationOptions options
    ) => options switch
    {
        MigrationsSqlGenerationOptions.Default => "default",
        MigrationsSqlGenerationOptions.Script => "script",
        MigrationsSqlGenerationOptions.Idempotent => "idempotent",
        MigrationsSqlGenerationOptions.NoTransactions => "no_transactions",
        MigrationsSqlGenerationOptions.Script | MigrationsSqlGenerationOptions.Idempotent => "script_idempotent",
        MigrationsSqlGenerationOptions.Script | MigrationsSqlGenerationOptions.NoTransactions =>
            "script_no_transactions",
        MigrationsSqlGenerationOptions.Idempotent | MigrationsSqlGenerationOptions.NoTransactions =>
            "idempotent_no_transactions",
        MigrationsSqlGenerationOptions.Script
            | MigrationsSqlGenerationOptions.Idempotent
            | MigrationsSqlGenerationOptions.NoTransactions => "script_idempotent_no_transactions",
        _ => "unknown",
    };
}
