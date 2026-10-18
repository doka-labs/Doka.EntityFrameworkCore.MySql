namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task First_named_mapping_keeps_navigation_on_mysql84() =>
        AssertNamedNavigationLineageAsync(IntegrationDatabaseTarget.MySql84, "First");

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task First_named_mapping_keeps_navigation_on_mysql97() =>
        AssertNamedNavigationLineageAsync(IntegrationDatabaseTarget.MySql97, "First");

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task First_named_mapping_keeps_navigation_on_mariadb1011() =>
        AssertNamedNavigationLineageAsync(IntegrationDatabaseTarget.MariaDb1011, "First");

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task First_named_mapping_keeps_navigation_on_mariadb114() =>
        AssertNamedNavigationLineageAsync(IntegrationDatabaseTarget.MariaDb114, "First");

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task First_named_mapping_keeps_navigation_on_mariadb118() =>
        AssertNamedNavigationLineageAsync(IntegrationDatabaseTarget.MariaDb118, "First");

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task First_named_mapping_keeps_navigation_on_mariadb123() =>
        AssertNamedNavigationLineageAsync(IntegrationDatabaseTarget.MariaDb123, "First");

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Second_named_mapping_keeps_navigation_on_mysql84() =>
        AssertNamedNavigationLineageAsync(IntegrationDatabaseTarget.MySql84, "Second");

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Second_named_mapping_keeps_navigation_on_mysql97() =>
        AssertNamedNavigationLineageAsync(IntegrationDatabaseTarget.MySql97, "Second");

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Second_named_mapping_keeps_navigation_on_mariadb1011() =>
        AssertNamedNavigationLineageAsync(IntegrationDatabaseTarget.MariaDb1011, "Second");

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Second_named_mapping_keeps_navigation_on_mariadb114() =>
        AssertNamedNavigationLineageAsync(IntegrationDatabaseTarget.MariaDb114, "Second");

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Second_named_mapping_keeps_navigation_on_mariadb118() =>
        AssertNamedNavigationLineageAsync(IntegrationDatabaseTarget.MariaDb118, "Second");

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Second_named_mapping_keeps_navigation_on_mariadb123() =>
        AssertNamedNavigationLineageAsync(IntegrationDatabaseTarget.MariaDb123, "Second");

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Join_result_keeps_selected_named_mapping_on_mysql84() =>
        AssertJoinResultPreservesNamedLineageAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Join_result_keeps_selected_named_mapping_on_mysql97() =>
        AssertJoinResultPreservesNamedLineageAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Join_result_keeps_selected_named_mapping_on_mariadb1011() =>
        AssertJoinResultPreservesNamedLineageAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Join_result_keeps_selected_named_mapping_on_mariadb114() =>
        AssertJoinResultPreservesNamedLineageAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Join_result_keeps_selected_named_mapping_on_mariadb118() =>
        AssertJoinResultPreservesNamedLineageAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Join_result_keeps_selected_named_mapping_on_mariadb123() =>
        AssertJoinResultPreservesNamedLineageAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Owned_navigation_after_binding_keeps_values_on_mysql84() =>
        AssertOwnedNavigationAfterBindingAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Owned_navigation_after_binding_keeps_values_on_mysql97() =>
        AssertOwnedNavigationAfterBindingAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Owned_navigation_after_binding_keeps_values_on_mariadb1011() =>
        AssertOwnedNavigationAfterBindingAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Owned_navigation_after_binding_keeps_values_on_mariadb114() =>
        AssertOwnedNavigationAfterBindingAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Owned_navigation_after_binding_keeps_values_on_mariadb118() =>
        AssertOwnedNavigationAfterBindingAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Owned_navigation_after_binding_keeps_values_on_mariadb123() =>
        AssertOwnedNavigationAfterBindingAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Historical_navigation_after_binding_keeps_instant_on_mysql84() =>
        AssertTemporalNavigationAfterBindingAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Historical_navigation_after_binding_keeps_instant_on_mysql97() =>
        AssertTemporalNavigationAfterBindingAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Historical_navigation_after_binding_keeps_instant_on_mariadb1011() =>
        AssertTemporalNavigationAfterBindingAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Historical_navigation_after_binding_keeps_instant_on_mariadb114() =>
        AssertTemporalNavigationAfterBindingAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Historical_navigation_after_binding_keeps_instant_on_mariadb118() =>
        AssertTemporalNavigationAfterBindingAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Historical_navigation_after_binding_keeps_instant_on_mariadb123() =>
        AssertTemporalNavigationAfterBindingAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Projected_historical_navigation_keeps_instant_on_mysql84() =>
        AssertProjectedTemporalNavigationAfterBindingAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Projected_historical_navigation_keeps_instant_on_mysql97() =>
        AssertProjectedTemporalNavigationAfterBindingAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Projected_historical_navigation_keeps_instant_on_mariadb1011() =>
        AssertProjectedTemporalNavigationAfterBindingAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Projected_historical_navigation_keeps_instant_on_mariadb114() =>
        AssertProjectedTemporalNavigationAfterBindingAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Projected_historical_navigation_keeps_instant_on_mariadb118() =>
        AssertProjectedTemporalNavigationAfterBindingAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Projected_historical_navigation_keeps_instant_on_mariadb123() =>
        AssertProjectedTemporalNavigationAfterBindingAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Matching_historical_bindings_preserve_concat_bag_on_mysql84() =>
        AssertMatchingTemporalBindingsCanConcatenateAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Matching_historical_bindings_preserve_concat_bag_on_mysql97() =>
        AssertMatchingTemporalBindingsCanConcatenateAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Matching_historical_bindings_preserve_concat_bag_on_mariadb1011() =>
        AssertMatchingTemporalBindingsCanConcatenateAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Matching_historical_bindings_preserve_concat_bag_on_mariadb114() =>
        AssertMatchingTemporalBindingsCanConcatenateAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Matching_historical_bindings_preserve_concat_bag_on_mariadb118() =>
        AssertMatchingTemporalBindingsCanConcatenateAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Matching_historical_bindings_preserve_concat_bag_on_mariadb123() =>
        AssertMatchingTemporalBindingsCanConcatenateAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Current_and_historical_bindings_reject_before_io_on_mysql84() =>
        AssertCurrentAndHistoricalBindingsRejectBeforeIoAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Current_and_historical_bindings_reject_before_io_on_mysql97() =>
        AssertCurrentAndHistoricalBindingsRejectBeforeIoAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Current_and_historical_bindings_reject_before_io_on_mariadb1011() =>
        AssertCurrentAndHistoricalBindingsRejectBeforeIoAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Current_and_historical_bindings_reject_before_io_on_mariadb114() =>
        AssertCurrentAndHistoricalBindingsRejectBeforeIoAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Current_and_historical_bindings_reject_before_io_on_mariadb118() =>
        AssertCurrentAndHistoricalBindingsRejectBeforeIoAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Current_and_historical_bindings_reject_before_io_on_mariadb123() =>
        AssertCurrentAndHistoricalBindingsRejectBeforeIoAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Different_historical_instants_reject_before_io_on_mysql84() =>
        AssertDifferentTemporalBindingsRejectBeforeIoAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Different_historical_instants_reject_before_io_on_mysql97() =>
        AssertDifferentTemporalBindingsRejectBeforeIoAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Different_historical_instants_reject_before_io_on_mariadb1011() =>
        AssertDifferentTemporalBindingsRejectBeforeIoAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Different_historical_instants_reject_before_io_on_mariadb114() =>
        AssertDifferentTemporalBindingsRejectBeforeIoAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Different_historical_instants_reject_before_io_on_mariadb118() =>
        AssertDifferentTemporalBindingsRejectBeforeIoAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Different_historical_instants_reject_before_io_on_mariadb123() =>
        AssertDifferentTemporalBindingsRejectBeforeIoAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Optional_separate_owned_reference_materializes_on_mysql84() =>
        AssertOptionalSeparateOwnedMaterializationAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Optional_separate_owned_reference_materializes_on_mysql97() =>
        AssertOptionalSeparateOwnedMaterializationAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Optional_separate_owned_reference_materializes_on_mariadb1011() =>
        AssertOptionalSeparateOwnedMaterializationAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Optional_separate_owned_reference_materializes_on_mariadb114() =>
        AssertOptionalSeparateOwnedMaterializationAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Optional_separate_owned_reference_materializes_on_mariadb118() =>
        AssertOptionalSeparateOwnedMaterializationAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Optional_separate_owned_reference_materializes_on_mariadb123() =>
        AssertOptionalSeparateOwnedMaterializationAsync(IntegrationDatabaseTarget.MariaDb123);
}
