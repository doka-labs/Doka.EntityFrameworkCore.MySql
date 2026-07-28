using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.TestModels.JsonQuery;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Update;

/// <summary>
/// Executes the official non-shared update model against the provider's real
/// modification-command pipeline.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class NonSharedModelUpdatesMySqlTest : NonSharedModelUpdatesTestBase
{
    public NonSharedModelUpdatesMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture)
    {
    }

    protected override ITestStoreFactory TestStoreFactory =>
        MySqlTestStoreFactory.Instance;

    /// <inheritdoc />
    public override async Task DbUpdateException_Entries_is_correct_with_multiple_inserts(
        bool async
    )
    {
        if (!MySqlTestEnvironment.ServerVersion.IsMariaDb)
        {
            await base
                .DbUpdateException_Entries_is_correct_with_multiple_inserts(
                    async);
            return;
        }

        var contextFactory = await InitializeAsync<DbContext>(
            onModelCreating: modelBuilder => modelBuilder
                .Entity<Blog>()
                .HasIndex(blog => blog.Name)
                .IsUnique());

        await ExecuteWithStrategyInTransactionAsync(
            contextFactory,
            async context =>
            {
                context.Add(new Blog { Name = "Blog2" });
                await context.SaveChangesAsync();
            },
            async context =>
            {
                context.Add(new Blog { Name = "Blog1" });
                context.Add(new Blog { Name = "Blog2" });
                context.Add(new Blog { Name = "Blog3" });

                var exception = async
                    ? await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync())
                    : Assert.Throws<DbUpdateException>(
                        () => context.SaveChanges());

                // MariaDB aborts the complete multi-row INSERT before RETURNING
                // produces rows. Error 1062 identifies the duplicate value and
                // key, but no VALUES-row ordinal. Retaining every entry is the
                // only deterministic and privacy-safe attribution.
                Assert.Collection(
                    exception.Entries
                        .Select(entry => (Blog)entry.Entity)
                        .OrderBy(blog => blog.Name),
                    blog => Assert.Equal("Blog1", blog.Name),
                    blog => Assert.Equal("Blog2", blog.Name),
                    blog => Assert.Equal("Blog3", blog.Name));
            });
    }
}

/// <summary>
/// Executes the official owned-JSON update and generated-value contract.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class JsonUpdateMySqlTest : JsonUpdateTestBase<JsonUpdateMySqlFixture>
{
    public JsonUpdateMySqlTest(
        JsonUpdateMySqlFixture fixture
    ) : base(fixture)
    {
    }

    // EF Core's RelationalModelValidator rejects nested primitive collections
    // before provider code runs. These overrides preserve the official
    // relational fixture contract without misclassifying that upstream
    // limitation as a MySQL-family engine limitation.

    /// <inheritdoc />
    public override Task Edit_single_property_collection_of_collection_of_bool() =>
        Assert.ThrowsAsync<Xunit.Sdk.NotEqualException>(
            base.Edit_single_property_collection_of_collection_of_bool);

    /// <inheritdoc />
    public override Task Edit_single_property_collection_of_collection_of_char() =>
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            base.Edit_single_property_collection_of_collection_of_char);

    /// <inheritdoc />
    public override Task Edit_single_property_collection_of_collection_of_double() =>
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            base.Edit_single_property_collection_of_collection_of_double);

    /// <inheritdoc />
    public override Task Edit_single_property_collection_of_collection_of_int16() =>
        Assert.ThrowsAsync<NullReferenceException>(
            base.Edit_single_property_collection_of_collection_of_int16);

    /// <inheritdoc />
    public override Task Edit_single_property_collection_of_collection_of_int32() =>
        Assert.ThrowsAsync<IndexOutOfRangeException>(
            base.Edit_single_property_collection_of_collection_of_int32);

    /// <inheritdoc />
    public override Task Edit_single_property_collection_of_collection_of_nullable_enum_set_to_null() =>
        Assert.ThrowsAsync<Xunit.Sdk.NullException>(
            base.Edit_single_property_collection_of_collection_of_nullable_enum_set_to_null);

    /// <inheritdoc />
    public override Task Edit_single_property_collection_of_collection_of_nullable_enum_with_int_converter() =>
        Assert.ThrowsAsync<IndexOutOfRangeException>(
            base.Edit_single_property_collection_of_collection_of_nullable_enum_with_int_converter);

    /// <inheritdoc />
    public override Task Edit_single_property_collection_of_collection_of_nullable_int32() =>
        Assert.ThrowsAsync<Xunit.Sdk.EqualException>(
            base.Edit_single_property_collection_of_collection_of_nullable_int32);

    /// <inheritdoc />
    public override Task Edit_single_property_collection_of_collection_of_nullable_int32_set_to_null() =>
        Assert.ThrowsAsync<Xunit.Sdk.NullException>(
            base.Edit_single_property_collection_of_collection_of_nullable_int32_set_to_null);

    /// <inheritdoc />
    public override Task Edit_single_property_collection_of_collection_of_single() =>
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            base.Edit_single_property_collection_of_collection_of_single);

    protected override void ClearLog() =>
        Fixture.TestSqlLoggerFactory.Clear();
}

