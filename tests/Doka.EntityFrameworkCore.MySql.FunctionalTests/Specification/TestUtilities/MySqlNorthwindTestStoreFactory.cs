namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Northwind-fixture test-store factory. Pins the database name so the spec suite reuses the same
/// schema across query-shape variants rather than creating one per fixture instance, which keeps
/// the Northwind seed cost amortized across the suite.
/// </summary>
public class MySqlNorthwindTestStoreFactory : MySqlTestStoreFactory
{
    public const string DatabaseName = "Northwind";

    public static new MySqlNorthwindTestStoreFactory Instance { get; } = new();

    protected MySqlNorthwindTestStoreFactory()
    {
    }

    public override TestStore GetOrCreate(
        string storeName
    ) => MySqlTestStore.GetOrCreate(DatabaseName);
}

/// <summary>
/// Isolates the bulk-update Northwind model from the query model.
/// </summary>
public sealed class MySqlNorthwindBulkUpdatesTestStoreFactory
    : MySqlNorthwindTestStoreFactory
{
    private const string BulkDatabaseName = "NorthwindBulkUpdates";

    public static new MySqlNorthwindBulkUpdatesTestStoreFactory Instance { get; } =
        new();

    private MySqlNorthwindBulkUpdatesTestStoreFactory()
    {
    }

    public override TestStore GetOrCreate(
        string storeName
    ) => MySqlTestStore.GetOrCreate(BulkDatabaseName);
}
