using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Xunit.Abstractions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Conversion;

/// <summary>
/// Verifies conversions whose provider-facing CLR type differs from the model CLR type.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    ConvertToProviderTypesMySqlTest : ConvertToProviderTypesTestBase<ConvertToProviderTypesMySqlTest.MySqlFixture>
{
    public ConvertToProviderTypesMySqlTest(
        MySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        fixture.TestSqlLoggerFactory.Clear();
        fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public sealed class MySqlFixture : ConvertToProviderTypesFixtureBase, ITestSqlLoggerFactory
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

        public override bool StrictEquality => false;

        public override bool SupportsAnsi => false;

        public override bool SupportsUnicodeToAnsiConversion => false;

        public override bool SupportsLargeStringComparisons => true;

        public override bool SupportsDecimalComparisons => true;

        public override bool SupportsBinaryKeys => true;

        public override bool PreservesDateTimeKind => false;

        public override DateTime DefaultDateTime => new();
    }
}

/// <summary>
/// Exercises custom value converters across query, update, and materialization paths.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class CustomConvertersMySqlTest : CustomConvertersTestBase<CustomConvertersMySqlTest.MySqlFixture>
{
    public CustomConvertersMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public override void Collection_enum_as_string_Contains()
    {
        using var context = CreateContext();
        var role = Roles.Seller;

        var entity = Assert.Single(
            context
                .Set<CollectionEnum>()
                .Where(item => item.Roles.Contains(role)));

        Assert.Equal(1, entity.Id);
    }

    public sealed class MySqlFixture : CustomConvertersFixtureBase, ITestSqlLoggerFactory
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

        public override bool StrictEquality => false;

        public override bool SupportsAnsi => false;

        public override bool SupportsUnicodeToAnsiConversion => false;

        public override bool SupportsLargeStringComparisons => true;

        public override bool SupportsDecimalComparisons => true;

        public override bool SupportsBinaryKeys => true;

        public override bool PreservesDateTimeKind => false;

        public override DateTime DefaultDateTime => new();
    }
}

/// <summary>
/// Validates converted primary and foreign keys through EF Core's identity map.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class KeysWithConvertersMySqlTest : KeysWithConvertersTestBase<KeysWithConvertersMySqlTest.MySqlFixture>
{
    public KeysWithConvertersMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : KeysWithConvertersFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        public override DbContextOptionsBuilder AddOptions(
            DbContextOptionsBuilder builder
        ) => base
            .AddOptions(builder)
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.CollectionWithoutComparer));
    }
}

/// <summary>
/// Runs value-converter round trips end-to-end through relational storage.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    ValueConvertersEndToEndMySqlTest : ValueConvertersEndToEndTestBase<ValueConvertersEndToEndMySqlTest.MySqlFixture>
{
    public ValueConvertersEndToEndMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : ValueConvertersEndToEndFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}

/// <summary>
/// Verifies constructor binding and materialization for relational query results.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class WithConstructorsMySqlTest : WithConstructorsTestBase<WithConstructorsMySqlTest.MySqlFixture>
{
    public WithConstructorsMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    protected override void UseTransaction(
        DatabaseFacade facade,
        IDbContextTransaction transaction
    ) => facade.UseTransaction(transaction.GetDbTransaction());

    public sealed class MySqlFixture : WithConstructorsFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        protected override void OnModelCreating(
            ModelBuilder modelBuilder,
            DbContext context
        )
        {
            base.OnModelCreating(modelBuilder, context);

            modelBuilder
                .Entity<BlogQuery>()
                .HasNoKey()
                .ToSqlQuery("SELECT * FROM `Blog`");
        }
    }
}

/// <summary>
/// Executes field-backed model materialization with the provider's relational transaction.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class FieldMappingMySqlTest : FieldMappingTestBase<FieldMappingMySqlTest.MySqlFixture>
{
    public FieldMappingMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    protected override void UseTransaction(
        DatabaseFacade facade,
        IDbContextTransaction transaction
    ) => facade.UseTransaction(transaction.GetDbTransaction());

    public sealed class MySqlFixture : FieldMappingFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}

/// <summary>
/// Verifies that malformed JSON is rejected during provider materialization.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BadDataJsonDeserializationMySqlTest : BadDataJsonDeserializationTestBase
{
    protected override void OnConfiguring(
        DbContextOptionsBuilder optionsBuilder
    ) => base.OnConfiguring(MySqlTestHelpers.Instance.UseProviderOptions(optionsBuilder));
}
