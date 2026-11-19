namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    private static readonly Guid s_firstGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid s_secondGuid = Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100");
    private static readonly Guid s_secondNativeGuid = Guid.Parse("01234567-89ab-4cde-8f01-23456789abcd");
    private static readonly DateTime s_firstMoment = new DateTime(2026, 1, 2, 3, 4, 5).AddTicks(1234560);

    private sealed partial class GraphStore
    {
        /// <summary>Seeds known native values independently of EF conversions or feature translation.</summary>
        private Task SeedAsync(
            CancellationToken cancellationToken
        ) => ExecuteAsync(
            "INSERT INTO `GraphTargets` (`Id`,`IsVisible`,`Name`) VALUES (10,1,'visible'),(20,0,'hidden'); "
            + "INSERT INTO `GraphVertices` (`Id`,`TenantId`,`TargetId`,`Name`,`FieldNote`,`ShadowNumber`,`Kind`,`Tag`) VALUES "
            + "(1,1,10,'one','field-1',101,'base',NULL),(2,1,20,'two','field-2',102,'base',NULL),"
            + "(3,2,10,'three','field-3',103,'special','derived-three'),(4,1,10,'four','field-4',104,'special','derived-four'),"
            + "(5,1,10,'five','field-5',105,'base',NULL),(6,1,10,'six','field-6',106,'base',NULL); "
            + "INSERT INTO `GraphEdges` (`Id`,`ParentId`,`ChildId`,`TenantId`) VALUES "
            + "(1,1,2,1),(2,1,3,1),(3,2,4,1),(4,3,5,1),(5,3,6,1); "
            + "INSERT INTO `GraphFacets` (`Id`,`BinaryId`,`CharId`,`NativeId`,`OptionalId`,`Amount`,`Moment`,`OptionalNumber`,`State`,`Code`,`Label`) VALUES "
            + "(1,X'00112233445566778899AABBCCDDEEFF','00112233-4455-6677-8899-aabbccddeeff',"
            + "'00112233-4455-6677-8899-aabbccddeeff',NULL,12.3456,'2026-01-02 03:04:05.123456',NULL,'Ready','code-one',_utf8mb4 0xC3A9F09F9880),"
            + "(2,X'FFEEDDCCBBAA99887766554433221100','ffeeddcc-bbaa-9988-7766-554433221100',"

            // WHY: Native UUID validates UUID syntax more narrowly than arbitrary Binary16/Char36
            // GUID bits. Keep that binary/text vector intact and use an independent RFC4122 UUID here.
            + "'01234567-89ab-4cde-8f01-23456789abcd','ffeeddcc-bbaa-9988-7766-554433221100',NULL,"
            + "'2026-01-02 03:04:05.123456',7,'Done','code-two','alpha'); "
            + "INSERT INTO `GraphLatinRows` (`Id`,`Value`) VALUES(1,'ascii');",
            cancellationToken);

        /// <summary>Changes only the fixture's explicit edge occurrences for a named graph topology.</summary>
        internal Task SetEdgesAsync(
            string topology,
            CancellationToken cancellationToken
        )
        {
            var edges = topology switch
            {
                "chain" => "(1,1,2,1),(2,2,3,1),(3,3,4,1)",
                "branch" => "(1,1,2,1),(2,1,3,1),(3,2,4,1),(4,3,5,1),(5,3,6,1)",
                "diamond" => "(1,1,2,1),(2,1,3,1),(3,2,4,1),(4,3,4,1)",
                "long-cycle" => "(1,1,2,1),(2,2,3,1),(3,3,1,1)",
                "self-cycle" => "(1,1,1,1)",
                "empty" => null,
                _ => throw new ArgumentOutOfRangeException(nameof(topology)),
            };

            return ExecuteAsync("DELETE FROM `GraphEdges`;"
                + (edges is null
                    ? string.Empty
                    : " INSERT INTO `GraphEdges` (`Id`,`ParentId`,`ChildId`,`TenantId`) VALUES " + edges + ";"),
                cancellationToken);
        }
    }
}
