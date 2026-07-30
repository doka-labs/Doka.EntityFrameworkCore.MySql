using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Fixtures;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Applications;

/// <summary>
/// Runs the conference-planner application scenario through a real MySQL transaction.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ConferencePlannerMySqlTest : ConferencePlannerTestBase<ConferencePlannerMySqlTest.MySqlFixture>
{
    public ConferencePlannerMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    protected override void UseTransaction(
        DatabaseFacade facade,
        IDbContextTransaction transaction
    ) => facade.UseTransaction(transaction.GetDbTransaction());

    public sealed class MySqlFixture : ConferencePlannerFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}

/// <summary>
/// Verifies generated and explicit values in composite keys end-to-end.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    CompositeKeyEndToEndMySqlTest : CompositeKeyEndToEndTestBase<CompositeKeyEndToEndMySqlTest.MySqlFixture>
{
    public CompositeKeyEndToEndMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : CompositeKeyEndToEndFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}

/// <summary>
/// Exercises relationship fixup for a large notification-based object graph.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class MonsterFixupMySqlTest : MonsterFixupTestBase<MonsterFixupMySqlTest.MySqlFixture>
{
    public MonsterFixupMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : MonsterFixupChangedOnlyFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        protected override void OnModelCreating<TMessage, TProduct, TProductPhoto, TProductReview, TComputerDetail,
            TDimensions>(
            ModelBuilder builder
        )
        {
            base.OnModelCreating<TMessage, TProduct, TProductPhoto, TProductReview, TComputerDetail, TDimensions>(
                builder);

            builder
                .Entity<TMessage>()
                .HasKey(e => e.MessageId);
            builder
                .Entity<TProductPhoto>()
                .HasKey(e => e.PhotoId);
            builder
                .Entity<TProductReview>()
                .HasKey(e => e.ReviewId);
        }
    }
}

/// <summary>
/// Runs the Music Store application model and data-access scenario against MySQL.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class MusicStoreMySqlTest : MusicStoreTestBase<MusicStoreMySqlTest.MySqlFixture>
{
    public MusicStoreMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : MusicStoreFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}

/// <summary>
/// Verifies relationship fixup for notification entities.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    NotificationEntitiesMySqlTest : NotificationEntitiesTestBase<NotificationEntitiesMySqlTest.MySqlFixture>
{
    public NotificationEntitiesMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : NotificationEntitiesFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}

/// <summary>
/// Guards against provider service initialization changing unrelated EF Core behavior.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    OverzealousInitializationMySqlTest : OverzealousInitializationTestBase<
    OverzealousInitializationMySqlTest.MySqlFixture>
{
    public OverzealousInitializationMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : OverzealousInitializationFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}

/// <summary>
/// Verifies application data binding against the shared relational Formula 1 model.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class DataBindingMySqlTest : DataBindingTestBase<MySqlF1Fixture>
{
    public DataBindingMySqlTest(
        MySqlF1Fixture fixture
    ) : base(fixture) { }
}

/// <summary>
/// Verifies serialization behavior without provider-specific model divergence.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class SerializationMySqlTest : SerializationTestBase<MySqlF1Fixture>
{
    public SerializationMySqlTest(
        MySqlF1Fixture fixture
    ) : base(fixture) { }
}
