using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Fixtures;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.TestModels.ManyToManyModel;
using Xunit.Abstractions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Tracking;

/// <summary>
/// Runs the relational complex-type change-tracking contract against MySQL.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    ComplexTypesTrackingMySqlTest : ComplexTypesTrackingRelationalTestBase<ComplexTypesTrackingMySqlTest.MySqlFixture>
{
    public ComplexTypesTrackingMySqlTest(
        MySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }

    public sealed class MySqlFixture : RelationalFixtureBase, ITestSqlLoggerFactory
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}

/// <summary>
/// Runs EF Core's many-to-many state-manager contract with MySQL-generated payload values.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    ManyToManyTrackingMySqlTest : ManyToManyTrackingRelationalTestBase<ManyToManyTrackingMySqlTest.MySqlFixture>
{
    public ManyToManyTrackingMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : ManyToManyTrackingRelationalFixture, ITestSqlLoggerFactory
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

/// <summary>
/// Executes EF Core's optimistic-concurrency contract with binary row versions.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class OptimisticConcurrencyMySqlTest : OptimisticConcurrencyRelationalTestBase<MySqlF1Fixture, byte[]>
{
    public OptimisticConcurrencyMySqlTest(
        MySqlF1Fixture fixture
    ) : base(fixture) { }

    protected override void UseTransaction(
        DatabaseFacade facade,
        IDbContextTransaction transaction
    ) => facade.UseTransaction(transaction.GetDbTransaction());
}

/// <summary>
/// Runs current, original, and database property-value semantics against MySQL.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class PropertyValuesMySqlTest : PropertyValuesRelationalTestBase<PropertyValuesMySqlTest.MySqlFixture>
{
    public PropertyValuesMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : PropertyValuesRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}

/// <summary>
/// Runs graph updates with snapshot change tracking and relational transaction sharing.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class GraphUpdatesMySqlTest : GraphUpdatesTestBase<GraphUpdatesMySqlTest.MySqlFixture>
{
    public GraphUpdatesMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    protected override void UseTransaction(
        DatabaseFacade facade,
        IDbContextTransaction transaction
    ) => facade.UseTransaction(transaction.GetDbTransaction());

    public sealed class MySqlFixture : GraphUpdatesFixtureBase
    {
        protected override string StoreName => "GraphUpdatesSnapshotTest";

        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        protected override void OnModelCreating(
            ModelBuilder modelBuilder,
            DbContext context
        )
        {
            modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.Snapshot);

            base.OnModelCreating(modelBuilder, context);

            modelBuilder.Entity<AccessState>(builder =>
            {
                builder
                    .Property(entity => entity.AccessStateId)
                    .ValueGeneratedNever();

                builder.HasData(new AccessState { AccessStateId = 1 });
            });

            modelBuilder.Entity<Cruiser>(builder =>
            {
                builder
                    .Property(entity => entity.IdUserState)
                    .HasDefaultValue(1);

                builder
                    .HasOne(entity => entity.UserState)
                    .WithMany(entity => entity.Users)
                    .HasForeignKey(entity => entity.IdUserState);
            });

            modelBuilder.Entity<AccessStateWithSentinel>(builder =>
            {
                builder
                    .Property(entity => entity.AccessStateWithSentinelId)
                    .ValueGeneratedNever();

                builder.HasData(new AccessStateWithSentinel { AccessStateWithSentinelId = 1 });
            });

            modelBuilder.Entity<CruiserWithSentinel>(builder =>
            {
                builder
                    .Property(entity => entity.IdUserState)
                    .HasDefaultValue(1)
                    .HasSentinel(667);

                builder
                    .HasOne(entity => entity.UserState)
                    .WithMany(entity => entity.Users)
                    .HasForeignKey(entity => entity.IdUserState);
            });

            modelBuilder
                .Entity<SomethingOfCategoryA>()
                .Property<int>("CategoryId")
                .HasDefaultValue(1);

            modelBuilder
                .Entity<SomethingOfCategoryB>()
                .Property(entity => entity.CategoryId)
                .HasDefaultValue(2);

            modelBuilder
                .Entity<CompositeKeyWith<int>>()
                .Property(entity => entity.PrimaryGroup)
                .HasDefaultValue(1)
                .HasSentinel(1);

            modelBuilder
                .Entity<CompositeKeyWith<bool>>()
                .Property(entity => entity.PrimaryGroup)
                .HasDefaultValue(true);

            modelBuilder
                .Entity<CompositeKeyWith<bool?>>()
                .Property(entity => entity.PrimaryGroup)
                .HasDefaultValue(true);

            modelBuilder
                .Entity<OwnerWithKeyedCollection>()
                .OwnsMany(entity => entity.OwnedCollectionPrivateKey, owned => owned.HasKey("PrivateKey"));

            modelBuilder.Entity<OwnerRoot>(builder =>
            {
                builder.OwnsMany(
                    entity => entity.OptionalChildren,
                    owned =>
                    {
                        owned.HasKey("Id");
                        owned
                            .OwnsMany(entity => entity.Children)
                            .HasKey("Id");
                    });

                builder.OwnsMany(
                    entity => entity.RequiredChildren,
                    owned =>
                    {
                        owned.HasKey("Id");
                        owned
                            .OwnsMany(entity => entity.Children)
                            .HasKey("Id");
                    });
            });
        }
    }
}

/// <summary>
/// Runs graph updates with change-tracking and lazy-loading proxies enabled together.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ProxyGraphUpdatesMySqlTest : ProxyGraphUpdatesTestBase<ProxyGraphUpdatesMySqlTest.MySqlFixture>
{
    public ProxyGraphUpdatesMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    protected override bool DoesLazyLoading => true;

    protected override bool DoesChangeTracking => true;

    protected override void UseTransaction(
        DatabaseFacade facade,
        IDbContextTransaction transaction
    ) => facade.UseTransaction(transaction.GetDbTransaction());

    public sealed class MySqlFixture : ProxyGraphUpdatesFixtureBase
    {
        protected override string StoreName => "ProxyGraphChangeTrackingAndLazyLoadingUpdatesTest";

        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        public override DbContextOptionsBuilder AddOptions(
            DbContextOptionsBuilder builder
        ) => base.AddOptions(
            builder
                .UseChangeTrackingProxies()
                .UseLazyLoadingProxies());

        protected override IServiceCollection AddServices(
            IServiceCollection serviceCollection
        ) => base.AddServices(serviceCollection.AddEntityFrameworkProxies());
    }
}
