namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies statement-local source values and ordinary EF execution against each supported engine.
/// </summary>
[IntegrationDatabaseTest]
public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Volatile_values_are_shared_on_mysql84() =>
        AssertVolatileValuesSharedAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Volatile_values_are_shared_on_mysql97() =>
        AssertVolatileValuesSharedAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Volatile_values_are_shared_on_mariadb1011() =>
        AssertVolatileValuesSharedAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Volatile_values_are_shared_on_mariadb114() =>
        AssertVolatileValuesSharedAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Volatile_values_are_shared_on_mariadb118() =>
        AssertVolatileValuesSharedAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Volatile_values_are_shared_on_mariadb123() =>
        AssertVolatileValuesSharedAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Disjoint_arms_share_random_page_on_mysql84() =>
        AssertDisjointArmsShareRandomPageAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Disjoint_arms_share_random_page_on_mysql97() =>
        AssertDisjointArmsShareRandomPageAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Disjoint_arms_share_random_page_on_mariadb1011() =>
        AssertDisjointArmsShareRandomPageAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Disjoint_arms_share_random_page_on_mariadb114() =>
        AssertDisjointArmsShareRandomPageAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Disjoint_arms_share_random_page_on_mariadb118() =>
        AssertDisjointArmsShareRandomPageAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Disjoint_arms_share_random_page_on_mariadb123() =>
        AssertDisjointArmsShareRandomPageAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Duplicate_occurrences_are_preserved_on_mysql84() =>
        AssertDuplicateOccurrencesAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Duplicate_occurrences_are_preserved_on_mysql97() =>
        AssertDuplicateOccurrencesAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Duplicate_occurrences_are_preserved_on_mariadb1011() =>
        AssertDuplicateOccurrencesAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Duplicate_occurrences_are_preserved_on_mariadb114() =>
        AssertDuplicateOccurrencesAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Duplicate_occurrences_are_preserved_on_mariadb118() =>
        AssertDuplicateOccurrencesAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Duplicate_occurrences_are_preserved_on_mariadb123() =>
        AssertDuplicateOccurrencesAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Reference_filters_are_independent_on_mysql84() =>
        AssertReferenceFiltersIndependentAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Reference_filters_are_independent_on_mysql97() =>
        AssertReferenceFiltersIndependentAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Reference_filters_are_independent_on_mariadb1011() =>
        AssertReferenceFiltersIndependentAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Reference_filters_are_independent_on_mariadb114() =>
        AssertReferenceFiltersIndependentAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Reference_filters_are_independent_on_mariadb118() =>
        AssertReferenceFiltersIndependentAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Reference_filters_are_independent_on_mariadb123() =>
        AssertReferenceFiltersIndependentAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Empty_source_has_empty_envelope_on_mysql84() =>
        AssertEmptyEnvelopeAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Empty_source_has_empty_envelope_on_mysql97() =>
        AssertEmptyEnvelopeAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Empty_source_has_empty_envelope_on_mariadb1011() =>
        AssertEmptyEnvelopeAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Empty_source_has_empty_envelope_on_mariadb114() =>
        AssertEmptyEnvelopeAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Empty_source_has_empty_envelope_on_mariadb118() =>
        AssertEmptyEnvelopeAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Empty_source_has_empty_envelope_on_mariadb123() =>
        AssertEmptyEnvelopeAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Independent_bindings_have_independent_values_on_mysql84() =>
        AssertIndependentBindingsAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Independent_bindings_have_independent_values_on_mysql97() =>
        AssertIndependentBindingsAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Independent_bindings_have_independent_values_on_mariadb1011() =>
        AssertIndependentBindingsAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Independent_bindings_have_independent_values_on_mariadb114() =>
        AssertIndependentBindingsAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Independent_bindings_have_independent_values_on_mariadb118() =>
        AssertIndependentBindingsAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Independent_bindings_have_independent_values_on_mariadb123() =>
        AssertIndependentBindingsAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Changed_parameters_are_not_reused_on_mysql84() =>
        AssertChangedParametersAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Changed_parameters_are_not_reused_on_mysql97() =>
        AssertChangedParametersAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Changed_parameters_are_not_reused_on_mariadb1011() =>
        AssertChangedParametersAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Changed_parameters_are_not_reused_on_mariadb114() =>
        AssertChangedParametersAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Changed_parameters_are_not_reused_on_mariadb118() =>
        AssertChangedParametersAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Changed_parameters_are_not_reused_on_mariadb123() =>
        AssertChangedParametersAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Repeated_enumeration_recomputes_values_on_mysql84() =>
        AssertRepeatedEnumerationAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Repeated_enumeration_recomputes_values_on_mysql97() =>
        AssertRepeatedEnumerationAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Repeated_enumeration_recomputes_values_on_mariadb1011() =>
        AssertRepeatedEnumerationAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Repeated_enumeration_recomputes_values_on_mariadb114() =>
        AssertRepeatedEnumerationAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Repeated_enumeration_recomputes_values_on_mariadb118() =>
        AssertRepeatedEnumerationAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Repeated_enumeration_recomputes_values_on_mariadb123() =>
        AssertRepeatedEnumerationAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Compiled_query_preserves_binding_on_mysql84() =>
        AssertCompiledQueryAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Compiled_query_preserves_binding_on_mysql97() =>
        AssertCompiledQueryAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Compiled_query_preserves_binding_on_mariadb1011() =>
        AssertCompiledQueryAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Compiled_query_preserves_binding_on_mariadb114() =>
        AssertCompiledQueryAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Compiled_query_preserves_binding_on_mariadb118() =>
        AssertCompiledQueryAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Compiled_query_preserves_binding_on_mariadb123() =>
        AssertCompiledQueryAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Native_guid_null_collation_and_converter_are_preserved_on_mysql84() =>
        AssertNativeValuesAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Native_guid_null_collation_and_converter_are_preserved_on_mysql97() =>
        AssertNativeValuesAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Native_guid_null_collation_and_converter_are_preserved_on_mariadb1011() =>
        AssertNativeValuesAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Native_guid_null_collation_and_converter_are_preserved_on_mariadb114() =>
        AssertNativeValuesAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Native_guid_null_collation_and_converter_are_preserved_on_mariadb118() =>
        AssertNativeValuesAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Native_guid_null_collation_and_converter_are_preserved_on_mariadb123() =>
        AssertNativeValuesAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Entity_results_keep_tracking_on_mysql84() =>
        AssertEntityTrackingAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Entity_results_keep_tracking_on_mysql97() =>
        AssertEntityTrackingAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Entity_results_keep_tracking_on_mariadb1011() =>
        AssertEntityTrackingAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Entity_results_keep_tracking_on_mariadb114() =>
        AssertEntityTrackingAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Entity_results_keep_tracking_on_mariadb118() =>
        AssertEntityTrackingAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Entity_results_keep_tracking_on_mariadb123() =>
        AssertEntityTrackingAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Collection_results_keep_identifiers_on_mysql84() =>
        AssertCollectionResultsAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Collection_results_keep_identifiers_on_mysql97() =>
        AssertCollectionResultsAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Collection_results_keep_identifiers_on_mariadb1011() =>
        AssertCollectionResultsAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Collection_results_keep_identifiers_on_mariadb114() =>
        AssertCollectionResultsAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Collection_results_keep_identifiers_on_mariadb118() =>
        AssertCollectionResultsAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Collection_results_keep_identifiers_on_mariadb123() =>
        AssertCollectionResultsAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Cancellation_reaches_one_command_on_mysql84() =>
        AssertCancellationAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Cancellation_reaches_one_command_on_mysql97() =>
        AssertCancellationAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Cancellation_reaches_one_command_on_mariadb1011() =>
        AssertCancellationAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Cancellation_reaches_one_command_on_mariadb114() =>
        AssertCancellationAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Cancellation_reaches_one_command_on_mariadb118() =>
        AssertCancellationAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Cancellation_reaches_one_command_on_mariadb123() =>
        AssertCancellationAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Construction_does_not_execute_on_mysql84() =>
        AssertConstructionAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Construction_does_not_execute_on_mysql97() =>
        AssertConstructionAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Construction_does_not_execute_on_mariadb1011() =>
        AssertConstructionAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Construction_does_not_execute_on_mariadb114() =>
        AssertConstructionAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Construction_does_not_execute_on_mariadb118() =>
        AssertConstructionAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Construction_does_not_execute_on_mariadb123() =>
        AssertConstructionAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Client_source_projection_is_rejected_before_io_on_mysql84() =>
        AssertClientProjectionRejectedAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Client_source_projection_is_rejected_before_io_on_mysql97() =>
        AssertClientProjectionRejectedAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Client_source_projection_is_rejected_before_io_on_mariadb1011() =>
        AssertClientProjectionRejectedAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Client_source_projection_is_rejected_before_io_on_mariadb114() =>
        AssertClientProjectionRejectedAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Client_source_projection_is_rejected_before_io_on_mariadb118() =>
        AssertClientProjectionRejectedAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Client_source_projection_is_rejected_before_io_on_mariadb123() =>
        AssertClientProjectionRejectedAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Deferred_collection_source_is_rejected_before_io_on_mysql84() =>
        AssertCollectionSourceRejectedAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Deferred_collection_source_is_rejected_before_io_on_mysql97() =>
        AssertCollectionSourceRejectedAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Deferred_collection_source_is_rejected_before_io_on_mariadb1011() =>
        AssertCollectionSourceRejectedAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Deferred_collection_source_is_rejected_before_io_on_mariadb114() =>
        AssertCollectionSourceRejectedAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Deferred_collection_source_is_rejected_before_io_on_mariadb118() =>
        AssertCollectionSourceRejectedAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Deferred_collection_source_is_rejected_before_io_on_mariadb123() =>
        AssertCollectionSourceRejectedAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Foreign_context_is_rejected_before_io_on_mysql84() =>
        AssertForeignContextRejectedAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Foreign_context_is_rejected_before_io_on_mysql97() =>
        AssertForeignContextRejectedAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Foreign_context_is_rejected_before_io_on_mariadb1011() =>
        AssertForeignContextRejectedAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Foreign_context_is_rejected_before_io_on_mariadb114() =>
        AssertForeignContextRejectedAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Foreign_context_is_rejected_before_io_on_mariadb118() =>
        AssertForeignContextRejectedAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Foreign_context_is_rejected_before_io_on_mariadb123() =>
        AssertForeignContextRejectedAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Dependent_binding_preserves_outer_values_on_mysql84() =>
        AssertDependentBindingAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Dependent_binding_preserves_outer_values_on_mysql97() =>
        AssertDependentBindingAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Dependent_binding_preserves_outer_values_on_mariadb1011() =>
        AssertDependentBindingAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Dependent_binding_preserves_outer_values_on_mariadb114() =>
        AssertDependentBindingAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Dependent_binding_preserves_outer_values_on_mariadb118() =>
        AssertDependentBindingAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Dependent_binding_preserves_outer_values_on_mariadb123() =>
        AssertDependentBindingAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Distinct_global_filter_parameters_are_preserved_on_mysql84() =>
        AssertDistinctGlobalFilterParametersAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Distinct_global_filter_parameters_are_preserved_on_mysql97() =>
        AssertDistinctGlobalFilterParametersAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Distinct_global_filter_parameters_are_preserved_on_mariadb1011() =>
        AssertDistinctGlobalFilterParametersAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Distinct_global_filter_parameters_are_preserved_on_mariadb114() =>
        AssertDistinctGlobalFilterParametersAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Distinct_global_filter_parameters_are_preserved_on_mariadb118() =>
        AssertDistinctGlobalFilterParametersAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Distinct_global_filter_parameters_are_preserved_on_mariadb123() =>
        AssertDistinctGlobalFilterParametersAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Cached_global_filters_read_current_context_values_on_mysql84() =>
        AssertCachedGlobalFiltersUseCurrentContextValuesAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Cached_global_filters_read_current_context_values_on_mysql97() =>
        AssertCachedGlobalFiltersUseCurrentContextValuesAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Cached_global_filters_read_current_context_values_on_mariadb1011() =>
        AssertCachedGlobalFiltersUseCurrentContextValuesAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Cached_global_filters_read_current_context_values_on_mariadb114() =>
        AssertCachedGlobalFiltersUseCurrentContextValuesAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Cached_global_filters_read_current_context_values_on_mariadb118() =>
        AssertCachedGlobalFiltersUseCurrentContextValuesAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Cached_global_filters_read_current_context_values_on_mariadb123() =>
        AssertCachedGlobalFiltersUseCurrentContextValuesAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Paged_global_filter_is_not_reapplied_on_mysql84() =>
        AssertPagedGlobalFilterAppliedOnlyToSourceAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Paged_global_filter_is_not_reapplied_on_mysql97() =>
        AssertPagedGlobalFilterAppliedOnlyToSourceAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Paged_global_filter_is_not_reapplied_on_mariadb1011() =>
        AssertPagedGlobalFilterAppliedOnlyToSourceAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Paged_global_filter_is_not_reapplied_on_mariadb114() =>
        AssertPagedGlobalFilterAppliedOnlyToSourceAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Paged_global_filter_is_not_reapplied_on_mariadb118() =>
        AssertPagedGlobalFilterAppliedOnlyToSourceAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Paged_global_filter_is_not_reapplied_on_mariadb123() =>
        AssertPagedGlobalFilterAppliedOnlyToSourceAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Source_ignore_filters_keeps_query_wide_semantics_on_mysql84() =>
        AssertIgnoredFiltersKeepQueryWideSemanticsAsync(IntegrationDatabaseTarget.MySql84, true);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Source_ignore_filters_keeps_query_wide_semantics_on_mysql97() =>
        AssertIgnoredFiltersKeepQueryWideSemanticsAsync(IntegrationDatabaseTarget.MySql97, true);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Source_ignore_filters_keeps_query_wide_semantics_on_mariadb1011() =>
        AssertIgnoredFiltersKeepQueryWideSemanticsAsync(IntegrationDatabaseTarget.MariaDb1011, true);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Source_ignore_filters_keeps_query_wide_semantics_on_mariadb114() =>
        AssertIgnoredFiltersKeepQueryWideSemanticsAsync(IntegrationDatabaseTarget.MariaDb114, true);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Source_ignore_filters_keeps_query_wide_semantics_on_mariadb118() =>
        AssertIgnoredFiltersKeepQueryWideSemanticsAsync(IntegrationDatabaseTarget.MariaDb118, true);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Source_ignore_filters_keeps_query_wide_semantics_on_mariadb123() =>
        AssertIgnoredFiltersKeepQueryWideSemanticsAsync(IntegrationDatabaseTarget.MariaDb123, true);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Outer_ignore_filters_keeps_query_wide_semantics_on_mysql84() =>
        AssertIgnoredFiltersKeepQueryWideSemanticsAsync(IntegrationDatabaseTarget.MySql84, false);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Outer_ignore_filters_keeps_query_wide_semantics_on_mysql97() =>
        AssertIgnoredFiltersKeepQueryWideSemanticsAsync(IntegrationDatabaseTarget.MySql97, false);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Outer_ignore_filters_keeps_query_wide_semantics_on_mariadb1011() =>
        AssertIgnoredFiltersKeepQueryWideSemanticsAsync(IntegrationDatabaseTarget.MariaDb1011, false);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Outer_ignore_filters_keeps_query_wide_semantics_on_mariadb114() =>
        AssertIgnoredFiltersKeepQueryWideSemanticsAsync(IntegrationDatabaseTarget.MariaDb114, false);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Outer_ignore_filters_keeps_query_wide_semantics_on_mariadb118() =>
        AssertIgnoredFiltersKeepQueryWideSemanticsAsync(IntegrationDatabaseTarget.MariaDb118, false);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Outer_ignore_filters_keeps_query_wide_semantics_on_mariadb123() =>
        AssertIgnoredFiltersKeepQueryWideSemanticsAsync(IntegrationDatabaseTarget.MariaDb123, false);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Raw_sql_name_collision_keeps_bounded_identifier_on_mysql84() =>
        AssertRawSqlCollisionHasBoundedIdentifierAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Raw_sql_name_collision_keeps_bounded_identifier_on_mysql97() =>
        AssertRawSqlCollisionHasBoundedIdentifierAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Raw_sql_name_collision_keeps_bounded_identifier_on_mariadb1011() =>
        AssertRawSqlCollisionHasBoundedIdentifierAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Raw_sql_name_collision_keeps_bounded_identifier_on_mariadb114() =>
        AssertRawSqlCollisionHasBoundedIdentifierAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Raw_sql_name_collision_keeps_bounded_identifier_on_mariadb118() =>
        AssertRawSqlCollisionHasBoundedIdentifierAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Raw_sql_name_collision_keeps_bounded_identifier_on_mariadb123() =>
        AssertRawSqlCollisionHasBoundedIdentifierAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Supplied_runtime_model_preserves_source_metadata_on_mysql84() =>
        AssertSuppliedRuntimeModelPreservesSourceMetadataAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Supplied_runtime_model_preserves_source_metadata_on_mysql97() =>
        AssertSuppliedRuntimeModelPreservesSourceMetadataAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Supplied_runtime_model_preserves_source_metadata_on_mariadb1011() =>
        AssertSuppliedRuntimeModelPreservesSourceMetadataAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Supplied_runtime_model_preserves_source_metadata_on_mariadb114() =>
        AssertSuppliedRuntimeModelPreservesSourceMetadataAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Supplied_runtime_model_preserves_source_metadata_on_mariadb118() =>
        AssertSuppliedRuntimeModelPreservesSourceMetadataAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Supplied_runtime_model_preserves_source_metadata_on_mariadb123() =>
        AssertSuppliedRuntimeModelPreservesSourceMetadataAsync(IntegrationDatabaseTarget.MariaDb123);
}
