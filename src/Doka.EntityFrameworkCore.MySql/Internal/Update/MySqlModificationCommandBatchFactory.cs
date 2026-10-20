namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Creates batches with the current context's connection and transaction boundary.
/// </summary>
internal sealed class MySqlModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    private const int DefaultMaxBatchSize = 1000;

    private readonly ModificationCommandBatchFactoryDependencies _dependencies;
    private readonly int _maxBatchSize;
    private readonly IRelationalConnection _connection;

    /// <summary>
    /// Captures scoped services used to evaluate transaction permission when a pending insert group is flushed.
    /// </summary>
    public MySqlModificationCommandBatchFactory(
        ModificationCommandBatchFactoryDependencies dependencies,
        IDbContextOptions options,
        IRelationalConnection connection
    )
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));

        var relationalOptions = RelationalOptionsExtension.Extract(options);
        _maxBatchSize = relationalOptions.MaxBatchSize ?? DefaultMaxBatchSize;
    }

    /// <inheritdoc />
    public ModificationCommandBatch Create() => new MySqlModificationCommandBatch(
        _dependencies,
        _maxBatchSize,
        _connection);
}
