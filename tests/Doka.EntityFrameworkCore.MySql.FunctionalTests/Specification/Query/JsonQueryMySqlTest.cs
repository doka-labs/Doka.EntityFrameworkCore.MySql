using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

        /// <summary>
        /// Downgrades two EF Core query-compilation warnings from the spec-test-base's
        /// <c>Default(Throw)</c> to <c>Log</c>, scoped to this fixture only. The provider's
        /// runtime configuration is unchanged -- production users see whatever
        /// <c>WarningBehavior</c> they configure (EF Core default: Log).
        /// <para>
        /// Both warnings are upstream false positives on the EF Core 8+ JSON owned-entity
        /// projection path: every <see cref="MySqlJsonTableExpression"/>-backed collection
        /// projection increments <c>_collectionId</c> inside
        /// <c>RelationalShapedQueryCompilingExpressionVisitor.ShaperProcessingExpressionVisitor</c>,
        /// triggering <c>MultipleCollectionIncludeWarning</c> as soon as two JSON collections
        /// are co-projected. <c>DistinctAfterOrderByWithoutRowLimitingOperatorWarning</c>
        /// hits the same shape when <c>.Distinct()</c> composes over a JSON_TABLE source.
        /// </para>
        /// <para>
        /// Cross-provider empirical verification (SqlServer 10.0.4 + SQL Server 2022 Docker)
        /// confirmed the same shape throws under the spec-test base's <c>Default(Throw)</c>
        /// configuration -- no mainstream provider has a clean translator-side bypass.
        /// SqlServer and Npgsql have the same gap; SQLite sidesteps it via an unrelated
        /// <c>ApplyNotSupported</c> earlier error.
        /// </para>
        /// <para>
        /// See ADR D-015 (<c>docs/decisions/D-015-json-query-warning-suppression.md</c>) for
        /// the full empirical record, the cross-provider table, and the trigger predicate
        /// for re-evaluation when <c>dotnet/efcore</c> issue
        /// <see href="https://github.com/dotnet/efcore/issues/29665">#29665</see> closes.
        /// </para>
        /// </summary>
        public override DbContextOptionsBuilder AddOptions(
            DbContextOptionsBuilder builder
        ) => base
            .AddOptions(builder)
            .ConfigureWarnings(w => w
                .Log(CoreEventId.DistinctAfterOrderByWithoutRowLimitingOperatorWarning)
                .Log(RelationalEventId.MultipleCollectionIncludeWarning));

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
