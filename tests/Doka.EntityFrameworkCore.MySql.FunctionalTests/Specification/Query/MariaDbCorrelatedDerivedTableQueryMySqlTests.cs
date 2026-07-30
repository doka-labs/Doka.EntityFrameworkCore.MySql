using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

// These metadata-only overrides keep every unsupported official query explicit. The base
// implementation and its theory rows remain authoritative; only the MariaDB engine
// disposition changes at the provider boundary.

public sealed partial class AdHocNavigationsQueryMySqlTest
{
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Let_multiple_references_with_reference_to_outer() =>
        base.Let_multiple_references_with_reference_to_outer();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Projection_with_multiple_includes_and_subquery_with_set_operation() =>
        base.Projection_with_multiple_includes_and_subquery_with_set_operation();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task SelectMany_and_collection_in_projection_in_FirstOrDefault() =>
        base.SelectMany_and_collection_in_projection_in_FirstOrDefault();
}

public sealed partial class AdHocQueryFiltersQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Group_by_multiple_aggregate_joining_different_tables(
        bool async
    ) => base.Group_by_multiple_aggregate_joining_different_tables(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Group_by_multiple_aggregate_joining_different_tables_with_query_filter(
        bool async
    ) => base.Group_by_multiple_aggregate_joining_different_tables_with_query_filter(async);
}

public sealed partial class ComplexNavigationsCollectionsQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Complex_query_issue_21665(
        bool async
    ) => base.Complex_query_issue_21665(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Complex_query_with_let_collection_projection_FirstOrDefault(
        bool async
    ) => base.Complex_query_with_let_collection_projection_FirstOrDefault(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Complex_query_with_let_collection_projection_FirstOrDefault_with_ToList_on_inner_and_outer(
        bool async
    ) => base.Complex_query_with_let_collection_projection_FirstOrDefault_with_ToList_on_inner_and_outer(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_Skip_Take_with_another_Skip_Take_on_top_level(
        bool async
    ) => base.Filtered_include_Skip_Take_with_another_Skip_Take_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_Take_with_another_Take_on_top_level(
        bool async
    ) => base.Filtered_include_Take_with_another_Take_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_after_different_filtered_include_different_level(
        bool async
    ) => base.Filtered_include_after_different_filtered_include_different_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_and_non_filtered_include_followed_by_then_include_on_same_navigation(
        bool async
    ) => base.Filtered_include_and_non_filtered_include_followed_by_then_include_on_same_navigation(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_complex_three_level_with_middle_having_filter1(
        bool async
    ) => base.Filtered_include_complex_three_level_with_middle_having_filter1(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_complex_three_level_with_middle_having_filter2(
        bool async
    ) => base.Filtered_include_complex_three_level_with_middle_having_filter2(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_multiple_multi_level_includes_with_first_level_using_filter_include_on_one_of_the_chains_only(
            bool async
        ) => base
        .Filtered_include_multiple_multi_level_includes_with_first_level_using_filter_include_on_one_of_the_chains_only(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_outer_parameter_used_inside_filter(
        bool async
    ) => base.Filtered_include_outer_parameter_used_inside_filter(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_same_filter_set_on_same_navigation_twice_followed_by_ThenIncludes(
        bool async
    ) => base.Filtered_include_same_filter_set_on_same_navigation_twice_followed_by_ThenIncludes(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_FirstOrDefault_on_top_level(
            bool async
        ) => base
        .Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_FirstOrDefault_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_unordered_Take_on_top_level(
            bool async
        ) => base
        .Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_unordered_Take_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Include_inside_subquery(
        bool async
    ) => base.Include_inside_subquery(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Projecting_collection_with_group_by_after_optional_reference_correlated_with_parent(
        bool async
    ) => base.Projecting_collection_with_group_by_after_optional_reference_correlated_with_parent(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        SelectMany_with_predicate_and_DefaultIfEmpty_projecting_root_collection_element_and_another_collection(
            bool async
        ) => base
        .SelectMany_with_predicate_and_DefaultIfEmpty_projecting_root_collection_element_and_another_collection(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_Distinct_on_grouping_element(
        bool async
    ) => base.Skip_Take_Distinct_on_grouping_element(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_Select_collection_Skip_Take(
        bool async
    ) => base.Skip_Take_Select_collection_Skip_Take(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_on_grouping_element_inside_collection_projection(
        bool async
    ) => base.Skip_Take_on_grouping_element_inside_collection_projection(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_on_grouping_element_with_collection_include(
        bool async
    ) => base.Skip_Take_on_grouping_element_with_collection_include(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_on_grouping_element_with_reference_include(
        bool async
    ) => base.Skip_Take_on_grouping_element_with_reference_include(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Take_Select_collection_Take(
        bool async
    ) => base.Take_Select_collection_Take(async);
}

public sealed partial class ComplexNavigationsCollectionsSharedTypeQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Complex_query_with_let_collection_projection_FirstOrDefault(
        bool async
    ) => base.Complex_query_with_let_collection_projection_FirstOrDefault(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Complex_query_with_let_collection_projection_FirstOrDefault_with_ToList_on_inner_and_outer(
        bool async
    ) => base.Complex_query_with_let_collection_projection_FirstOrDefault_with_ToList_on_inner_and_outer(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_Skip_Take_with_another_Skip_Take_on_top_level(
        bool async
    ) => base.Filtered_include_Skip_Take_with_another_Skip_Take_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_Take_with_another_Take_on_top_level(
        bool async
    ) => base.Filtered_include_Take_with_another_Take_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_after_different_filtered_include_different_level(
        bool async
    ) => base.Filtered_include_after_different_filtered_include_different_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_and_non_filtered_include_followed_by_then_include_on_same_navigation(
        bool async
    ) => base.Filtered_include_and_non_filtered_include_followed_by_then_include_on_same_navigation(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_complex_three_level_with_middle_having_filter1(
        bool async
    ) => base.Filtered_include_complex_three_level_with_middle_having_filter1(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_complex_three_level_with_middle_having_filter2(
        bool async
    ) => base.Filtered_include_complex_three_level_with_middle_having_filter2(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_multiple_multi_level_includes_with_first_level_using_filter_include_on_one_of_the_chains_only(
            bool async
        ) => base
        .Filtered_include_multiple_multi_level_includes_with_first_level_using_filter_include_on_one_of_the_chains_only(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_same_filter_set_on_same_navigation_twice_followed_by_ThenIncludes(
        bool async
    ) => base.Filtered_include_same_filter_set_on_same_navigation_twice_followed_by_ThenIncludes(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_FirstOrDefault_on_top_level(
            bool async
        ) => base
        .Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_FirstOrDefault_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_unordered_Take_on_top_level(
            bool async
        ) => base
        .Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_unordered_Take_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Projecting_collection_after_optional_reference_correlated_with_parent(
        bool async
    ) => base.Projecting_collection_after_optional_reference_correlated_with_parent(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Projecting_collection_with_group_by_after_optional_reference_correlated_with_parent(
        bool async
    ) => base.Projecting_collection_with_group_by_after_optional_reference_correlated_with_parent(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        SelectMany_with_predicate_and_DefaultIfEmpty_projecting_root_collection_element_and_another_collection(
            bool async
        ) => base
        .SelectMany_with_predicate_and_DefaultIfEmpty_projecting_root_collection_element_and_another_collection(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_Distinct_on_grouping_element(
        bool async
    ) => base.Skip_Take_Distinct_on_grouping_element(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_Select_collection_Skip_Take(
        bool async
    ) => base.Skip_Take_Select_collection_Skip_Take(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_on_grouping_element_inside_collection_projection(
        bool async
    ) => base.Skip_Take_on_grouping_element_inside_collection_projection(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_on_grouping_element_with_collection_include(
        bool async
    ) => base.Skip_Take_on_grouping_element_with_collection_include(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_on_grouping_element_with_reference_include(
        bool async
    ) => base.Skip_Take_on_grouping_element_with_reference_include(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Take_Select_collection_Take(
        bool async
    ) => base.Take_Select_collection_Take(async);
}

public sealed partial class ComplexNavigationsCollectionsSplitQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Complex_query_issue_21665(
        bool async
    ) => base.Complex_query_issue_21665(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Complex_query_with_let_collection_projection_FirstOrDefault(
        bool async
    ) => base.Complex_query_with_let_collection_projection_FirstOrDefault(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Complex_query_with_let_collection_projection_FirstOrDefault_with_ToList_on_inner_and_outer(
        bool async
    ) => base.Complex_query_with_let_collection_projection_FirstOrDefault_with_ToList_on_inner_and_outer(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_Skip_Take_with_another_Skip_Take_on_top_level(
        bool async
    ) => base.Filtered_include_Skip_Take_with_another_Skip_Take_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_Take_with_another_Take_on_top_level(
        bool async
    ) => base.Filtered_include_Take_with_another_Take_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_and_non_filtered_include_followed_by_then_include_on_same_navigation(
        bool async
    ) => base.Filtered_include_and_non_filtered_include_followed_by_then_include_on_same_navigation(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_multiple_multi_level_includes_with_first_level_using_filter_include_on_one_of_the_chains_only(
            bool async
        ) => base
        .Filtered_include_multiple_multi_level_includes_with_first_level_using_filter_include_on_one_of_the_chains_only(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_same_filter_set_on_same_navigation_twice_followed_by_ThenIncludes(
        bool async
    ) => base.Filtered_include_same_filter_set_on_same_navigation_twice_followed_by_ThenIncludes(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_FirstOrDefault_on_top_level(
            bool async
        ) => base
        .Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_FirstOrDefault_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_unordered_Take_on_top_level(
            bool async
        ) => base
        .Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_unordered_Take_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Include_inside_subquery(
        bool async
    ) => base.Include_inside_subquery(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Projecting_collection_with_group_by_after_optional_reference_correlated_with_parent(
        bool async
    ) => base.Projecting_collection_with_group_by_after_optional_reference_correlated_with_parent(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        SelectMany_with_predicate_and_DefaultIfEmpty_projecting_root_collection_element_and_another_collection(
            bool async
        ) => base
        .SelectMany_with_predicate_and_DefaultIfEmpty_projecting_root_collection_element_and_another_collection(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_Distinct_on_grouping_element(
        bool async
    ) => base.Skip_Take_Distinct_on_grouping_element(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_Select_collection_Skip_Take(
        bool async
    ) => base.Skip_Take_Select_collection_Skip_Take(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_on_grouping_element_inside_collection_projection(
        bool async
    ) => base.Skip_Take_on_grouping_element_inside_collection_projection(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_on_grouping_element_with_reference_include(
        bool async
    ) => base.Skip_Take_on_grouping_element_with_reference_include(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Take_Select_collection_Take(
        bool async
    ) => base.Take_Select_collection_Take(async);
}

public sealed partial class ComplexNavigationsCollectionsSplitSharedTypeQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Complex_query_with_let_collection_projection_FirstOrDefault(
        bool async
    ) => base.Complex_query_with_let_collection_projection_FirstOrDefault(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Complex_query_with_let_collection_projection_FirstOrDefault_with_ToList_on_inner_and_outer(
        bool async
    ) => base.Complex_query_with_let_collection_projection_FirstOrDefault_with_ToList_on_inner_and_outer(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_Skip_Take_with_another_Skip_Take_on_top_level(
        bool async
    ) => base.Filtered_include_Skip_Take_with_another_Skip_Take_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_Take_with_another_Take_on_top_level(
        bool async
    ) => base.Filtered_include_Take_with_another_Take_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_and_non_filtered_include_followed_by_then_include_on_same_navigation(
        bool async
    ) => base.Filtered_include_and_non_filtered_include_followed_by_then_include_on_same_navigation(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_multiple_multi_level_includes_with_first_level_using_filter_include_on_one_of_the_chains_only(
            bool async
        ) => base
        .Filtered_include_multiple_multi_level_includes_with_first_level_using_filter_include_on_one_of_the_chains_only(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_same_filter_set_on_same_navigation_twice_followed_by_ThenIncludes(
        bool async
    ) => base.Filtered_include_same_filter_set_on_same_navigation_twice_followed_by_ThenIncludes(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_FirstOrDefault_on_top_level(
            bool async
        ) => base
        .Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_FirstOrDefault_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_unordered_Take_on_top_level(
            bool async
        ) => base
        .Filtered_include_with_Take_without_order_by_followed_by_ThenInclude_and_unordered_Take_on_top_level(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Projecting_collection_after_optional_reference_correlated_with_parent(
        bool async
    ) => base.Projecting_collection_after_optional_reference_correlated_with_parent(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Projecting_collection_with_group_by_after_optional_reference_correlated_with_parent(
        bool async
    ) => base.Projecting_collection_with_group_by_after_optional_reference_correlated_with_parent(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        SelectMany_with_predicate_and_DefaultIfEmpty_projecting_root_collection_element_and_another_collection(
            bool async
        ) => base
        .SelectMany_with_predicate_and_DefaultIfEmpty_projecting_root_collection_element_and_another_collection(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_Distinct_on_grouping_element(
        bool async
    ) => base.Skip_Take_Distinct_on_grouping_element(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_Select_collection_Skip_Take(
        bool async
    ) => base.Skip_Take_Select_collection_Skip_Take(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_on_grouping_element_inside_collection_projection(
        bool async
    ) => base.Skip_Take_on_grouping_element_inside_collection_projection(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Skip_Take_on_grouping_element_with_reference_include(
        bool async
    ) => base.Skip_Take_on_grouping_element_with_reference_include(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Take_Select_collection_Take(
        bool async
    ) => base.Take_Select_collection_Take(async);
}

public sealed partial class ComplexNavigationsQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Contains_with_subquery_optional_navigation_and_constant_item(
        bool async
    ) => base.Contains_with_subquery_optional_navigation_and_constant_item(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_projection_with_first(
        bool async
    ) => base.Correlated_projection_with_first(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Let_let_contains_from_outer_let(
        bool async
    ) => base.Let_let_contains_from_outer_let(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Multiple_select_many_in_projection(
        bool async
    ) => base.Multiple_select_many_in_projection(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Nested_SelectMany_correlated_with_join_table_correctly_translated_to_apply(
        bool async
    ) => base.Nested_SelectMany_correlated_with_join_table_correctly_translated_to_apply(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Single_select_many_in_projection_with_take(
        bool async
    ) => base.Single_select_many_in_projection_with_take(async);
}

public sealed partial class ComplexNavigationsSharedTypeQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Contains_with_subquery_optional_navigation_and_constant_item(
        bool async
    ) => base.Contains_with_subquery_optional_navigation_and_constant_item(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_projection_with_first(
        bool async
    ) => base.Correlated_projection_with_first(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Let_let_contains_from_outer_let(
        bool async
    ) => base.Let_let_contains_from_outer_let(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Multiple_select_many_in_projection(
        bool async
    ) => base.Multiple_select_many_in_projection(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Nested_SelectMany_correlated_with_join_table_correctly_translated_to_apply(
        bool async
    ) => base.Nested_SelectMany_correlated_with_join_table_correctly_translated_to_apply(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Single_select_many_in_projection_with_take(
        bool async
    ) => base.Single_select_many_in_projection_with_take(async);
}

public sealed partial class ComplexTypeQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Same_entity_with_complex_type_projected_twice_with_pushdown_as_part_of_another_projection(
        bool async
    ) => base.Same_entity_with_complex_type_projected_twice_with_pushdown_as_part_of_another_projection(async);
}

public sealed partial class GearsOfWarQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Concat_with_collection_navigations(
        bool async
    ) => base.Concat_with_collection_navigations(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collection_after_distinct_3_levels(
        bool async
    ) => base.Correlated_collection_after_distinct_3_levels(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collection_via_SelectMany_with_Distinct_missing_indentifying_columns_in_projection(
        bool async
    ) => base.Correlated_collection_via_SelectMany_with_Distinct_missing_indentifying_columns_in_projection(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collection_with_distinct_not_projecting_identifier_column(
        bool async
    ) => base.Correlated_collection_with_distinct_not_projecting_identifier_column(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collection_with_distinct_projecting_identifier_column(
        bool async
    ) => base.Correlated_collection_with_distinct_projecting_identifier_column(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Correlated_collection_with_groupby_not_projecting_identifier_column_but_only_grouping_key_in_final_projection(
            bool async
        ) => base
        .Correlated_collection_with_groupby_not_projecting_identifier_column_but_only_grouping_key_in_final_projection(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection(
            bool async
        ) => base
        .Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection_multiple_grouping_keys(
            bool async
        ) => base
        .Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection_multiple_grouping_keys(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Correlated_collection_with_groupby_with_complex_grouping_key_not_projecting_identifier_column_with_group_aggregate_in_final_projection(
            bool async
        ) => base
        .Correlated_collection_with_groupby_with_complex_grouping_key_not_projecting_identifier_column_with_group_aggregate_in_final_projection(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collections_nested_inner_subquery_references_outer_qsre_two_levels_up(
        bool async
    ) => base.Correlated_collections_nested_inner_subquery_references_outer_qsre_two_levels_up(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collections_with_Distinct(
        bool async
    ) => base.Correlated_collections_with_Distinct(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Outer_parameter_in_group_join_with_DefaultIfEmpty(
        bool async
    ) => base.Outer_parameter_in_group_join_with_DefaultIfEmpty(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Outer_parameter_in_join_key(
        bool async
    ) => base.Outer_parameter_in_join_key(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Outer_parameter_in_join_key_inner_and_outer(
        bool async
    ) => base.Outer_parameter_in_join_key_inner_and_outer(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task SelectMany_predicate_with_non_equality_comparison_with_Take_doesnt_convert_to_join(
        bool async
    ) => base.SelectMany_predicate_with_non_equality_comparison_with_Take_doesnt_convert_to_join(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_navigation_with_concat_and_count(
        bool async
    ) => base.Select_navigation_with_concat_and_count(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_distinct_firstordefault(
        bool async
    ) => base.Select_subquery_distinct_firstordefault(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_distinct_singleordefault_boolean1(
        bool async
    ) => base.Select_subquery_distinct_singleordefault_boolean1(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_distinct_singleordefault_boolean_empty1(
        bool async
    ) => base.Select_subquery_distinct_singleordefault_boolean_empty1(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_distinct_singleordefault_boolean_empty_with_pushdown(
        bool async
    ) => base.Select_subquery_distinct_singleordefault_boolean_empty_with_pushdown(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_distinct_singleordefault_boolean_with_pushdown(
        bool async
    ) => base.Select_subquery_distinct_singleordefault_boolean_with_pushdown(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion(
        bool async
    ) => base.Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion_negated(
            bool async
        ) => base
        .Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion_negated(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion(
        bool async
    ) => base.Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion_negated(
        bool async
    ) => base.Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion_negated(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Union_with_collection_navigations(
        bool async
    ) => base.Union_with_collection_navigations(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_concat_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_concat_firstordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_first_boolean(
        bool async
    ) => base.Where_subquery_distinct_first_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_distinct_firstordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_firstordefault_boolean_with_pushdown(
        bool async
    ) => base.Where_subquery_distinct_firstordefault_boolean_with_pushdown(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_last_boolean(
        bool async
    ) => base.Where_subquery_distinct_last_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_lastordefault_boolean(
        bool async
    ) => base.Where_subquery_distinct_lastordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_orderby_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_distinct_orderby_firstordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_orderby_firstordefault_boolean_with_pushdown(
        bool async
    ) => base.Where_subquery_distinct_orderby_firstordefault_boolean_with_pushdown(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_singleordefault_boolean1(
        bool async
    ) => base.Where_subquery_distinct_singleordefault_boolean1(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_singleordefault_boolean_with_pushdown(
        bool async
    ) => base.Where_subquery_distinct_singleordefault_boolean_with_pushdown(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_join_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_join_firstordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_left_join_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_left_join_firstordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_union_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_union_firstordefault_boolean(async);
}

public sealed partial class ManyToManyNoTrackingQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
            bool async
        ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
        async);
}

public sealed partial class ManyToManyQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
            bool async
        ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
        async);
}

public sealed partial class OwnedQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Distinct_over_owned_collection(
        bool async
    ) => base.Distinct_over_owned_collection(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Union_over_owned_collection(
        bool async
    ) => base.Union_over_owned_collection(async);
}

public sealed partial class PrecompiledQueryMySqlTest
{
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task ValuesExpression() => base.ValuesExpression();
}

public sealed partial class PrimitiveCollectionsQueryMySqlTest
{
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Distinct() => base.Column_collection_Distinct();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Intersect_inline_collection() =>
        base.Column_collection_Intersect_inline_collection();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Skip() => base.Column_collection_Skip();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Union_parameter_collection() =>
        base.Column_collection_Union_parameter_collection();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Where_Skip() => base.Column_collection_Where_Skip();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Where_Skip_Take() => base.Column_collection_Where_Skip_Take();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Where_Take() => base.Column_collection_Where_Take();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Where_Union() => base.Column_collection_Where_Union();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_in_subquery_Union_parameter_collection() =>
        base.Column_collection_in_subquery_Union_parameter_collection();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Inline_collection_Except_column_collection() =>
        base.Inline_collection_Except_column_collection();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Inline_collection_of_nullable_value_type_Max() =>
        base.Inline_collection_of_nullable_value_type_Max();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Inline_collection_of_nullable_value_type_Min() =>
        base.Inline_collection_of_nullable_value_type_Min();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Inline_collection_of_nullable_value_type_with_null_Max() =>
        base.Inline_collection_of_nullable_value_type_with_null_Max();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Inline_collection_of_nullable_value_type_with_null_Min() =>
        base.Inline_collection_of_nullable_value_type_with_null_Min();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Parameter_collection_Concat_column_collection() =>
        base.Parameter_collection_Concat_column_collection();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Parameter_collection_in_subquery_Union_column_collection() =>
        base.Parameter_collection_in_subquery_Union_column_collection();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Parameter_collection_in_subquery_Union_column_collection_as_compiled_query() =>
        base.Parameter_collection_in_subquery_Union_column_collection_as_compiled_query();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Parameter_collection_in_subquery_Union_column_collection_nested() =>
        base.Parameter_collection_in_subquery_Union_column_collection_nested();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Project_collection_of_ints_with_distinct() => base.Project_collection_of_ints_with_distinct();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Project_collection_of_nullable_ints_with_paging() =>
        base.Project_collection_of_nullable_ints_with_paging();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Project_collection_of_nullable_ints_with_paging2() =>
        base.Project_collection_of_nullable_ints_with_paging2();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Project_collection_of_nullable_ints_with_paging3() =>
        base.Project_collection_of_nullable_ints_with_paging3();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Project_inline_collection_with_Union() => base.Project_inline_collection_with_Union();
}

public sealed partial class TpcGearsOfWarQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Concat_with_collection_navigations(
        bool async
    ) => base.Concat_with_collection_navigations(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collection_after_distinct_3_levels(
        bool async
    ) => base.Correlated_collection_after_distinct_3_levels(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collection_via_SelectMany_with_Distinct_missing_indentifying_columns_in_projection(
        bool async
    ) => base.Correlated_collection_via_SelectMany_with_Distinct_missing_indentifying_columns_in_projection(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collection_with_distinct_not_projecting_identifier_column(
        bool async
    ) => base.Correlated_collection_with_distinct_not_projecting_identifier_column(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collection_with_distinct_projecting_identifier_column(
        bool async
    ) => base.Correlated_collection_with_distinct_projecting_identifier_column(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Correlated_collection_with_groupby_not_projecting_identifier_column_but_only_grouping_key_in_final_projection(
            bool async
        ) => base
        .Correlated_collection_with_groupby_not_projecting_identifier_column_but_only_grouping_key_in_final_projection(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection(
            bool async
        ) => base
        .Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection_multiple_grouping_keys(
            bool async
        ) => base
        .Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection_multiple_grouping_keys(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Correlated_collection_with_groupby_with_complex_grouping_key_not_projecting_identifier_column_with_group_aggregate_in_final_projection(
            bool async
        ) => base
        .Correlated_collection_with_groupby_with_complex_grouping_key_not_projecting_identifier_column_with_group_aggregate_in_final_projection(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collections_nested_inner_subquery_references_outer_qsre_two_levels_up(
        bool async
    ) => base.Correlated_collections_nested_inner_subquery_references_outer_qsre_two_levels_up(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collections_with_Distinct(
        bool async
    ) => base.Correlated_collections_with_Distinct(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Outer_parameter_in_group_join_with_DefaultIfEmpty(
        bool async
    ) => base.Outer_parameter_in_group_join_with_DefaultIfEmpty(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Outer_parameter_in_join_key(
        bool async
    ) => base.Outer_parameter_in_join_key(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Outer_parameter_in_join_key_inner_and_outer(
        bool async
    ) => base.Outer_parameter_in_join_key_inner_and_outer(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task SelectMany_predicate_with_non_equality_comparison_with_Take_doesnt_convert_to_join(
        bool async
    ) => base.SelectMany_predicate_with_non_equality_comparison_with_Take_doesnt_convert_to_join(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_navigation_with_concat_and_count(
        bool async
    ) => base.Select_navigation_with_concat_and_count(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_distinct_firstordefault(
        bool async
    ) => base.Select_subquery_distinct_firstordefault(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_distinct_singleordefault_boolean1(
        bool async
    ) => base.Select_subquery_distinct_singleordefault_boolean1(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_distinct_singleordefault_boolean_empty1(
        bool async
    ) => base.Select_subquery_distinct_singleordefault_boolean_empty1(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_distinct_singleordefault_boolean_empty_with_pushdown(
        bool async
    ) => base.Select_subquery_distinct_singleordefault_boolean_empty_with_pushdown(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_distinct_singleordefault_boolean_with_pushdown(
        bool async
    ) => base.Select_subquery_distinct_singleordefault_boolean_with_pushdown(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion(
        bool async
    ) => base.Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion_negated(
            bool async
        ) => base
        .Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion_negated(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion(
        bool async
    ) => base.Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion_negated(
        bool async
    ) => base.Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion_negated(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Union_with_collection_navigations(
        bool async
    ) => base.Union_with_collection_navigations(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_concat_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_concat_firstordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_first_boolean(
        bool async
    ) => base.Where_subquery_distinct_first_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_distinct_firstordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_firstordefault_boolean_with_pushdown(
        bool async
    ) => base.Where_subquery_distinct_firstordefault_boolean_with_pushdown(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_last_boolean(
        bool async
    ) => base.Where_subquery_distinct_last_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_lastordefault_boolean(
        bool async
    ) => base.Where_subquery_distinct_lastordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_orderby_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_distinct_orderby_firstordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_orderby_firstordefault_boolean_with_pushdown(
        bool async
    ) => base.Where_subquery_distinct_orderby_firstordefault_boolean_with_pushdown(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_singleordefault_boolean1(
        bool async
    ) => base.Where_subquery_distinct_singleordefault_boolean1(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_singleordefault_boolean_with_pushdown(
        bool async
    ) => base.Where_subquery_distinct_singleordefault_boolean_with_pushdown(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_join_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_join_firstordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_left_join_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_left_join_firstordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_union_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_union_firstordefault_boolean(async);
}

public sealed partial class TpcManyToManyNoTrackingQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
            bool async
        ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
        async);
}

public sealed partial class TpcManyToManyQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
            bool async
        ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
        async);
}

public sealed partial class TptGearsOfWarQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Concat_with_collection_navigations(
        bool async
    ) => base.Concat_with_collection_navigations(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collection_after_distinct_3_levels(
        bool async
    ) => base.Correlated_collection_after_distinct_3_levels(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collection_via_SelectMany_with_Distinct_missing_indentifying_columns_in_projection(
        bool async
    ) => base.Correlated_collection_via_SelectMany_with_Distinct_missing_indentifying_columns_in_projection(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collection_with_distinct_not_projecting_identifier_column(
        bool async
    ) => base.Correlated_collection_with_distinct_not_projecting_identifier_column(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collection_with_distinct_projecting_identifier_column(
        bool async
    ) => base.Correlated_collection_with_distinct_projecting_identifier_column(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Correlated_collection_with_groupby_not_projecting_identifier_column_but_only_grouping_key_in_final_projection(
            bool async
        ) => base
        .Correlated_collection_with_groupby_not_projecting_identifier_column_but_only_grouping_key_in_final_projection(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection(
            bool async
        ) => base
        .Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection_multiple_grouping_keys(
            bool async
        ) => base
        .Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection_multiple_grouping_keys(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Correlated_collection_with_groupby_with_complex_grouping_key_not_projecting_identifier_column_with_group_aggregate_in_final_projection(
            bool async
        ) => base
        .Correlated_collection_with_groupby_with_complex_grouping_key_not_projecting_identifier_column_with_group_aggregate_in_final_projection(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collections_nested_inner_subquery_references_outer_qsre_two_levels_up(
        bool async
    ) => base.Correlated_collections_nested_inner_subquery_references_outer_qsre_two_levels_up(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Correlated_collections_with_Distinct(
        bool async
    ) => base.Correlated_collections_with_Distinct(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Outer_parameter_in_group_join_with_DefaultIfEmpty(
        bool async
    ) => base.Outer_parameter_in_group_join_with_DefaultIfEmpty(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Outer_parameter_in_join_key(
        bool async
    ) => base.Outer_parameter_in_join_key(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Outer_parameter_in_join_key_inner_and_outer(
        bool async
    ) => base.Outer_parameter_in_join_key_inner_and_outer(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task SelectMany_predicate_with_non_equality_comparison_with_Take_doesnt_convert_to_join(
        bool async
    ) => base.SelectMany_predicate_with_non_equality_comparison_with_Take_doesnt_convert_to_join(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_navigation_with_concat_and_count(
        bool async
    ) => base.Select_navigation_with_concat_and_count(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_distinct_firstordefault(
        bool async
    ) => base.Select_subquery_distinct_firstordefault(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_distinct_singleordefault_boolean1(
        bool async
    ) => base.Select_subquery_distinct_singleordefault_boolean1(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_distinct_singleordefault_boolean_empty1(
        bool async
    ) => base.Select_subquery_distinct_singleordefault_boolean_empty1(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_distinct_singleordefault_boolean_empty_with_pushdown(
        bool async
    ) => base.Select_subquery_distinct_singleordefault_boolean_empty_with_pushdown(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_distinct_singleordefault_boolean_with_pushdown(
        bool async
    ) => base.Select_subquery_distinct_singleordefault_boolean_with_pushdown(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion(
        bool async
    ) => base.Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion_negated(
            bool async
        ) => base
        .Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion_negated(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion(
        bool async
    ) => base.Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion_negated(
        bool async
    ) => base.Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion_negated(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Union_with_collection_navigations(
        bool async
    ) => base.Union_with_collection_navigations(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_concat_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_concat_firstordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_first_boolean(
        bool async
    ) => base.Where_subquery_distinct_first_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_distinct_firstordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_firstordefault_boolean_with_pushdown(
        bool async
    ) => base.Where_subquery_distinct_firstordefault_boolean_with_pushdown(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_last_boolean(
        bool async
    ) => base.Where_subquery_distinct_last_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_lastordefault_boolean(
        bool async
    ) => base.Where_subquery_distinct_lastordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_orderby_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_distinct_orderby_firstordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_orderby_firstordefault_boolean_with_pushdown(
        bool async
    ) => base.Where_subquery_distinct_orderby_firstordefault_boolean_with_pushdown(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_singleordefault_boolean1(
        bool async
    ) => base.Where_subquery_distinct_singleordefault_boolean1(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_distinct_singleordefault_boolean_with_pushdown(
        bool async
    ) => base.Where_subquery_distinct_singleordefault_boolean_with_pushdown(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_join_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_join_firstordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_left_join_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_left_join_firstordefault_boolean(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Where_subquery_union_firstordefault_boolean(
        bool async
    ) => base.Where_subquery_union_firstordefault_boolean(async);
}

public sealed partial class TptManyToManyNoTrackingQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
            bool async
        ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
        async);
}

public sealed partial class TptManyToManyQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
            bool async
        ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
        async);
}
