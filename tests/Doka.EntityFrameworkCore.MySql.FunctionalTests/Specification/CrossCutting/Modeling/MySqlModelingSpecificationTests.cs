using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.TestModels.TransportationModel;
using Xunit.Abstractions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Modeling;

/// <summary>
/// Runs relational data-annotation conventions and validation against MySQL metadata.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class DataAnnotationMySqlTest : DataAnnotationRelationalTestBase<DataAnnotationMySqlTest.MySqlFixture>
{
    public DataAnnotationMySqlTest(
        MySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        fixture.TestSqlLoggerFactory.Clear();
        fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    protected override TestHelpers TestHelpers => MySqlTestHelpers.Instance;

    protected override void UseTransaction(
        DatabaseFacade facade,
        IDbContextTransaction transaction
    ) => facade.UseTransaction(transaction.GetDbTransaction());

    public sealed class MySqlFixture : DataAnnotationRelationalFixtureBase, ITestSqlLoggerFactory
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;
    }
}

/// <summary>
/// Exercises the provider-independent model-building examples through MySQL options.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ModelBuilding101MySqlTest : ModelBuilding101RelationalTestBase
{
    protected override DbContextOptionsBuilder ConfigureContext(
        DbContextOptionsBuilder optionsBuilder
    ) => MySqlTestHelpers.Instance.UseProviderOptions(optionsBuilder);
}

/// <summary>
/// Validates entity splitting across multiple MySQL tables.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class EntitySplittingMySqlTest : EntitySplittingTestBase
{
    public EntitySplittingMySqlTest(
        NonSharedFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

/// <summary>
/// Validates relational table splitting and shared-row update behavior.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TableSplittingMySqlTest : TableSplittingTestBase
{
    public TableSplittingMySqlTest(
        NonSharedFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder
            .Entity<Engine>()
            .ToTable("Vehicles")
            .Property(e => e.Computed)
            .HasComputedColumnSql("1");
    }
}

/// <summary>
/// Validates table-per-type inheritance when owned dependents share table rows.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TptTableSplittingMySqlTest : TPTTableSplittingTestBase
{
    private const string OneParentInapplicableReason =
        "[spec-not-applicable:EFCORE-TPT-DEPENDENT-ONE-PARENT-NOT-APPLICABLE] "
        + "EF Core's official SQL Server TPT suite identifies this scenario as invalid for TPT. "
        + "See SpecDispositions.json.";

    public TptTableSplittingMySqlTest(
        NonSharedFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    [Fact(Skip = OneParentInapplicableReason)]
    public override Task Can_insert_dependent_with_just_one_parent() =>
        base.Can_insert_dependent_with_just_one_parent();
}

/// <summary>
/// Verifies that the provider's service-registration surface satisfies EF Core's
/// relational dependency-injection contract.
/// </summary>
[Trait("Category", "Spec")]
public sealed class MySqlServiceCollectionExtensionsTest : RelationalServiceCollectionExtensionsTestBase
{
    public MySqlServiceCollectionExtensionsTest() : base(MySqlTestHelpers.Instance) { }
}
