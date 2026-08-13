namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Generates MySQL migration commands for one exact custom
/// <see cref="MigrationOperation"/> type.
/// </summary>
/// <remarks>
/// Implementations are resolved from the scoped EF Core service provider. They
/// must be deterministic, synchronous, and free of database or network I/O.
/// A handler package should add its own <c>IDbContextOptionsExtension</c> and
/// register handlers from <c>ApplyServices</c> with <c>TryAddEnumerable</c>.
/// This places the registration in EF Core's internal service provider and
/// composes without replacing handlers from other packages.
/// </remarks>
public interface IMySqlMigrationOperationHandler
{
    /// <summary>
    /// Gets the stable, package-owned identifier used by diagnostics and
    /// registration-conflict messages.
    /// </summary>
    string HandlerId { get; }

    /// <summary>
    /// Gets the concrete custom operation type owned by this handler.
    /// Dispatch uses exact runtime-type equality and never base-type matching.
    /// </summary>
    Type OperationType { get; }

    /// <summary>
    /// Generates the complete command result for the current operation.
    /// </summary>
    /// <param name="context">The immutable provider generation context.</param>
    /// <returns>A complete, validated command result.</returns>
    MySqlMigrationOperationResult Generate(MySqlMigrationOperationContext context);
}
