namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Test-store factory the EF Core specification suite calls into. Routes every
/// <c>GetOrCreate</c> / <c>Create</c> request through <see cref="MySqlTestStore"/> and registers
/// the provider's services on the test infrastructure's DI container.
/// </summary>
public class MySqlTestStoreFactory : RelationalTestStoreFactory
{
    public static MySqlTestStoreFactory Instance { get; } = new();

    protected MySqlTestStoreFactory()
    {
    }

    public override TestStore Create(
        string storeName
    ) => MySqlTestStore.Create(storeName);

    public override TestStore GetOrCreate(
        string storeName
    ) => MySqlTestStore.GetOrCreate(storeName);

    public override IServiceCollection AddProviderServices(
        IServiceCollection serviceCollection
    ) => serviceCollection
        .AddEntityFrameworkDokaMySql()
        .AddEntityFrameworkDokaMySqlNetTopologySuite();
}
