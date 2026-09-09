namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Processes one exact custom <see cref="MigrationOperation"/> type and either
/// generates MySQL commands or explicitly consumes the operation without SQL.
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
    /// Processes the current operation and returns its complete result.
    /// </summary>
    /// <param name="context">The immutable provider generation context.</param>
    /// <returns>A generated command result or an explicit commandless result.</returns>
    MySqlMigrationOperationResult Generate(MySqlMigrationOperationContext context);
}
