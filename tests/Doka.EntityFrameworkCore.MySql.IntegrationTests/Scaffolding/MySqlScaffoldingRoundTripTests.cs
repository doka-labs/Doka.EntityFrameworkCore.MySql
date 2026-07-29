using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies that core relational metadata survives live reverse engineering and
/// provider code generation on every supported database family and release line.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlScaffoldingRoundTripTests
{
    private const string ParentTable = "doka_scaffold_core_parent";
    private const string ChildTable = "doka_scaffold_core_child";
    private const string SummaryView = "doka_scaffold_core_summary";
    private const string CheckConstraint = "CK_doka_scaffold_optional";
    private const string UniqueConstraint = "UQ_doka_scaffold_code";
    private const string ForeignKey = "FK_doka_scaffold_child_parent";
    private const string IndexStoreTable = "doka_scaffold_index_store";
    private const string IndexStoreSequence = "doka_scaffold_sequence";
    private const string EmulatedSequenceTable = "__efsequence_" + IndexStoreSequence;
    private const string PrefixIndex = "IX_doka_scaffold_name_code";
    private const string FullTextIndex = "IX_doka_scaffold_body";
    private const string SpatialIndex = "IX_doka_scaffold_location";
    private const string UniqueIndex = "UX_doka_scaffold_code";
    private const string FunctionalIndex = "IX_doka_scaffold_lower_name";
    private static readonly int[] s_prefixLengths = [32, 0];
    private static readonly bool[] s_indexDirections = [false, true];

    /// <summary>
    /// Verifies core schema-metadata fidelity against MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_core_schema_metadata_roundtrips()
    {
        await RunCoreSchemaRoundTripAsync(IntegrationDatabaseTarget.MySql84)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies core schema-metadata fidelity against MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_core_schema_metadata_roundtrips()
    {
        await RunCoreSchemaRoundTripAsync(IntegrationDatabaseTarget.MariaDb114)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies core schema-metadata fidelity against MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_core_schema_metadata_roundtrips()
    {
        await RunCoreSchemaRoundTripAsync(IntegrationDatabaseTarget.MariaDb118)
            .ConfigureAwait(false);
    }

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
    /// Verifies compiled generated-code execution and schema reconstruction on MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_generated_context_compiles_and_executes()
    {
        await RunGeneratedContextRuntimeAsync(IntegrationDatabaseTarget.MySql84)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies compiled generated-code execution and schema reconstruction on MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_generated_context_compiles_and_executes()
    {
        await RunGeneratedContextRuntimeAsync(IntegrationDatabaseTarget.MariaDb114)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies compiled generated-code execution and schema reconstruction on MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_generated_context_compiles_and_executes()
    {
        await RunGeneratedContextRuntimeAsync(IntegrationDatabaseTarget.MariaDb118)
            .ConfigureAwait(false);
    }

    private static async Task RunCoreSchemaRoundTripAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await CreateSchemaAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            using var serviceProvider = CreateDesignTimeServiceProvider();
            using var scope = serviceProvider.CreateScope();
            var scopedServices = scope.ServiceProvider;
            var databaseOptions = new DatabaseModelFactoryOptions(
                [
                    ParentTable,
                    ChildTable,
                    SummaryView,
                ],
                Array.Empty<string>());
            var databaseModel = scopedServices
                .GetRequiredService<IDatabaseModelFactory>()
                .Create(connectionString, databaseOptions);

            AssertDatabaseModel(databaseModel);

            var scaffoldedModel = scopedServices
                .GetRequiredService<IReverseEngineerScaffolder>()
                .ScaffoldModel(
                    connectionString,
                    databaseOptions,
                    new ModelReverseEngineerOptions(),
                    CreateCodeGenerationOptions(connectionString));

            AssertGeneratedModel(scaffoldedModel);
        }
        finally
        {
            await DropSchemaAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    private static async Task RunIndexAndStoreRoundTripAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await CreateIndexAndStoreSchemaAsync(connectionString, target)
            .ConfigureAwait(false);

        try
        {
            using var serviceProvider = CreateDesignTimeServiceProvider();
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
            var databaseModel = scopedServices
                .GetRequiredService<IDatabaseModelFactory>()
                .Create(connectionString, databaseOptions);

            AssertIndexAndStoreDatabaseModel(databaseModel, target);

            var scaffoldedModel = scopedServices
                .GetRequiredService<IReverseEngineerScaffolder>()
                .ScaffoldModel(
                    connectionString,
                    databaseOptions,
                    new ModelReverseEngineerOptions(),
                    CreateCodeGenerationOptions(connectionString));

            AssertIndexAndStoreGeneratedModel(scaffoldedModel);
        }
        finally
        {
            await DropIndexAndStoreSchemaAsync(connectionString, target)
                .ConfigureAwait(false);
        }
    }

    private static async Task RunGeneratedContextRuntimeAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await CreateSchemaAsync(connectionString)
            .ConfigureAwait(false);
        await CreateIndexAndStoreSchemaAsync(connectionString, target)
            .ConfigureAwait(false);

        try
        {
            var serverVersionText = await ReadServerVersionAsync(connectionString)
                .ConfigureAwait(false);
            await using var serviceProvider = CreateDesignTimeServiceProvider();
            using var scope = serviceProvider.CreateScope();
            var scopedServices = scope.ServiceProvider;
            var databaseOptions = new DatabaseModelFactoryOptions(
                IsMariaDb(target)
                    ?
                    [
                        ParentTable,
                        ChildTable,
                        IndexStoreTable,
                        IndexStoreSequence,
                    ]
                    :
                    [
                        ParentTable,
                        ChildTable,
                        IndexStoreTable,
                        EmulatedSequenceTable,
                    ],
                Array.Empty<string>());
            var scaffoldedModel = scopedServices
                .GetRequiredService<IReverseEngineerScaffolder>()
                .ScaffoldModel(
                    connectionString,
                    databaseOptions,
                    new ModelReverseEngineerOptions(),
                    CreateCodeGenerationOptions(
                        connectionString,
                        contextName: "RuntimeSchemaContext",
                        suppressOnConfiguring: true));

            await DropSchemaAsync(connectionString)
                .ConfigureAwait(false);
            await DropIndexAndStoreSchemaAsync(connectionString, target)
                .ConfigureAwait(false);

            await GeneratedContextRuntimeVerifier
                .VerifyAsync(
                    scaffoldedModel,
                    connectionString,
                    serverVersionText)
                .ConfigureAwait(false);

            await AssertRuntimeSchemaAsync(connectionString, target)
                .ConfigureAwait(false);
        }
        finally
        {
            await DropSchemaAsync(connectionString)
                .ConfigureAwait(false);
            await DropIndexAndStoreSchemaAsync(connectionString, target)
                .ConfigureAwait(false);
        }
    }

    private static ServiceProvider CreateDesignTimeServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddEntityFrameworkDokaMySqlDesignTime();
        services.AddEntityFrameworkDokaMySqlNetTopologySuite();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static ModelCodeGenerationOptions CreateCodeGenerationOptions(
        string connectionString,
        string contextName = "CoreSchemaContext",
        bool suppressOnConfiguring = false
    ) => new()
    {
        ContextName = contextName,
        ContextNamespace = "Doka.Scaffolding",
        ModelNamespace = "Doka.Scaffolding.Models",
        RootNamespace = "Doka.Scaffolding",
        Language = "C#",
        ContextDir = "Generated",
        ProjectDir = "Generated",
        ConnectionString = connectionString,
        SuppressConnectionStringWarning = true,
        SuppressOnConfiguring = suppressOnConfiguring,
        UseNullableReferenceTypes = true,
    };

    private static void AssertDatabaseModel(
        DatabaseModel databaseModel
    )
    {
        Assert.Equal(3, databaseModel.Tables.Count);

        var parent = databaseModel.Tables.Single(table => table.Name == ParentTable);
        var child = databaseModel.Tables.Single(table => table.Name == ChildTable);
        var view = Assert.IsType<DatabaseView>(
            databaseModel.Tables.Single(table => table.Name == SummaryView));

        Assert.Equal("core table", parent.Comment);
        Assert.Equal("PRIMARY", parent.PrimaryKey?.Name);

        var optionalCount = parent.Columns.Single(column => column.Name == "OptionalCount");
        Assert.True(optionalCount.IsNullable);
        Assert.Equal("7", optionalCount.DefaultValueSql);
        Assert.Equal("optional count", optionalCount.Comment);

        var computedCount = parent.Columns.Single(column => column.Name == "ComputedCount");
        Assert.True(computedCount.IsStored);
        Assert.Contains("OptionalCount", computedCount.ComputedColumnSql, StringComparison.OrdinalIgnoreCase);

        var uniqueConstraint = Assert.Single(parent.UniqueConstraints);
        Assert.Equal(UniqueConstraint, uniqueConstraint.Name);
        Assert.Equal("Code", Assert.Single(uniqueConstraint.Columns).Name);

        var checkConstraints = Assert.IsAssignableFrom<IReadOnlyList<MySqlScaffoldedCheckConstraint>>(
            parent.FindAnnotation(MySqlAnnotationNames.ScaffoldingCheckConstraints)?.Value);
        var checkConstraint = Assert.Single(checkConstraints);
        Assert.Equal(CheckConstraint, checkConstraint.Name);
        Assert.Contains("OptionalCount", checkConstraint.Sql, StringComparison.OrdinalIgnoreCase);

        var foreignKey = Assert.Single(child.ForeignKeys);
        Assert.Equal(ForeignKey, foreignKey.Name);
        Assert.Equal(ParentTable, foreignKey.PrincipalTable.Name);
        Assert.Equal(ReferentialAction.Cascade, foreignKey.OnDelete);

        Assert.True(view.Columns.Single(column => column.Name == "OptionalCount").IsNullable);
    }

    private static void AssertGeneratedModel(
        ScaffoldedModel scaffoldedModel
    )
    {
        var contextCode = scaffoldedModel.ContextFile.Code;
        var parentCode = scaffoldedModel
            .AdditionalFiles.Single(
                file => file.Code.Contains(
                    "class DokaScaffoldCoreParent",
                    StringComparison.Ordinal))
            .Code;
        var viewCode = scaffoldedModel
            .AdditionalFiles.Single(
                file => file.Code.Contains(
                    "class DokaScaffoldCoreSummary",
                    StringComparison.Ordinal))
            .Code;

        Assert.Contains($"ToView(\"{SummaryView}\"", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasNoKey()", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasDefaultValueSql(\"7\")", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasComment(\"optional count\")", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasComment(\"core table\")", contextCode, StringComparison.Ordinal);
        Assert.Contains($"HasCheckConstraint(\"{CheckConstraint}\"", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasComputedColumnSql(", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasName(\"PRIMARY\")", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasAlternateKey(e => e.Code)", contextCode, StringComparison.Ordinal);
        Assert.Contains($"HasName(\"{UniqueConstraint}\")", contextCode, StringComparison.Ordinal);
        Assert.DoesNotContain("HasIndex(e => e.Code", contextCode, StringComparison.Ordinal);
        Assert.Contains($"HasConstraintName(\"{ForeignKey}\")", contextCode, StringComparison.Ordinal);
        Assert.Contains("public int? OptionalCount", parentCode, StringComparison.Ordinal);
        Assert.Contains("public int? OptionalCount", viewCode, StringComparison.Ordinal);
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

    private static void AssertStoreType(
        DatabaseTable table,
        string columnName,
        string expectedStoreType
    )
    {
        Assert.Equal(
            expectedStoreType,
            table.Columns.Single(column => column.Name == columnName).StoreType,
            ignoreCase: true);
    }

    private static async Task CreateSchemaAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await ExecuteNonQueryAsync(
                connection,
                $"""
                DROP VIEW IF EXISTS `{SummaryView}`;
                DROP TABLE IF EXISTS `{ChildTable}`;
                DROP TABLE IF EXISTS `{ParentTable}`;
                CREATE TABLE `{ParentTable}` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `Code` varchar(32) NOT NULL,
                    `OptionalCount` int NULL DEFAULT 7 COMMENT 'optional count',
                    `ComputedCount` int GENERATED ALWAYS AS (IFNULL(`OptionalCount`, 0) + 1) STORED,
                    PRIMARY KEY (`Id`),
                    CONSTRAINT `{UniqueConstraint}` UNIQUE (`Code`),
                    CONSTRAINT `{CheckConstraint}`
                        CHECK (`OptionalCount` IS NULL OR `OptionalCount` >= 0)
                ) ENGINE=InnoDB COMMENT='core table';
                CREATE TABLE `{ChildTable}` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `ParentId` int NOT NULL,
                    PRIMARY KEY (`Id`),
                    CONSTRAINT `{ForeignKey}`
                        FOREIGN KEY (`ParentId`) REFERENCES `{ParentTable}` (`Id`) ON DELETE CASCADE
                ) ENGINE=InnoDB;
                CREATE VIEW `{SummaryView}` AS
                    SELECT `Id`, `OptionalCount`
                    FROM `{ParentTable}`;
                """)
            .ConfigureAwait(false);
    }

    private static async Task DropSchemaAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await ExecuteNonQueryAsync(
                connection,
                $"""
                DROP VIEW IF EXISTS `{SummaryView}`;
                DROP TABLE IF EXISTS `{ChildTable}`;
                DROP TABLE IF EXISTS `{ParentTable}`;
                """)
            .ConfigureAwait(false);
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

    private static async Task<string> ReadServerVersionAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT VERSION();";

        return Convert.ToString(
                await command
                    .ExecuteScalarAsync()
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("The database did not return a server version.");
    }

    private static async Task AssertRuntimeSchemaAsync(
        string connectionString,
        IntegrationDatabaseTarget target
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        Assert.Equal(
            3L,
            await ExecuteScalarInt64Async(
                    connection,
                    $"""
                    SELECT COUNT(*)
                    FROM information_schema.TABLES
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME IN ('{ParentTable}', '{ChildTable}', '{IndexStoreTable}')
                      AND TABLE_TYPE = 'BASE TABLE';
                    """)
                .ConfigureAwait(false));
        Assert.Equal(
            1L,
            await ExecuteScalarInt64Async(
                    connection,
                    $"""
                    SELECT COUNT(*)
                    FROM information_schema.REFERENTIAL_CONSTRAINTS
                    WHERE CONSTRAINT_SCHEMA = DATABASE()
                      AND CONSTRAINT_NAME = '{ForeignKey}';
                    """)
                .ConfigureAwait(false));
        Assert.Equal(
            1L,
            await ExecuteScalarInt64Async(
                    connection,
                    $"""
                    SELECT COUNT(*)
                    FROM `{ParentTable}`
                    WHERE `OptionalCount` = 9
                      AND `ComputedCount` = 10;
                    """)
                .ConfigureAwait(false));
        Assert.Equal(
            4L,
            await ExecuteScalarInt64Async(
                    connection,
                    $"""
                    SELECT COUNT(DISTINCT INDEX_NAME)
                    FROM information_schema.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = '{IndexStoreTable}'
                      AND INDEX_NAME IN ('{PrefixIndex}', '{FullTextIndex}', '{SpatialIndex}', '{UniqueIndex}');
                    """)
                .ConfigureAwait(false));

        var sequenceExistsSql = IsMariaDb(target)
            ?
            $"""
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = '{IndexStoreSequence}'
              AND TABLE_TYPE = 'SEQUENCE';
            """
            :
            $"""
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = '{EmulatedSequenceTable}'
              AND TABLE_TYPE = 'BASE TABLE';
            """;

        Assert.Equal(
            1L,
            await ExecuteScalarInt64Async(connection, sequenceExistsSql)
                .ConfigureAwait(false));
    }

    private static async Task<long> ExecuteScalarInt64Async(
        MySqlConnection connection,
        string commandText
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;

        return Convert.ToInt64(
            await command
                .ExecuteScalarAsync()
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static bool IsMariaDb(
        IntegrationDatabaseTarget target
    ) => target is IntegrationDatabaseTarget.MariaDb114
        or IntegrationDatabaseTarget.MariaDb118;

    private static async Task ExecuteNonQueryAsync(
        MySqlConnection connection,
        string commandText
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;

        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }
}
