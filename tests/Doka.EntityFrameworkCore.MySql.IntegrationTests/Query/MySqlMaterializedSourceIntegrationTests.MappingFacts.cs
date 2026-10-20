namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Tpt_mapping_roundtrips_on_mysql84() =>
        AssertMappingRoundtripAsync<TptLeaf>(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Tpt_mapping_roundtrips_on_mysql97() =>
        AssertMappingRoundtripAsync<TptLeaf>(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Tpt_mapping_roundtrips_on_mariadb1011() =>
        AssertMappingRoundtripAsync<TptLeaf>(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Tpt_mapping_roundtrips_on_mariadb114() =>
        AssertMappingRoundtripAsync<TptLeaf>(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Tpt_mapping_roundtrips_on_mariadb118() =>
        AssertMappingRoundtripAsync<TptLeaf>(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Tpt_mapping_roundtrips_on_mariadb123() =>
        AssertMappingRoundtripAsync<TptLeaf>(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Tpc_mapping_roundtrips_on_mysql84() =>
        AssertMappingRoundtripAsync<TpcRoot>(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Tpc_mapping_roundtrips_on_mysql97() =>
        AssertMappingRoundtripAsync<TpcRoot>(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Tpc_mapping_roundtrips_on_mariadb1011() =>
        AssertMappingRoundtripAsync<TpcRoot>(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Tpc_mapping_roundtrips_on_mariadb114() =>
        AssertMappingRoundtripAsync<TpcRoot>(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Tpc_mapping_roundtrips_on_mariadb118() =>
        AssertMappingRoundtripAsync<TpcRoot>(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Tpc_mapping_roundtrips_on_mariadb123() =>
        AssertMappingRoundtripAsync<TpcRoot>(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Entity_split_mapping_roundtrips_on_mysql84() =>
        AssertMappingRoundtripAsync<SplitMappedItem>(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Entity_split_mapping_roundtrips_on_mysql97() =>
        AssertMappingRoundtripAsync<SplitMappedItem>(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Entity_split_mapping_roundtrips_on_mariadb1011() =>
        AssertMappingRoundtripAsync<SplitMappedItem>(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Entity_split_mapping_roundtrips_on_mariadb114() =>
        AssertMappingRoundtripAsync<SplitMappedItem>(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Entity_split_mapping_roundtrips_on_mariadb118() =>
        AssertMappingRoundtripAsync<SplitMappedItem>(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Entity_split_mapping_roundtrips_on_mariadb123() =>
        AssertMappingRoundtripAsync<SplitMappedItem>(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Table_split_mapping_roundtrips_on_mysql84() =>
        AssertMappingRoundtripAsync<SharedMappedDetail>(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Table_split_mapping_roundtrips_on_mysql97() =>
        AssertMappingRoundtripAsync<SharedMappedDetail>(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Table_split_mapping_roundtrips_on_mariadb1011() =>
        AssertMappingRoundtripAsync<SharedMappedDetail>(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Table_split_mapping_roundtrips_on_mariadb114() =>
        AssertMappingRoundtripAsync<SharedMappedDetail>(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Table_split_mapping_roundtrips_on_mariadb118() =>
        AssertMappingRoundtripAsync<SharedMappedDetail>(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Table_split_mapping_roundtrips_on_mariadb123() =>
        AssertMappingRoundtripAsync<SharedMappedDetail>(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Owned_reference_mapping_roundtrips_on_mysql84() =>
        AssertMappingRoundtripAsync<OwnedMappedItem>(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Owned_reference_mapping_roundtrips_on_mysql97() =>
        AssertMappingRoundtripAsync<OwnedMappedItem>(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Owned_reference_mapping_roundtrips_on_mariadb1011() =>
        AssertMappingRoundtripAsync<OwnedMappedItem>(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Owned_reference_mapping_roundtrips_on_mariadb114() =>
        AssertMappingRoundtripAsync<OwnedMappedItem>(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Owned_reference_mapping_roundtrips_on_mariadb118() =>
        AssertMappingRoundtripAsync<OwnedMappedItem>(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Owned_reference_mapping_roundtrips_on_mariadb123() =>
        AssertMappingRoundtripAsync<OwnedMappedItem>(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Shadow_composite_mapping_roundtrips_on_mysql84() =>
        AssertMappingRoundtripAsync<ShadowMappedItem>(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Shadow_composite_mapping_roundtrips_on_mysql97() =>
        AssertMappingRoundtripAsync<ShadowMappedItem>(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Shadow_composite_mapping_roundtrips_on_mariadb1011() =>
        AssertMappingRoundtripAsync<ShadowMappedItem>(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Shadow_composite_mapping_roundtrips_on_mariadb114() =>
        AssertMappingRoundtripAsync<ShadowMappedItem>(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Shadow_composite_mapping_roundtrips_on_mariadb118() =>
        AssertMappingRoundtripAsync<ShadowMappedItem>(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Shadow_composite_mapping_roundtrips_on_mariadb123() =>
        AssertMappingRoundtripAsync<ShadowMappedItem>(IntegrationDatabaseTarget.MariaDb123);
}
