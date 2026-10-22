using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Shares the indexes responsibility without changing the test fixture.</summary>
public sealed partial class MySqlScaffoldingRoundTripTests
{
    /// <summary>
    /// Verifies index, store-type, spatial, sequence, and functional-part fidelity
    /// against MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_index_and_store_metadata_roundtrips()
    {
        await RunIndexAndStoreRoundTripAsync(IntegrationDatabaseTarget.MySql84)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies index, store-type, spatial, sequence, and functional-part fidelity
    /// against MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task MySql97_index_and_store_metadata_roundtrips()
    {
        await RunIndexAndStoreRoundTripAsync(IntegrationDatabaseTarget.MySql97)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies index, store-type, spatial, and native-sequence fidelity against MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_index_and_store_metadata_roundtrips()
    {
        await RunIndexAndStoreRoundTripAsync(IntegrationDatabaseTarget.MariaDb1011)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies index, store-type, spatial, and native-sequence fidelity against MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_index_and_store_metadata_roundtrips()
    {
        await RunIndexAndStoreRoundTripAsync(IntegrationDatabaseTarget.MariaDb114)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies index, store-type, spatial, and native-sequence fidelity against MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_index_and_store_metadata_roundtrips()
    {
        await RunIndexAndStoreRoundTripAsync(IntegrationDatabaseTarget.MariaDb118)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies index, store-type, spatial, and native-sequence fidelity against MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_index_and_store_metadata_roundtrips()
    {
        await RunIndexAndStoreRoundTripAsync(IntegrationDatabaseTarget.MariaDb123)
            .ConfigureAwait(false);
    }

    private static async Task RunIndexAndStoreRoundTripAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await CreateIndexAndStoreSchemaAsync(connectionString, target)
            .ConfigureAwait(false);

        try
        {
            await using var serviceProvider =
                ScaffoldingTestServices.CreateDesignTimeServiceProvider(
                    includeSpatialServices: true);

            using var scope = serviceProvider.CreateScope();
            var scopedServices = scope.ServiceProvider;
            var databaseOptions = new DatabaseModelFactoryOptions(
                IsMariaDb(target)
                    ?
                    [
                        IndexStoreTable,
                        IndexStoreSequence,
                    ]
                    :
                    [
                        IndexStoreTable,
                        EmulatedSequenceTable,
                    ],
                Array.Empty<string>());

            // Act
            var databaseModel = scopedServices
                .GetRequiredService<IDatabaseModelFactory>()
                .Create(connectionString, databaseOptions);

            var scaffoldedModel = scopedServices
                .GetRequiredService<IReverseEngineerScaffolder>()
                .ScaffoldModel(
                    connectionString,
                    databaseOptions,
                    new ModelReverseEngineerOptions(),
                    ScaffoldingTestServices.CreateCodeGenerationOptions(connectionString));

            // Assert
            AssertIndexAndStoreDatabaseModel(databaseModel, target);
            AssertIndexAndStoreGeneratedModel(scaffoldedModel);
        }
        finally
        {
            await DropIndexAndStoreSchemaAsync(connectionString, target)
                .ConfigureAwait(false);
        }
    }

    private static void AssertIndexAndStoreDatabaseModel(
        DatabaseModel databaseModel,
        IntegrationDatabaseTarget target
    )
    {
        var table = Assert.Single(databaseModel.Tables);

        var sequence = Assert.Single(databaseModel.Sequences);

        Assert.Equal(IndexStoreTable, table.Name);
        Assert.Equal(IndexStoreSequence, sequence.Name);
        Assert.Equal(7, sequence.StartValue);
        Assert.Equal(3, sequence.IncrementBy);
        Assert.Equal(7, sequence.MinValue);
        Assert.Equal(700, sequence.MaxValue);
        Assert.True(sequence.IsCyclic);
        Assert.Equal(
            "InnoDB",
            table.FindAnnotation(MySqlAnnotationNames.StorageEngine)
                ?.Value as string,
            ignoreCase: true);

        AssertStoreType(table, "EnumValue", "enum('new','done')");
        AssertStoreType(table, "SetValue", "set('a','b')");
        AssertStoreType(table, "TinyUnsigned", "tinyint unsigned");
        AssertStoreType(table, "MediumSigned", "mediumint");
        AssertStoreType(table, "MediumUnsigned", "mediumint unsigned");
        AssertStoreType(table, "UnsignedValue", "bigint unsigned");
        AssertStoreType(table, "Amount", "decimal(20,6) unsigned");
        AssertStoreType(table, "Moment", "datetime(3)");
        AssertStoreType(table, "Duration", "time(4)");
        AssertStoreType(table, "FixedBinary", "binary(8)");
        AssertStoreType(table, "BlobValue", "mediumblob");
        AssertStoreType(table, "TextValue", "mediumtext");
        AssertStoreType(table, "BitValue", "bit(8)");
        AssertStoreType(table, "YearValue", "year");
        AssertStoreType(table, "JsonValue", "json");

        Assert.Equal(
            "utf8mb4_bin",
            table.Columns.Single(column => column.Name == "Name").Collation,
            ignoreCase: true);

        var prefixIndex = table.Indexes.Single(index => index.Name == PrefixIndex);
        var fullTextIndex = table.Indexes.Single(index => index.Name == FullTextIndex);
        var spatialIndex = table.Indexes.Single(index => index.Name == SpatialIndex);
        var uniqueIndex = table.Indexes.Single(index => index.Name == UniqueIndex);

        Assert.Equal(
            s_prefixLengths,
            prefixIndex.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength)
                ?.Value as int[]);
        Assert.Equal(s_indexDirections, prefixIndex.IsDescending);
        Assert.True(
            fullTextIndex.FindAnnotation(MySqlAnnotationNames.FullTextIndex)
                ?.Value as bool?);
        Assert.Null(fullTextIndex.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength));
        Assert.True(
            spatialIndex.FindAnnotation(MySqlAnnotationNames.SpatialIndex)
                ?.Value as bool?);
        Assert.Null(spatialIndex.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength));
        Assert.True(uniqueIndex.IsUnique);
        Assert.Contains(
            table.UniqueConstraints,
            constraint => constraint.Name == UniqueIndex
                && constraint.Columns.Single().Name == "Code");

        if (IsMariaDb(target))
        {
            Assert.DoesNotContain(table.Indexes, index => index.Name == FunctionalIndex);

            return;
        }

        var functionalIndex = table.Indexes.Single(index => index.Name == FunctionalIndex);

        var functionalParts = Assert.IsType<MySqlScaffoldedIndexPart[]>(
            functionalIndex.FindAnnotation(MySqlAnnotationNames.ScaffoldingIndexParts)
                ?.Value);

        var functionalPart = Assert.Single(functionalParts);

        Assert.Null(functionalPart.ColumnName);
        Assert.Contains("lower", functionalPart.Expression, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertIndexAndStoreGeneratedModel(
        ScaffoldedModel scaffoldedModel
    )
    {
        var contextCode = scaffoldedModel.ContextFile.Code;

        var entityCode = scaffoldedModel
            .AdditionalFiles.Single(
                file => file.Code.Contains(
                    "class DokaScaffoldIndexStore",
                    StringComparison.Ordinal))
            .Code;

        Assert.Contains(".HasPrefixLength(32, 0)", contextCode, StringComparison.Ordinal);
        Assert.Contains(".IsDescending(false, true)", contextCode, StringComparison.Ordinal);
        Assert.Contains(".IsFullText()", contextCode, StringComparison.Ordinal);
        Assert.Contains(".IsSpatial()", contextCode, StringComparison.Ordinal);
        Assert.Contains(".UseNetTopologySuite()", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasAlternateKey(e => e.Code)", contextCode, StringComparison.Ordinal);
        Assert.Contains($"HasName(\"{UniqueIndex}\")", contextCode, StringComparison.Ordinal);
        Assert.DoesNotContain(FunctionalIndex, contextCode, StringComparison.Ordinal);
        Assert.Contains($"HasSequence(\"{IndexStoreSequence}\")", contextCode, StringComparison.Ordinal);
        Assert.Contains(".StartsAt(7L)", contextCode, StringComparison.Ordinal);
        Assert.Contains(".IncrementsBy(3)", contextCode, StringComparison.Ordinal);
        Assert.Contains(".HasMin(7L)", contextCode, StringComparison.Ordinal);
        Assert.Contains(".HasMax(700L)", contextCode, StringComparison.Ordinal);
        Assert.Contains(".IsCyclic()", contextCode, StringComparison.Ordinal);

        Assert.Contains("public byte TinyUnsigned", entityCode, StringComparison.Ordinal);
        Assert.Contains("public uint MediumUnsigned", entityCode, StringComparison.Ordinal);
        Assert.Contains("public ulong UnsignedValue", entityCode, StringComparison.Ordinal);
        Assert.Contains("public decimal Amount", entityCode, StringComparison.Ordinal);
        Assert.Contains("public DateTime Moment", entityCode, StringComparison.Ordinal);
        Assert.Contains("public TimeOnly Duration", entityCode, StringComparison.Ordinal);
        Assert.Contains("public byte[] FixedBinary", entityCode, StringComparison.Ordinal);
        Assert.Contains("public byte[] BlobValue", entityCode, StringComparison.Ordinal);
        Assert.Contains("public ulong BitValue", entityCode, StringComparison.Ordinal);
        Assert.Contains("public short YearValue", entityCode, StringComparison.Ordinal);
        Assert.Contains("Point Location", entityCode, StringComparison.Ordinal);
    }

    private static async Task CreateIndexAndStoreSchemaAsync(
        string connectionString,
        IntegrationDatabaseTarget target
    )
    {
        await DropIndexAndStoreSchemaAsync(connectionString, target)
            .ConfigureAwait(false);

        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await ExecuteNonQueryAsync(
                connection,
                $"""
                CREATE TABLE `{IndexStoreTable}` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `Name` varchar(191) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
                    `Code` varchar(64) NOT NULL,
                    `Body` text NOT NULL,
                    `EnumValue` enum('new','done') NOT NULL,
                    `SetValue` set('a','b') NOT NULL,
                    `TinyUnsigned` tinyint unsigned NOT NULL,
                    `MediumSigned` mediumint NOT NULL,
                    `MediumUnsigned` mediumint unsigned NOT NULL,
                    `UnsignedValue` bigint unsigned NOT NULL,
                    `Amount` decimal(20,6) unsigned NOT NULL,
                    `Moment` datetime(3) NOT NULL,
                    `Duration` time(4) NOT NULL,
                    `FixedBinary` binary(8) NOT NULL,
                    `BlobValue` mediumblob NOT NULL,
                    `TextValue` mediumtext NOT NULL,
                    `BitValue` bit(8) NOT NULL,
                    `YearValue` year NOT NULL,
                    `JsonValue` json NOT NULL,
                    `Location` point NOT NULL,
                    PRIMARY KEY (`Id`),
                    UNIQUE INDEX `{UniqueIndex}` (`Code`),
                    INDEX `{PrefixIndex}` (`Name`(32), `Code` DESC),
                    FULLTEXT INDEX `{FullTextIndex}` (`Body`),
                    SPATIAL INDEX `{SpatialIndex}` (`Location`)
                ) ENGINE=InnoDB DEFAULT CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
                """)
            .ConfigureAwait(false);

        if (IsMariaDb(target))
        {
            await ExecuteNonQueryAsync(
                    connection,
                    $"""
                    CREATE SEQUENCE `{IndexStoreSequence}`
                        START WITH 7
                        INCREMENT BY 3
                        MINVALUE 7
                        MAXVALUE 700
                        CYCLE;
                    """)
                .ConfigureAwait(false);

            return;
        }

        // Sources retrieved 2026-07-28:
        // MySQL: https://dev.mysql.com/doc/refman/8.4/en/information-schema-statistics-table.html
        // MariaDB: https://mariadb.com/docs/server/reference/sql-statements/data-definition/create/create-index
        // MySQL exposes functional key parts in STATISTICS.EXPRESSION. MariaDB's
        // CREATE INDEX grammar has only column key parts; a generated-column rewrite
        // would invent schema state and is not a faithful reverse-engineering result.
        await ExecuteNonQueryAsync(
                connection,
                $"""
                CREATE INDEX `{FunctionalIndex}`
                    ON `{IndexStoreTable}` ((lower(`Name`)));
                CREATE TABLE `{EmulatedSequenceTable}` (
                    `id` tinyint unsigned NOT NULL,
                    `value` bigint NOT NULL,
                    `start_value` bigint NOT NULL,
                    `increment_by` int NOT NULL,
                    `min_value` bigint NOT NULL,
                    `max_value` bigint NOT NULL,
                    `is_cyclic` boolean NOT NULL,
                    `is_called` boolean NOT NULL,
                    PRIMARY KEY (`id`),
                    CHECK (`id` = 1)
                ) ENGINE=InnoDB;
                INSERT INTO `{EmulatedSequenceTable}` (
                    `id`,
                    `value`,
                    `start_value`,
                    `increment_by`,
                    `min_value`,
                    `max_value`,
                    `is_cyclic`,
                    `is_called`
                ) VALUES (1, 7, 7, 3, 7, 700, TRUE, FALSE);
                """)
            .ConfigureAwait(false);
    }

    private static async Task DropIndexAndStoreSchemaAsync(
        string connectionString,
        IntegrationDatabaseTarget target
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        var dropSequenceSql = IsMariaDb(target)
            ? $"DROP SEQUENCE IF EXISTS `{IndexStoreSequence}`;"
            : $"DROP TABLE IF EXISTS `{EmulatedSequenceTable}`;";

        await ExecuteNonQueryAsync(
                connection,
                $"""
                {dropSequenceSql}
                DROP TABLE IF EXISTS `{IndexStoreTable}`;
                """)
            .ConfigureAwait(false);
    }
}
