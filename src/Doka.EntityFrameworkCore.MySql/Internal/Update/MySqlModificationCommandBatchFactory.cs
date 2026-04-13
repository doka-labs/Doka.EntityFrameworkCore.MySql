namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    private const int DefaultMaxBatchSize = 1000;

    private readonly ModificationCommandBatchFactoryDependencies _dependencies;
    private readonly int _maxBatchSize;

    public MySqlModificationCommandBatchFactory(
        ModificationCommandBatchFactoryDependencies dependencies,
        IDbContextOptions options
    )
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

        var relationalOptions = RelationalOptionsExtension.Extract(options);
        _maxBatchSize = relationalOptions.MaxBatchSize ?? DefaultMaxBatchSize;
    }

    public ModificationCommandBatch Create() => new MySqlModificationCommandBatch(_dependencies, _maxBatchSize);
}
