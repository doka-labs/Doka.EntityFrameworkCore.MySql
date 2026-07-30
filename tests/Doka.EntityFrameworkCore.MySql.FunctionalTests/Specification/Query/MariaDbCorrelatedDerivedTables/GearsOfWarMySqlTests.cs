using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

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
