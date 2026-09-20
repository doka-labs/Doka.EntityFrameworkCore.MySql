using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class AdHocAdvancedMappingsQueryMySqlTest : AdHocAdvancedMappingsQueryRelationalTestBase
{
    public AdHocAdvancedMappingsQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory NonSharedTestStoreFactory => MySqlTestStoreFactory.Instance;

    public override async Task Query_generates_correct_datetime2_parameter_definition(
        int? fractionalSeconds,
        string postfix
    )
    {
        if (fractionalSeconds == 7)
        {
            await AssertInvalidTemporalPrecision(
                () => base.Query_generates_correct_datetime2_parameter_definition(fractionalSeconds, postfix));

            return;
        }

        await base.Query_generates_correct_datetime2_parameter_definition(fractionalSeconds, postfix);
    }

    public override async Task Query_generates_correct_timespan_parameter_definition(
        int? fractionalSeconds,
        string postfix
    )
    {
        if (fractionalSeconds == 7)
        {
            await AssertInvalidTemporalPrecision(
                () => base.Query_generates_correct_timespan_parameter_definition(fractionalSeconds, postfix));

            return;
        }

        await base.Query_generates_correct_timespan_parameter_definition(fractionalSeconds, postfix);
    }

    private static async Task AssertInvalidTemporalPrecision(
        Func<Task> action
    )
    {
        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(action);

        Assert.Equal("precision", exception.ParamName);
        Assert.Equal(7, exception.ActualValue);
    }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class AdHocNavigationsQueryMySqlTest : AdHocNavigationsQueryRelationalTestBase
{
    public AdHocNavigationsQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory NonSharedTestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class AdHocQueryFiltersQueryMySqlTest : AdHocQueryFiltersQueryRelationalTestBase
{
    public AdHocQueryFiltersQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory NonSharedTestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class EntitySplittingQueryMySqlTest : EntitySplittingQueryTestBase
{
    public EntitySplittingQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory NonSharedTestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class OperatorsProceduralQueryMySqlTest : OperatorsProceduralQueryTestBase
{
    public OperatorsProceduralQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory NonSharedTestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class OperatorsQueryMySqlTest : OperatorsQueryTestBase
{
    public OperatorsQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory NonSharedTestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ToSqlQueryMySqlTest : ToSqlQueryTestBase
{
    public ToSqlQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory NonSharedTestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class AdHocMiscellaneousQueryMySqlTest : AdHocMiscellaneousQueryRelationalTestBase
{
    public AdHocMiscellaneousQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory NonSharedTestStoreFactory => MySqlTestStoreFactory.Instance;

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

    protected override async Task Seed30915(
        Context30915 context
    )
    {
        context.Statuses.AddRange(
            new Context30915.PickupStatus30915
            {
                PickupStatusId = 1,
                Name = "Active",
            },
            new Context30915.PickupStatus30915
            {
                PickupStatusId = 2,
                Name = "NoRequests",
            },
            new Context30915.PickupStatus30915
            {
                PickupStatusId = 3,
                Name = "Busy",
            });

        context.Requests.AddRange(
            new Context30915.PickupRequest30915
            {
                PickupStatusId = 1,
                Priority = 5,
            },
            new Context30915.PickupRequest30915
            {
                PickupStatusId = 1,
                Priority = null,
            },
            new Context30915.PickupRequest30915
            {
                PickupStatusId = 3,
                Priority = 7,
            });

        await context.SaveChangesAsync();
    }

    public override async Task Multiple_different_entity_type_from_different_namespaces(
        bool async
    )
    {
        var contextFactory = await InitializeNonSharedTest<Context23981>();
        using var context = contextFactory.CreateDbContext();
        var query = context
            .Set<NameSpace1.TestQuery>()
            .FromSqlRaw("SELECT CAST(NULL AS SIGNED) AS `MyValue`");

        _ = async ? await query.ToListAsync() : query.ToList();
    }

    public override async Task Mapping_JsonElement_property_throws_a_meaningful_exception()
    {
        var contextFactory = await InitializeNonSharedTest<Context34752>();
        await using var context = contextFactory.CreateDbContext();
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

    protected override ITestStoreFactory NonSharedTestStoreFactory => MySqlTestStoreFactory.Instance;

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
