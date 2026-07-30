using System.Reflection;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class AdHocAdvancedMappingsQueryMySqlTest : AdHocAdvancedMappingsQueryRelationalTestBase
{
    public AdHocAdvancedMappingsQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    [DirectTheory]
    [InlineData(null, "")]
    [InlineData(1, " (Scale = 1)")]
    [InlineData(2, " (Scale = 2)")]
    [InlineData(3, " (Scale = 3)")]
    [InlineData(4, " (Scale = 4)")]
    [InlineData(5, " (Scale = 5)")]
    [InlineData(6, " (Scale = 6)")]
    public override Task Query_generates_correct_datetime2_parameter_definition(
        int? fractionalSeconds,
        string postfix
    ) => base.Query_generates_correct_datetime2_parameter_definition(fractionalSeconds, postfix);

    [SpecEngineLimitationFact("MYSQL-MARIADB-TEMPORAL-MICROSECOND-PRECISION", "mysql84", "mariadb114", "mariadb118")]
    public Task Query_generates_correct_datetime2_parameter_definition_at_precision_7() =>
        base.Query_generates_correct_datetime2_parameter_definition(7, " (Scale = 7)");

    [DirectTheory]
    [InlineData(null, "")]
    [InlineData(1, " (Scale = 1)")]
    [InlineData(2, " (Scale = 2)")]
    [InlineData(3, " (Scale = 3)")]
    [InlineData(4, " (Scale = 4)")]
    [InlineData(5, " (Scale = 5)")]
    [InlineData(6, " (Scale = 6)")]
    public override Task Query_generates_correct_timespan_parameter_definition(
        int? fractionalSeconds,
        string postfix
    ) => base.Query_generates_correct_timespan_parameter_definition(fractionalSeconds, postfix);

    [SpecEngineLimitationFact("MYSQL-MARIADB-TEMPORAL-MICROSECOND-PRECISION", "mysql84", "mariadb114", "mariadb118")]
    public Task Query_generates_correct_timespan_parameter_definition_at_precision_7() =>
        base.Query_generates_correct_timespan_parameter_definition(7, " (Scale = 7)");
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class AdHocComplexTypeQueryMySqlTest : AdHocComplexTypeQueryRelationalTestBase
{
    public AdHocComplexTypeQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class AdHocNavigationsQueryMySqlTest : AdHocNavigationsQueryRelationalTestBase
{
    public AdHocNavigationsQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class AdHocQueryFiltersQueryMySqlTest : AdHocQueryFiltersQueryRelationalTestBase
{
    public AdHocQueryFiltersQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class EntitySplittingQueryMySqlTest : EntitySplittingQueryTestBase
{
    public EntitySplittingQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class OperatorsProceduralQueryMySqlTest : OperatorsProceduralQueryTestBase
{
    public OperatorsProceduralQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class OperatorsQueryMySqlTest : OperatorsQueryTestBase
{
    public OperatorsQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ToSqlQueryMySqlTest : ToSqlQueryTestBase
{
    public ToSqlQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class AdHocMiscellaneousQueryMySqlTest : AdHocMiscellaneousQueryRelationalTestBase
{
    public AdHocMiscellaneousQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    protected override DbContextOptionsBuilder SetParameterizedCollectionMode(
        DbContextOptionsBuilder optionsBuilder,
        ParameterTranslationMode parameterizedCollectionMode
    )
    {
        new MySqlDbContextOptionsBuilder(optionsBuilder).UseParameterizedCollectionMode(parameterizedCollectionMode);

        return optionsBuilder;
    }

    protected override async Task Seed2951(
        Context2951 context
    )
    {
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE `ZeroKey` (`Id` int NULL)");

        await context.Database.ExecuteSqlRawAsync("INSERT INTO `ZeroKey` (`Id`) VALUES (NULL)");
    }

    public override async Task Multiple_different_entity_type_from_different_namespaces(
        bool async
    )
    {
        var contextFactory = await InitializeAsync<Context23981>();
        using var context = contextFactory.CreateContext();
        var query = context
            .Set<NameSpace1.TestQuery>()
            .FromSqlRaw("SELECT CAST(NULL AS SIGNED) AS `MyValue`");

        _ = async ? await query.ToListAsync() : query.ToList();
    }

    public override async Task Mapping_JsonElement_property_throws_a_meaningful_exception()
    {
        var contextFactory = await InitializeAsync<Context34752>();
        await using var context = contextFactory.CreateContext();
        using var document = JsonDocument.Parse("""{"enabled":true}""");

        context.Entities.Add(
            new Context34752.Entity
            {
                Json = document.RootElement.Clone(),
            });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var entity = await context.Entities.SingleAsync();

        Assert.True(
            entity
                .Json.GetProperty("enabled")
                .GetBoolean());
    }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    NonSharedPrimitiveCollectionsQueryMySqlTest : NonSharedPrimitiveCollectionsQueryRelationalTestBase
{
    public NonSharedPrimitiveCollectionsQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    protected override DbContextOptionsBuilder SetParameterizedCollectionMode(
        DbContextOptionsBuilder optionsBuilder,
        ParameterTranslationMode parameterizedCollectionMode
    )
    {
        new MySqlDbContextOptionsBuilder(optionsBuilder).UseParameterizedCollectionMode(parameterizedCollectionMode);

        return optionsBuilder;
    }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class AdHocQuerySplittingQueryMySqlTest : AdHocQuerySplittingQueryTestBase
{
    private static readonly FieldInfo s_querySplittingBehaviorField =
        typeof(RelationalOptionsExtension).GetField(
            "_querySplittingBehavior",
            BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("EF Core no longer exposes the expected query-splitting backing field.");

    public AdHocQuerySplittingQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    protected override DbContextOptionsBuilder SetQuerySplittingBehavior(
        DbContextOptionsBuilder optionsBuilder,
        QuerySplittingBehavior splittingBehavior
    )
    {
        new MySqlDbContextOptionsBuilder(optionsBuilder).UseQuerySplittingBehavior(splittingBehavior);

        return optionsBuilder;
    }

    protected override DbContextOptionsBuilder ClearQuerySplittingBehavior(
        DbContextOptionsBuilder optionsBuilder
    )
    {
        var extension = optionsBuilder.Options.FindExtension<MySqlOptionsExtension>() ?? new MySqlOptionsExtension();

        // The contract verifies EF Core's unset behavior. EF Core has no public API
        // for removing this inherited option, so the test mirrors its own providers.
        s_querySplittingBehaviorField.SetValue(extension, null);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        return optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning));
    }
}
