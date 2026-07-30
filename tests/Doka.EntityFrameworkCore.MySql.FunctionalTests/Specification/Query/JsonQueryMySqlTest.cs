using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
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
[Collection(FunctionalDatabaseTestGroup.Name)]
public class JsonQueryMySqlTest : JsonQueryRelationalTestBase<JsonQueryMySqlTest.JsonQueryMySqlFixture>
{
    public JsonQueryMySqlTest(
        JsonQueryMySqlFixture fixture
    ) : base(fixture) { }

    /// <summary>
    /// MySQL bug #114897: JSON_TABLE inside EXISTS with a nested correlated JSON_TABLE
    /// COUNT subquery returns zero rows on MySQL 8.x. The provider rewrites the EXISTS
    /// expression as a limited scalar subquery, which preserves the test semantics without
    /// entering the affected semijoin optimizer path.
    /// </summary>
    public override Task Json_collection_within_collection_Count(
        bool async
    ) => base.Json_collection_within_collection_Count(async);

    /// <summary>
    /// Executes the relational base assertion with directly declared async data. EF Core
    /// redeclares inheritable test data on two base levels for this method; direct discovery
    /// prevents duplicate IDs without changing the assertion.
    /// </summary>
    [DirectTheory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Project_json_reference_in_tracking_query_fails(
        bool async
    ) => base.Project_json_reference_in_tracking_query_fails(async);

    /// <summary>
    /// Executes the relational collection-tracking assertion once per async mode while
    /// excluding duplicate inherited data rows.
    /// </summary>
    [DirectTheory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Project_json_collection_in_tracking_query_fails(
        bool async
    ) => base.Project_json_collection_in_tracking_query_fails(async);

    /// <summary>
    /// Executes the relational owner-present tracking assertion once per async mode while
    /// excluding duplicate inherited data rows.
    /// </summary>
    [DirectTheory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Project_json_entity_in_tracking_query_fails_even_when_owner_is_present(
        bool async
    ) => base.Project_json_entity_in_tracking_query_fails_even_when_owner_is_present(async);

    /// <summary>
    /// Activates the upstream-skipped distinct anonymous JSON projection so provider support
    /// is verified rather than inferred from the referenced EF Core issue.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-31397")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_collection_anonymous_projection_distinct_in_projection(
        bool async
    ) => base.Json_collection_anonymous_projection_distinct_in_projection(async);

    /// <summary>
    /// Activates the upstream-skipped JSON scalar grouping and ordered FirstOrDefault shape.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-29287")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Group_by_json_scalar_Orderby_json_scalar_FirstOrDefault(
        bool async
    ) => base.Group_by_json_scalar_Orderby_json_scalar_FirstOrDefault(async);

    /// <summary>
    /// Activates the upstream-skipped JSON FirstOrDefault entity comparison shape.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-28733")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Project_json_entity_FirstOrDefault_subquery_with_entity_comparison_on_top(
        bool async
    ) => base.Project_json_entity_FirstOrDefault_subquery_with_entity_comparison_on_top(async);

