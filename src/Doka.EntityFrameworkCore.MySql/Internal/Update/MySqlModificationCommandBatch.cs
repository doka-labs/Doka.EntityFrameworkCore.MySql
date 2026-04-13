namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// MySQL-specific modification command batch that uses EF Core's affected-count batch
/// with MySQL-specific parameter count limits.
///
/// AUTO_INCREMENT values for batched inserts are correlated via LAST_INSERT_ID():
/// MySQL returns the first auto-increment value of the batch, and subsequent values are
/// LAST_INSERT_ID() + 1, LAST_INSERT_ID() + 2, etc.
/// </summary>
internal sealed class MySqlModificationCommandBatch : AffectedCountModificationCommandBatch
{
    private const int DefaultMaxBatchSize = 1000;

    public MySqlModificationCommandBatch(
        ModificationCommandBatchFactoryDependencies dependencies,
        int maxBatchSize
    ) : base(dependencies, Math.Min(maxBatchSize, DefaultMaxBatchSize)) { }
}