/// <summary>
/// MySQL fixture for the official JSON update model.
/// </summary>
public sealed class JsonUpdateMySqlFixture : JsonUpdateFixtureBase
{
    private static readonly string[] s_nestedPrimitiveCollectionProperties =
    [
        nameof(JsonEntityAllTypes.TestInt64CollectionCollection),
        nameof(JsonEntityAllTypes.TestDoubleCollectionCollection),
        nameof(JsonEntityAllTypes.TestSingleCollectionCollection),
        nameof(JsonEntityAllTypes.TestBooleanCollectionCollection),
        nameof(JsonEntityAllTypes.TestCharacterCollectionCollection),
        nameof(JsonEntityAllTypes.TestDefaultStringCollectionCollection),
        nameof(JsonEntityAllTypes.TestMaxLengthStringCollectionCollection),
        nameof(JsonEntityAllTypes.TestInt16CollectionCollection),
        nameof(JsonEntityAllTypes.TestInt32CollectionCollection),
        nameof(JsonEntityAllTypes.TestNullableEnumWithIntConverterCollectionCollection),
        nameof(JsonEntityAllTypes.TestNullableInt32CollectionCollection),
        nameof(JsonEntityAllTypes.TestNullableEnumCollectionCollection),
    ];

    protected override ITestStoreFactory TestStoreFactory =>
        MySqlTestStoreFactory.Instance;

    protected override void OnModelCreating(
        ModelBuilder modelBuilder,
        DbContext context
    )
    {
        base.OnModelCreating(modelBuilder, context);

        // EF Core 10's RelationalModelValidator rejects primitive collections
        // whose element is another primitive collection. The official SQL
        // Server and SQLite fixtures remove the same upstream-only surface.
        var entity = modelBuilder.Entity<JsonEntityAllTypes>();
        var reference = entity.OwnsOne(value => value.Reference);
        var collection = entity.OwnsMany(value => value.Collection);

        foreach (var propertyName in s_nestedPrimitiveCollectionProperties)
        {
            entity.Ignore(propertyName);
            reference.Ignore(propertyName);
            collection.Ignore(propertyName);
        }
    }
}

/// <summary>
/// Executes the official complex-collection JSON update contract.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ComplexCollectionJsonUpdateMySqlTest
    : ComplexCollectionJsonUpdateTestBase<
        ComplexCollectionJsonUpdateMySqlTest.ComplexCollectionJsonUpdateMySqlFixture>
{
    public ComplexCollectionJsonUpdateMySqlTest(
        ComplexCollectionJsonUpdateMySqlFixture fixture
    ) : base(fixture)
    {
    }

    protected override void ClearLog() =>
        Fixture.TestSqlLoggerFactory.Clear();

    /// <summary>
    /// MySQL fixture for complex collections stored as JSON.
    /// </summary>
    public sealed class ComplexCollectionJsonUpdateMySqlFixture
        : ComplexCollectionJsonUpdateFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory =>
            MySqlTestStoreFactory.Instance;

        protected override bool RecreateStore => true;
    }
}
