using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.TestModels.ManyToManyModel;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Loading;

/// <summary>
/// Exercises field-only navigation loading and relationship fixup.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class FieldsOnlyLoadMySqlTest : FieldsOnlyLoadTestBase<FieldsOnlyLoadMySqlTest.MySqlFixture>
{
    public FieldsOnlyLoadMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : FieldsOnlyLoadFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}

/// <summary>
/// Verifies primary-key lookup through the generic DbSet API.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class FindMySqlTest : FindTestBase<FindMySqlTest.MySqlFixture>
{
    public FindMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    protected override TestFinder Finder { get; } = new FindViaSetFinder();

    public sealed class MySqlFixture : FindFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}

/// <summary>
/// Executes explicit navigation loading for reference and collection relationships.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class LoadMySqlTest : LoadTestBase<LoadMySqlTest.MySqlFixture>
{
    public LoadMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : LoadFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}

/// <summary>
/// Exercises lazy-loading proxies through the relational provider pipeline.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class LazyLoadProxyMySqlTest : LazyLoadProxyRelationalTestBase<LazyLoadProxyMySqlTest.MySqlFixture>
{
    public LazyLoadProxyMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : LoadRelationalFixtureBase
    {
        public override DbContextOptionsBuilder AddOptions(
            DbContextOptionsBuilder builder
        ) => base.AddOptions(builder.UseLazyLoadingProxies());

        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}

/// <summary>
/// Verifies explicit loading for field-backed many-to-many navigations.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    ManyToManyFieldsLoadMySqlTest : ManyToManyFieldsLoadTestBase<ManyToManyFieldsLoadMySqlTest.MySqlFixture>
{
    public ManyToManyFieldsLoadMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : ManyToManyFieldsLoadFixtureBase, ITestSqlLoggerFactory
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override void OnModelCreating(
            ModelBuilder modelBuilder,
            DbContext context
        )
        {
            base.OnModelCreating(modelBuilder, context);

            modelBuilder
                .Entity<Microsoft.EntityFrameworkCore.TestModels.ManyToManyFieldsModel.JoinOneSelfPayload>()
                .Property(e => e.Payload)
                .HasDefaultValueSql("UTC_TIMESTAMP()");

            modelBuilder
                .SharedTypeEntity<Dictionary<string, object>>("JoinOneToThreePayloadFullShared")
                .IndexerProperty<string>("Payload")
                .HasDefaultValue("Generated");

            modelBuilder
                .Entity<Microsoft.EntityFrameworkCore.TestModels.ManyToManyFieldsModel.JoinOneToThreePayloadFull>()
                .Property(e => e.Payload)
                .HasDefaultValue("Generated");
        }
    }
}

/// <summary>
/// Verifies explicit loading for conventional and unidirectional many-to-many navigations.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ManyToManyLoadMySqlTest : ManyToManyLoadTestBase<ManyToManyLoadMySqlTest.MySqlFixture>
{
    public ManyToManyLoadMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : ManyToManyLoadFixtureBase, ITestSqlLoggerFactory
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override void OnModelCreating(
            ModelBuilder modelBuilder,
            DbContext context
        )
        {
            base.OnModelCreating(modelBuilder, context);

            ConfigurePayloadDefaults<JoinOneSelfPayload>(modelBuilder);
            ConfigurePayloadDefaults<UnidirectionalJoinOneSelfPayload>(modelBuilder);

            modelBuilder
                .SharedTypeEntity<Dictionary<string, object>>("JoinOneToThreePayloadFullShared")
                .IndexerProperty<string>("Payload")
                .HasDefaultValue("Generated");

            modelBuilder
                .SharedTypeEntity<Dictionary<string, object>>("UnidirectionalJoinOneToThreePayloadFullShared")
                .IndexerProperty<string>("Payload")
                .HasDefaultValue("Generated");

            modelBuilder
                .Entity<JoinOneToThreePayloadFull>()
                .Property(e => e.Payload)
                .HasDefaultValue("Generated");

            modelBuilder
                .Entity<UnidirectionalJoinOneToThreePayloadFull>()
                .Property(e => e.Payload)
                .HasDefaultValue("Generated");
        }

        private static void ConfigurePayloadDefaults<TEntity>(
            ModelBuilder modelBuilder
        )
            where TEntity : class
        {
            modelBuilder
                .Entity<TEntity>()
                .Property<DateTime>("Payload")
                .HasDefaultValueSql("UTC_TIMESTAMP()");
        }
    }
}