    /// <summary>
    /// Activates the upstream-skipped JSON parent backtracking projection.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-28645")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_entity_backtracking(
        bool async
    ) => base.Json_entity_backtracking(async);

    /// <summary>
    /// Activates the upstream-skipped single-pushdown JSON anonymous projection.
    /// </summary>
    [DirectTheory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_subquery_reference_pushdown_reference_anonymous_projection(
        bool async
    ) => base.Json_subquery_reference_pushdown_reference_anonymous_projection(async);

    /// <summary>
    /// Activates the upstream-skipped double-pushdown JSON anonymous projection.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-24263")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_subquery_reference_pushdown_reference_pushdown_anonymous_projection(
        bool async
    ) => base.Json_subquery_reference_pushdown_reference_pushdown_anonymous_projection(async);

    /// <summary>
    /// Activates the upstream-skipped nullable enum converter predicate with null handling.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-29416")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_predicate_on_nullableenumwithconverterthathandlesnulls2(
        bool async
    ) => base.Json_predicate_on_nullableenumwithconverterthathandlesnulls2(async);

    // MariaDB cannot correlate a FROM-subquery with an outer query and its JOIN grammar
    // has no LATERAL derived-table form. The attributes below turn that engine boundary
    // into visible xUnit skips linked to the primary-source-backed disposition ledger.

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_collection_Distinct_Count_with_predicate(
        bool async
    ) => base.Json_collection_Distinct_Count_with_predicate(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_collection_OrderByDescending_Skip_ElementAt(
        bool async
    ) => base.Json_collection_OrderByDescending_Skip_ElementAt(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_collection_Skip(
        bool async
    ) => base.Json_collection_Skip(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_collection_Select_entity_in_anonymous_object_ElementAt(
        bool async
    ) => base.Json_collection_Select_entity_in_anonymous_object_ElementAt(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_collection_Select_entity_with_initializer_ElementAt(
        bool async
    ) => base.Json_collection_Select_entity_with_initializer_ElementAt(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_collection_skip_take_in_projection(
        bool async
    ) => base.Json_collection_skip_take_in_projection(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_collection_skip_take_in_projection_project_into_anonymous_type(
        bool async
    ) => base.Json_collection_skip_take_in_projection_project_into_anonymous_type(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_collection_skip_take_in_projection_with_json_reference_access_as_final_operation(
        bool async
    ) => base.Json_collection_skip_take_in_projection_with_json_reference_access_as_final_operation(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_collection_distinct_in_projection(
        bool async
    ) => base.Json_collection_distinct_in_projection(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_branch_collection_distinct_and_other_collection(
        bool async
    ) => base.Json_branch_collection_distinct_and_other_collection(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_leaf_collection_distinct_and_other_collection(
        bool async
    ) => base.Json_leaf_collection_distinct_and_other_collection(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_collection_filter_in_projection(
        bool async
    ) => base.Json_collection_filter_in_projection(async);

    /// <summary>
    /// Keeps the leaf-filter projection active on every supported target. The current
    /// relational tree no longer requires a correlated derived-table boundary.
    /// </summary>
    [DirectTheory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_collection_leaf_filter_in_projection(
        bool async
    ) => base.Json_collection_leaf_filter_in_projection(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_collection_in_projection_with_composition_where_and_anonymous_projection_of_primitive_arrays(
        bool async
    ) => base.Json_collection_in_projection_with_composition_where_and_anonymous_projection_of_primitive_arrays(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_collection_in_projection_with_composition_where_and_anonymous_projection_of_scalars(
        bool async
    ) => base.Json_collection_in_projection_with_composition_where_and_anonymous_projection_of_scalars(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_multiple_collection_projections(
        bool async
    ) => base.Json_multiple_collection_projections(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_nested_collection_anonymous_projection_in_projection(
        bool async
    ) => base.Json_nested_collection_anonymous_projection_in_projection(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_nested_collection_anonymous_projection_of_primitives_in_projection_NoTrackingWithIdentityResolution(
        bool async
    ) => base.Json_nested_collection_anonymous_projection_of_primitives_in_projection_NoTrackingWithIdentityResolution(async);

    [SpecEngineLimitationTheory(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Json_nested_collection_filter_in_projection(
        bool async
    ) => base.Json_nested_collection_filter_in_projection(async);

    [SpecEngineLimitationTheory(
        "MDB-JSON-TABLE-SUBDOCUMENT",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Custom_naming_projection_everything(
        bool async
    ) => base.Custom_naming_projection_everything(async);

    [SpecEngineLimitationTheory(
        "MDB-JSON-TABLE-SUBDOCUMENT",
        "mariadb114",
        "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Custom_naming_projection_owned_scalar(
        bool async
    ) => base.Custom_naming_projection_owned_scalar(async);

    /// <summary>
    /// The base spec test projects through a non-translatable C# helper
    /// (<c>MyMethod(x.Id)</c>) inside a JSON-collection indexer; EF Core 10 rejects
    /// the LINQ with <c>InvalidOperationException</c> carrying
    /// <c>CoreStrings.QueryUnableToTranslateMethod(...)</c>. The override mirrors
    /// SqlServer's <c>JsonQuerySqlServerTest</c> shape: invoke the base test,
    /// assert the expected throw, and assert the message contains the canonical
    /// "MyMethod" cannot-be-translated token.
    /// </summary>
    public override async Task Json_collection_index_in_projection_using_untranslatable_client_method(
        bool async
    )
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => base.Json_collection_index_in_projection_using_untranslatable_client_method(async));
        Assert.Contains("MyMethod", ex.Message);
        Assert.Contains("could not be translated", ex.Message);
    }

    /// <summary>
    /// Sibling test of <see cref="Json_collection_index_in_projection_using_untranslatable_client_method"/>;
    /// projects through a nested-collection indexer that also calls <c>MyMethod</c>.
    /// Same disposition rationale.
    /// </summary>
    public override async Task Json_collection_index_in_projection_using_untranslatable_client_method2(
        bool async
    )
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => base.Json_collection_index_in_projection_using_untranslatable_client_method2(async));
        Assert.Contains("MyMethod", ex.Message);
        Assert.Contains("could not be translated", ex.Message);
    }

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
