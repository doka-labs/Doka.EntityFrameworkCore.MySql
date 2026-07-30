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
