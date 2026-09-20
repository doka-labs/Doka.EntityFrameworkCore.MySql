using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.MySql.FunctionalTests;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Microsoft.EntityFrameworkCore;

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class RuntimeMigrationMySqlTest(
    RuntimeMigrationMySqlTest.RuntimeMigrationMySqlFixture fixture
) : RuntimeMigrationTestBase<RuntimeMigrationMySqlTest.RuntimeMigrationMySqlFixture>(fixture)
{
    protected override Assembly ProviderAssembly => typeof(MySqlDesignTimeServices).Assembly;

    protected override List<string> GetTableNames(
        DbConnection connection
    )
    {
        var tables = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES "
            + "WHERE TABLE_SCHEMA = DATABASE() "
            + "AND TABLE_TYPE = 'BASE TABLE' "
            + "AND TABLE_NAME != '__EFMigrationsHistory'";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    public sealed class RuntimeMigrationMySqlFixture : RuntimeMigrationFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}
