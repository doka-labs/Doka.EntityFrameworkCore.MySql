namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Classifies fail-closed migration-operation handler failures without exposing
/// SQL or plugin exception text.
/// </summary>
public enum MySqlMigrationHandlerFailureCode
{
    /// <summary>A handler registration violates the public contract.</summary>
    InvalidRegistration,

    /// <summary>Two registrations expose the same stable handler identifier.</summary>
    DuplicateHandlerId,

    /// <summary>Two registrations claim the same exact custom operation type.</summary>
    DuplicateOperationOwnership,

    /// <summary>An external handler attempts to claim an EF Core built-in operation.</summary>
    ReservedOperationType,

    /// <summary>No built-in or registered handler owns the operation type.</summary>
    UnknownOperationType,

    /// <summary>The selected handler threw while generating its result.</summary>
    HandlerFailed,

    /// <summary>The selected handler returned a result that violates the contract.</summary>
    InvalidHandlerResult,

    /// <summary>A handler entered provider baseline rendering recursively or concurrently.</summary>
    RecursiveProviderRendering,

    /// <summary>A handler retained and used its invocation-scoped context.</summary>
    ContextExpired,
}
