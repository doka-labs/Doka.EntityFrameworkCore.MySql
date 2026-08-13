namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides the immutable provider state available to one custom migration
/// operation handler invocation.
/// </summary>
public sealed class MySqlMigrationOperationContext
{
    private readonly Func<MigrationOperation, IReadOnlyList<MySqlMigrationCommandSpec>> _standardRenderer;
    private readonly object _renderStateLock = new();
    private bool _active = true;
    private bool _rendering;

    internal MySqlMigrationOperationContext(
        MigrationOperation operation,
        IModel? model,
        MigrationsSqlGenerationOptions options,
        MySqlServerVersion serverVersion,
        MySqlMigrationFeatureSet features,
        int operationOrdinal,
        string handlerId,
        Func<MigrationOperation, IReadOnlyList<MySqlMigrationCommandSpec>> standardRenderer
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(serverVersion);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerId);
        ArgumentNullException.ThrowIfNull(standardRenderer);

        Operation = operation;
        Model = model;
        Options = options;
        ServerVersion = serverVersion;
        Features = features;
        OperationOrdinal = operationOrdinal;
        HandlerId = handlerId;
        _standardRenderer = standardRenderer;
    }

    /// <summary>Gets the original exact custom migration operation.</summary>
    public MigrationOperation Operation { get; }

    /// <summary>Gets the target model supplied to EF Core, when available.</summary>
    public IModel? Model { get; }

    /// <summary>Gets the active EF Core migration SQL generation options.</summary>
    public MigrationsSqlGenerationOptions Options { get; }

    /// <summary>Gets the configured server-version descriptor.</summary>
    public MySqlServerVersion ServerVersion { get; }

    /// <summary>Gets the canonical migration capability projection.</summary>
    public MySqlMigrationFeatureSet Features { get; }

    /// <summary>Gets the zero-based ordinal in the current generation call.</summary>
    public int OperationOrdinal { get; }

    internal string HandlerId { get; }

    /// <summary>
    /// Renders one standard EF Core migration operation through the provider's
    /// built-in generator without invoking external handlers.
    /// </summary>
    /// <param name="operation">A concrete built-in EF Core operation.</param>
    /// <returns>An immutable copy of the generated command boundaries.</returns>
    /// <exception cref="MySqlMigrationOperationHandlerException">
    /// The context has expired, the operation is custom, or rendering is entered
    /// recursively or concurrently.
    /// </exception>
    public IReadOnlyList<MySqlMigrationCommandSpec> RenderStandardOperation(
        MigrationOperation operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_renderStateLock)
        {
            if (!_active)
            {
                throw CreateContractException(MySqlMigrationHandlerFailureCode.ContextExpired);
            }

            if (!MySqlStandardMigrationOperations.Contains(operation.GetType()))
            {
                throw CreateContractException(MySqlMigrationHandlerFailureCode.UnknownOperationType);
            }

            if (_rendering)
            {
                throw CreateContractException(MySqlMigrationHandlerFailureCode.RecursiveProviderRendering);
            }

            _rendering = true;
        }

        try
        {
            return _standardRenderer(operation);
        }
        finally
        {
            lock (_renderStateLock)
            {
                _rendering = false;
                Monitor.PulseAll(_renderStateLock);
            }
        }
    }

    internal void Deactivate()
    {
        lock (_renderStateLock)
        {
            // Closing the lease first prevents new rendering. Waiting for the
            // current render then guarantees that no provider callback can
            // outlive the handler invocation that owns this context.
            _active = false;

            while (_rendering)
            {
                Monitor.Wait(_renderStateLock);
            }
        }
    }

    private MySqlMigrationOperationHandlerException CreateContractException(
        MySqlMigrationHandlerFailureCode failureCode
    ) => new(
        failureCode,
        HandlerId,
        Operation.GetType(),
        Options,
        OperationOrdinal);
}
