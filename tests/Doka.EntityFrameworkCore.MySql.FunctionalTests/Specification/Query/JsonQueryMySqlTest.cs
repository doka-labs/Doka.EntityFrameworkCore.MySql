using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.JsonQuery;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

/// <summary>
/// JSON-query specification subclass. Exercises the EF Core JSON column query surface
/// (JSON_EXTRACT, JSON_TABLE on MySQL 8.x, JSON path navigation, JSON array indexing)
/// against the provider's <see cref="MySqlJsonTypeMapping"/> + JSON translator pipeline.
/// The provider supplies the JSON container column store-type ("json") via
/// <see cref="MySqlTypeMappingSource"/>'s <c>JsonTypePlaceholder</c> handler so EF Core's
/// owned-JSON entity stack can build the model without per-test column-type overrides.
/// The 12 nested-primitive-collection properties on the base JsonEntityAllTypes and
/// JsonOwnedAllTypes types remain Ignore()'d in the fixture because every relational
/// provider hits the upstream "Nested primitive collections are not yet supported"
/// validator surface; the per-property exclusion is the only safe disposition until EF
/// Core ships native nested-primitive-collection support
/// (see https://github.com/dotnet/efcore/issues/30713).
/// </summary>
[Trait("Category", "Spec")]
public class JsonQueryMySqlTest : JsonQueryRelationalTestBase<JsonQueryMySqlTest.JsonQueryMySqlFixture>
{
    public JsonQueryMySqlTest(
        JsonQueryMySqlFixture fixture
    ) : base(fixture) { }

    public class JsonQueryMySqlFixture : JsonQueryRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        protected override void OnModelCreating(
            ModelBuilder modelBuilder,
            DbContext context
        )
        {
            base.OnModelCreating(modelBuilder, context);

            string[] nestedCollectionProperties =
            [
                "TestBooleanCollectionCollection",
                "TestCharacterCollectionCollection",
                "TestDefaultStringCollectionCollection",
                "TestDoubleCollectionCollection",
                "TestInt16CollectionCollection",
                "TestInt32CollectionCollection",
                "TestInt64CollectionCollection",
                "TestMaxLengthStringCollectionCollection",
                "TestNullableEnumCollectionCollection",
                "TestNullableEnumWithIntConverterCollectionCollection",
                "TestNullableInt32CollectionCollection",
                "TestSingleCollectionCollection",
            ];

            modelBuilder.Entity<JsonEntityAllTypes>(b =>
            {
                foreach (var property in nestedCollectionProperties)
                {
                    b.Ignore(property);
                }
            });

            modelBuilder
                .Entity<JsonEntityAllTypes>()
                .OwnsOne(
                    x => x.Reference,
                    b =>
                    {
                        foreach (var property in nestedCollectionProperties)
                        {
                            b.Ignore(property);
                        }
                    });

            modelBuilder
                .Entity<JsonEntityAllTypes>()
                .OwnsMany(
                    x => x.Collection,
                    b =>
                    {
                        foreach (var property in nestedCollectionProperties)
                        {
                            b.Ignore(property);
                        }
                    });
        }
    }
}
