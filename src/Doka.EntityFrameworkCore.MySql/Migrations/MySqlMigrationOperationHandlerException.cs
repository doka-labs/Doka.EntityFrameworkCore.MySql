namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Represents a fail-closed custom migration-operation handler failure.
/// </summary>
public sealed class MySqlMigrationOperationHandlerException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance with bounded diagnostic metadata.
    /// </summary>
    /// <param name="failureCode">The stable failure classification.</param>
    /// <param name="handlerId">The validated handler identifier, when known.</param>
    /// <param name="operationType">The exact operation type involved.</param>
    /// <param name="generationOptions">The active EF Core generation options.</param>
    /// <param name="operationOrdinal">The zero-based operation ordinal.</param>
    /// <param name="innerException">
    /// The original exception for the direct caller. Provider logs and telemetry
    /// never record its message, stack trace, or data.
    /// </param>
    internal MySqlMigrationOperationHandlerException(
        MySqlMigrationHandlerFailureCode failureCode,
        string? handlerId,
        Type operationType,
        MigrationsSqlGenerationOptions generationOptions,
        int operationOrdinal,
        Exception? innerException = null
    ) : base(CreateMessage(failureCode, handlerId, operationType, generationOptions, operationOrdinal), innerException)
    {
        ArgumentNullException.ThrowIfNull(operationType);

        FailureCode = failureCode;
        HandlerId = handlerId;
        OperationType = operationType.FullName ?? operationType.Name;
        GenerationOptions = generationOptions;
        OperationOrdinal = operationOrdinal;
    }

    /// <summary>Gets the stable failure classification.</summary>
    public MySqlMigrationHandlerFailureCode FailureCode { get; }

    /// <summary>Gets the validated handler identifier, when known.</summary>
    public string? HandlerId { get; }

    /// <summary>Gets the fully qualified exact operation type name.</summary>
    public string OperationType { get; }

    /// <summary>Gets the active EF Core migration generation options.</summary>
    public MigrationsSqlGenerationOptions GenerationOptions { get; }

    /// <summary>Gets the zero-based ordinal in the current generation call.</summary>
    public int OperationOrdinal { get; }

    private static string CreateMessage(
        MySqlMigrationHandlerFailureCode failureCode,
        string? handlerId,
        Type operationType,
        MigrationsSqlGenerationOptions generationOptions,
        int operationOrdinal
    )
    {
        ArgumentNullException.ThrowIfNull(operationType);

        return $"Migration operation handler failure {failureCode} for "
            + $"{operationType.FullName ?? operationType.Name} at ordinal {operationOrdinal} "
            + $"with generation options {generationOptions}"
            + (handlerId is null ? "." : $" and handler {handlerId}.");
    }
}
