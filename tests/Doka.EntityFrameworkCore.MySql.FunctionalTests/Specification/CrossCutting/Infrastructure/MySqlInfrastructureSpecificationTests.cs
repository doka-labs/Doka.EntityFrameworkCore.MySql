using System.Reflection;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Infrastructure;

/// <summary>
/// Applies EF Core's public API consistency rules to the provider assembly.
/// </summary>
[Trait("Category", "Spec")]
public sealed class MySqlApiConsistencyTest : ApiConsistencyTestBase<MySqlApiConsistencyTest.MySqlFixture>
{
    public MySqlApiConsistencyTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    protected override Assembly TargetAssembly => typeof(MySqlDbContextOptionsBuilder).Assembly;

    protected override void AddServices(
        ServiceCollection serviceCollection
    ) => serviceCollection.AddEntityFrameworkDokaMySql();

    public sealed class MySqlFixture : ApiConsistencyFixtureBase
    {
        public override HashSet<Type> FluentApiTypes { get; } =
        [
            typeof(MySqlDbContextOptionsBuilderExtensions),
            typeof(MySqlEntityTypeBuilderExtensions),
            typeof(MySqlIndexBuilderExtensions),
            typeof(MySqlModelBuilderExtensions),
            typeof(MySqlPropertyBuilderExtensions),
            typeof(MySqlServiceCollectionExtensions),
        ];
    }
}

/// <summary>
/// Verifies that connection strings and connections can be switched between two databases.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TwoDatabasesMySqlTest : TwoDatabasesTestBase, IClassFixture<TwoDatabasesMySqlTest.MySqlFixture>
{
    private static readonly string s_dummyConnectionString = new MySqlConnectionStringBuilder
    {
        Server = "localhost",
        UserID = "root",
        Database = "DokaDummy",
        // The provider supplies bounded driver defaults before opening a
        // provider-owned connection. Declaring them here keeps the shared EF
        // test focused on interceptor-driven database switching.
        ApplicationName = MySqlDiagnostics.DefaultDriverPoolName,
        GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
    }.ConnectionString;

    public TwoDatabasesMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    private MySqlFixture TypedFixture => (MySqlFixture)Fixture;

    protected override DbContextOptionsBuilder CreateTestOptions(
        DbContextOptionsBuilder optionsBuilder,
        bool withConnectionString = false,
        bool withNullConnectionString = false
    )
    {
        if (!withConnectionString)
        {
            return optionsBuilder.UseMySql(new MySqlConnection(), MySqlTestStore.ServerVersion);
        }

        return optionsBuilder.UseMySql(
            withNullConnectionString ? null! : DummyConnectionString,
            MySqlTestStore.ServerVersion);
    }

    protected override TwoDatabasesWithDataContext CreateBackingContext(
        string databaseName
    ) => new(TypedFixture.CreateOptions(MySqlTestStore.Create(databaseName)));

    protected override string DummyConnectionString => s_dummyConnectionString;

    public sealed class MySqlFixture : ServiceProviderFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}

/// <summary>
/// Exercises model seeding without leaking seeded entities into the context state manager.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class SeedingMySqlTest : SeedingTestBase
{
    protected override TestStore TestStore => MySqlTestStore.Create("SeedingTest");

    protected override SeedingContext CreateContextWithEmptyDatabase(
        string testId
    ) => new MySqlSeedingContext(testId);

    private sealed class MySqlSeedingContext : SeedingContext
    {
        public MySqlSeedingContext(
            string testId
        ) : base(testId) { }

        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        )
        {
            var connectionString =
                new MySqlConnectionStringBuilder(MySqlTestEnvironment.ConnectionString)
                {
                    Database = $"Seeds{TestId}",
                }.ConnectionString;

            optionsBuilder.UseMySql(connectionString, MySqlTestStore.ServerVersion);
        }
    }
}

/// <summary>
/// Verifies the provider's dependency-injection registrations through the shared fixture.
/// </summary>
[Trait("Category", "Spec")]
public sealed class MySqlProviderServiceRegistrationTest : EntityFrameworkServiceCollectionExtensionsTestBase
{
    public MySqlProviderServiceRegistrationTest() : base(MySqlTestHelpers.Instance) { }
}
