namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Qualifies option defaults, physical inheritance and reverse engineering on every supported engine.</summary>
[IntegrationDatabaseTest]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed partial class MySqlDefaultTextOptionsIntegrationTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Captures native schema definitions alongside canonical physical-facet assertions.</summary>
    /// <param name="output">The output bound to this independently reported test case.</param>
    public MySqlDefaultTextOptionsIntegrationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Returns one independently reported case for each selected supported engine.</summary>
    public static IEnumerable<TheoryDataRow<string>> SelectedTargets => IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => IntegrationTestEnvironment
            .GetSupportedTargets()
            .Contains(target))
        .Select(target => new TheoryDataRow<string>(target.ToString()));

    /// <summary>Returns each selected engine with ordinary text and Char36 GUID overrides separately.</summary>
    public static IEnumerable<TheoryDataRow<string, bool>> CentralizationCases => SelectedTargets
        .SelectMany(target => new[]
        {
            new TheoryDataRow<string, bool>(target.Data, false), new TheoryDataRow<string, bool>(target.Data, true),
        });

    /// <summary>Migration installs the application defaults while preserving explicit and JSON/binary exceptions.</summary>
    /// <param name="target">The selected engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Option_defaults_migrate_to_database_tables_and_columns_without_overwriting_type_contracts(
        string target
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await using var context = CreateContext(database);

        // Act
        await ApplyModelAsync(context, null, CancellationToken.None);
        var databaseCollation =
            await ReadScalarAsync(database.Connection, "SELECT @@collation_database;", CancellationToken.None);

        var inherited =
            await ReadColumnFactAsync(database.Connection, MainTable, "Name", false, CancellationToken.None);

        var tableOverride =
            await ReadColumnFactAsync(database.Connection, OverrideTable, "Name", false, CancellationToken.None);

        var columnOverride = await ReadColumnFactAsync(database.Connection,
            OverrideTable,
            "ExternalText",
            false,
            CancellationToken.None);

        var columnCharSet = await ReadColumnFactAsync(database.Connection,
            OverrideTable,
            "ExternalText",
            true,
            CancellationToken.None);

        var ordinaryNamedJson =
            await ReadColumnFactAsync(database.Connection, MainTable, "Json", false, CancellationToken.None);

        var binary = await ReadColumnFactAsync(database.Connection, MainTable, "Bytes", false, CancellationToken.None);
        var document =
            await ReadColumnFactAsync(database.Connection, MainTable, "Document", false, CancellationToken.None);

        // Assert
        Assert.Equal(UnicodeCollation, databaseCollation);
        Assert.Equal(UnicodeCollation, inherited);
        Assert.Equal(BinaryCollation, tableOverride);
        Assert.Equal("latin1_swedish_ci", columnOverride);
        Assert.Equal("latin1", columnCharSet);
        Assert.Equal(UnicodeCollation, ordinaryNamedJson);
        Assert.Null(binary);

        if (IntegrationTestEnvironment.GetServerVersion(database.Target)
            .IsMariaDb)
        {
            Assert.Equal(BinaryCollation, document);
        }
    }

    /// <summary>Reverse engineering reports actual exceptions rather than applying one application default everywhere.</summary>
    /// <param name="target">The selected engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Reverse_engineering_preserves_physical_database_table_column_and_json_facets(
        string target
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await using var context = CreateContext(database);
        await ApplyModelAsync(context, null, CancellationToken.None);
        await using var services = ScaffoldingTestServices.CreateDesignTimeServiceProvider();
        using var scope = services.CreateScope();

        // Act
        var metadata = ReverseEngineer(database, scope.ServiceProvider);
        var scaffold = scope
            .ServiceProvider
            .GetRequiredService<IReverseEngineerScaffolder>()
            .ScaffoldModel(
                database.Connection.ConnectionString,
                new DatabaseModelFactoryOptions([MainTable, OverrideTable], []),
                new ModelReverseEngineerOptions(),
                ScaffoldingTestServices.CreateCodeGenerationOptions(database.Connection.ConnectionString));

        // Assert
        Assert.Equal(UnicodeCollation, metadata.Collation);
        Assert.Equal("utf8mb4",
            metadata.FindAnnotation(MySqlAnnotationNames.CharSet)
                ?.Value);

        var exceptions = metadata.Tables.Single(table => table.Name == OverrideTable);
        Assert.Equal(BinaryCollation,
            exceptions.FindAnnotation(RelationalAnnotationNames.Collation)
                ?.Value);

        Assert.Equal("latin1_swedish_ci",
            exceptions.Columns.Single(column => column.Name == "ExternalText")
                .Collation);

        var main = metadata.Tables.Single(table => table.Name == MainTable);
        Assert.Equal("json",
            main.Columns.Single(column => column.Name == "Document")
                .StoreType);

        Assert.Equal("longtext",
            main.Columns.Single(column => column.Name == "Json")
                .StoreType);

        Assert.Contains("UseCollation(\"utf8mb4_unicode_ci\")", scaffold.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains("latin1_swedish_ci", scaffold.ContextFile.Code, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultCollation(", scaffold.ContextFile.Code, StringComparison.Ordinal);
    }

    /// <summary>Changing the database default does not convert existing text columns or rewrite their contents.</summary>
    /// <param name="target">The selected engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Changing_only_database_default_preserves_existing_column_collation_and_data(
        string target
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await using var source = CreateContext(database);
        await ApplyModelAsync(source, null, CancellationToken.None);
        source.Add(new TextRow
        {
            Id = 1,
            Name = "Caf\u00e9 'Test'",
            ExternalId = s_externalId,
        });

        await source.SaveChangesAsync(CancellationToken.None);
        await using var changed = CreateContext(database, collation: BinaryCollation);

        // Act
        await ApplyModelAsync(changed, source, CancellationToken.None);
        var currentDefault =
            await ReadScalarAsync(database.Connection, "SELECT @@collation_database;", CancellationToken.None);

        var existingColumn =
            await ReadColumnFactAsync(database.Connection, MainTable, "Name", false, CancellationToken.None);

        var readback = await changed
            .Set<TextRow>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(BinaryCollation, currentDefault);
        Assert.Equal(UnicodeCollation, existingColumn);
        Assert.Equal("Caf\u00e9 'Test'", readback.Name);
        Assert.Equal(s_externalId, readback.ExternalId);
    }

    /// <summary>Applying recorded historical operations ignores the runtime option's different collation.</summary>
    /// <param name="target">The selected engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Historical_operation_values_are_not_replaced_by_current_runtime_options(
        string target
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await using var context = CreateContext(database);
        var historical = new AlterDatabaseOperation { Collation = BinaryCollation };
        historical.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");

        // Act
        await ExecuteOperationsAsync(context, [historical], CancellationToken.None);
        var actual = await ReadScalarAsync(database.Connection, "SELECT @@collation_database;", CancellationToken.None);

        // Assert
        Assert.Equal(BinaryCollation, actual);
        Assert.Equal(UnicodeCollation,
            context
                .GetService<IDesignTimeModel>()
                .Model
                .GetCollation());
    }

    /// <summary>Removing an explicit override follows the real table default, not the equal global name.</summary>
    /// <param name="target">The selected engine profile.</param>
    /// <param name="guidColumn">Whether the override belongs to a Char36 Guid.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CentralizationCases))]
    public async Task Centralizing_column_collation_under_different_table_default_changes_physical_definition(
        string target,
        bool guidColumn
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        var original = new TextSchema
        {
            TableCollation = BinaryCollation,
            ExplicitColumnCollation = true,
            GuidColumn = guidColumn,
        };

        await using var source = CreateContext(database, original);
        await ApplyModelAsync(source, null, CancellationToken.None);
        source.Add(new TextRow
        {
            Id = 1,
            Name = "original",
            ExternalId = s_externalId,
        });

        await source.SaveChangesAsync(CancellationToken.None);
        var originalDefinition = await ReadCreateTableAsync(database.Connection, MainTable, CancellationToken.None);
        await using var inherited = CreateContext(database, original with { ExplicitColumnCollation = false });

        // Act
        await ApplyModelAsync(inherited, source, CancellationToken.None);
        var actual = await ReadColumnFactAsync(database.Connection,
            MainTable,
            guidColumn
                ? "ExternalId"
                : "Name",
            false,
            CancellationToken.None);

        var row = await inherited
            .Set<TextRow>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        var changedDefinition = await ReadCreateTableAsync(database.Connection, MainTable, CancellationToken.None);

        // Assert
        Assert.Equal(BinaryCollation, actual);
        Assert.Equal("original", row.Name);
        Assert.Equal(s_externalId, row.ExternalId);
        Assert.Contains(UnicodeCollation, originalDefinition, StringComparison.Ordinal);
        Assert.DoesNotContain(UnicodeCollation, changedDefinition, StringComparison.Ordinal);
    }

    /// <summary>A regular metadata migration may preserve today's physical schema while recording inherited intent.</summary>
    /// <param name="target">The selected engine profile.</param>
    /// <param name="guidColumn">Whether the formerly explicit property is a Char36 Guid.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CentralizationCases))]
    public async Task Centralization_under_matching_table_default_preserves_current_physical_schema_and_values(
        string target,
        bool guidColumn
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        var original = new TextSchema
        {
            ExplicitColumnCollation = true,
            GuidColumn = guidColumn,
        };

        await using var source = CreateContext(database, original);
        await ApplyModelAsync(source, null, CancellationToken.None);
        source.Add(new TextRow
        {
            Id = 1,
            Name = "unchanged",
            ExternalId = s_externalId,
        });

        await source.SaveChangesAsync(CancellationToken.None);
        var before = await ReadCreateTableAsync(database.Connection, MainTable, CancellationToken.None);
        var beforeFacts =
            await ReadPhysicalColumnDefinitionsAsync(database.Connection, MainTable, CancellationToken.None);

        await using var inherited = CreateContext(database, original with { ExplicitColumnCollation = false });

        // Act
        await ApplyModelAsync(inherited, source, CancellationToken.None);
        var after = await ReadCreateTableAsync(database.Connection, MainTable, CancellationToken.None);
        var afterFacts =
            await ReadPhysicalColumnDefinitionsAsync(database.Connection, MainTable, CancellationToken.None);

        var collation = await ReadColumnFactAsync(database.Connection,
            MainTable,
            guidColumn
                ? "ExternalId"
                : "Name",
            false,
            CancellationToken.None);

        var row = await inherited
            .Set<TextRow>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        _output.WriteLine("Native table before centralization:\n" + before);
        _output.WriteLine("Native table after centralization:\n" + after);

        // Assert
        // WHY: MySQL may omit redundant CHARACTER SET text after MODIFY; physical facets are the schema-equivalence oracle.
        Assert.Equal(beforeFacts, afterFacts);
        Assert.Equal(UnicodeCollation, collation);
        Assert.Equal("unchanged", row.Name);
        Assert.Equal(s_externalId, row.ExternalId);
    }

    /// <summary>The normal inverse migration restores the explicit physical collation and existing values.</summary>
    /// <param name="target">The selected engine profile.</param>
    /// <param name="guidColumn">Whether the changed column is a Char36 Guid.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CentralizationCases))]
    public async Task Inverse_centralization_migration_restores_explicit_column_and_scaffolded_facts(
        string target,
        bool guidColumn
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        var original = new TextSchema
        {
            TableCollation = BinaryCollation,
            ExplicitColumnCollation = true,
            GuidColumn = guidColumn,
        };

        await using var explicitColumn = CreateContext(database, original);
        await ApplyModelAsync(explicitColumn, null, CancellationToken.None);
        explicitColumn.Add(new TextRow
        {
            Id = 1,
            Name = "original",
            ExternalId = s_externalId,
        });

        await explicitColumn.SaveChangesAsync(CancellationToken.None);
        await using var inherited = CreateContext(database, original with { ExplicitColumnCollation = false });
        await ApplyModelAsync(inherited, explicitColumn, CancellationToken.None);
        var inheritedDefinition = await ReadCreateTableAsync(database.Connection, MainTable, CancellationToken.None);
        await using var services = ScaffoldingTestServices.CreateDesignTimeServiceProvider();
        using var scope = services.CreateScope();

        // Act
        await ApplyModelAsync(explicitColumn, inherited, CancellationToken.None);
        var metadata = ReverseEngineer(database, scope.ServiceProvider);
        var row = await inherited
            .Set<TextRow>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        var restoredDefinition = await ReadCreateTableAsync(database.Connection, MainTable, CancellationToken.None);

        // Assert
        var table = metadata.Tables.Single(item => item.Name == MainTable);
        var column = table.Columns.Single(item => item.Name
            == (guidColumn
                ? "ExternalId"
                : "Name"));

        Assert.Equal(UnicodeCollation, column.Collation);
        Assert.Equal(BinaryCollation,
            table.FindAnnotation(RelationalAnnotationNames.Collation)
                ?.Value);

        Assert.Equal("original", row.Name);
        Assert.Equal(s_externalId, row.ExternalId);
        Assert.DoesNotContain(UnicodeCollation, inheritedDefinition, StringComparison.Ordinal);
        Assert.Contains(UnicodeCollation, restoredDefinition, StringComparison.Ordinal);
    }

    /// <summary>Native CHARACTER SET alone selects its own default, which is not necessarily the table's collation.</summary>
    /// <param name="target">The selected engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Charset_only_column_is_not_normalized_using_global_collation_name_alone(
        string target
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await ExecuteAsync(database.Connection,
            "CREATE TABLE `CharsetOnly` (`Id` int NOT NULL PRIMARY KEY, `Value` varchar(64) CHARACTER SET utf8mb4 NOT NULL) "
            + "CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;",
            CancellationToken.None);

        await using var services = ScaffoldingTestServices.CreateDesignTimeServiceProvider();
        using var scope = services.CreateScope();

        // Act
        var column =
            await ReadColumnFactAsync(database.Connection, "CharsetOnly", "Value", false, CancellationToken.None);

        var nativeDefault = await ReadScalarAsync(database.Connection,
            "SELECT DEFAULT_COLLATE_NAME FROM information_schema.CHARACTER_SETS WHERE CHARACTER_SET_NAME = 'utf8mb4';",
            CancellationToken.None);

        var tableDefault = await ReadScalarAsync(database.Connection,
            "SELECT TABLE_COLLATION FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'CharsetOnly';",
            CancellationToken.None);

        var physicalModel = scope
            .ServiceProvider
            .GetRequiredService<IDatabaseModelFactory>()
            .Create(
                database.Connection.ConnectionString,
                new DatabaseModelFactoryOptions(["CharsetOnly"], []));

        // Assert
        Assert.Equal(nativeDefault, column);
        Assert.Equal(UnicodeCollation, tableDefault);
        Assert.NotEqual(tableDefault, column);
        Assert.Equal(nativeDefault,
            physicalModel
                .Tables
                .Single()
                .Columns
                .Single(item => item.Name == "Value")
                .Collation);
    }

    /// <summary>Opting into a default never silently repairs a deliberately different existing column.</summary>
    /// <param name="target">The selected engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Applying_database_default_preserves_unmodelled_brownfield_table_and_column_drift(
        string target
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await ExecuteAsync(database.Connection,
            "CREATE TABLE `LegacyText` (`Id` int NOT NULL PRIMARY KEY, `Value` varchar(64) COLLATE latin1_swedish_ci NOT NULL) "
            + "CHARACTER SET latin1 COLLATE latin1_bin; INSERT INTO `LegacyText` VALUES (1, 'legacy');",
            CancellationToken.None);

        await using var context = CreateContext(database);
        var operation = new AlterDatabaseOperation { Collation = UnicodeCollation };
        operation.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");

        // Act
        await ExecuteOperationsAsync(context, [operation], CancellationToken.None);
        var actual =
            await ReadColumnFactAsync(database.Connection, "LegacyText", "Value", false, CancellationToken.None);

        var value = await ReadScalarAsync(database.Connection,
            "SELECT `Value` FROM `LegacyText` WHERE `Id` = 1;",
            CancellationToken.None);

        // Assert
        Assert.Equal("latin1_swedish_ci", actual);
        Assert.Equal("legacy", value);
    }

    /// <summary>A plain query uses native collation semantics without hidden metadata, DDL or COLLATE commands.</summary>
    /// <param name="target">The selected engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Query_uses_column_collation_without_extra_provider_commands(
        string target
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await using var setup = CreateContext(database);
        await ApplyModelAsync(setup, null, CancellationToken.None);
        setup
            .Set<TextRow>()
            .AddRange(new TextRow
                {
                    Id = 1,
                    Name = "Alpha",
                    ExternalId = s_externalId,
                },
                new TextRow
                {
                    Id = 2,
                    Name = "alpha",
                    ExternalId = Guid.Empty,
                });

        await setup.SaveChangesAsync(CancellationToken.None);
        var recorder = new QueryRecorder();
        await using var query = CreateContext(database, interceptor: recorder);

        // Act
        var values = await query
            .Set<TextRow>()
            .AsNoTracking()
            .Where(row => row.Name == "alpha")
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToListAsync(CancellationToken.None);

        var indexColumns = await ReadScalarAsync(database.Connection,
            "SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() "
            + "AND TABLE_NAME = 'DefaultTextRecords' AND INDEX_NAME = 'IX_DefaultText_Name' AND COLUMN_NAME = 'Name';",
            CancellationToken.None);

        // Assert
        Assert.Equal([1, 2], values);
        Assert.Equal("1", indexColumns);
        var sql = Assert.Single(recorder.Statements);
        Assert.DoesNotContain("COLLATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SET NAMES", sql, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("SELECT", sql, StringComparison.Ordinal);
    }

    /// <summary>Updates preserve escaped Unicode, JSON, GUID and binary values under the selected application defaults.</summary>
    /// <param name="target">The selected engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Updating_values_under_text_defaults_preserves_json_guid_binary_and_unicode_data(
        string target
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await using var context = CreateContext(database);
        await ApplyModelAsync(context, null, CancellationToken.None);
        var row = new TextRow
        {
            Id = 1,
            Name = "before",
            ExternalId = Guid.Empty,
        };

        context.Add(row);
        await context.SaveChangesAsync(CancellationToken.None);
        row.Name = "Caf\u00e9 'Test' \\ slash";
        row.ExternalId = s_externalId;
        row.Document = "{\"value\":\"Caf\u00e9 'Test'\"}";
        row.Json = "ordinary 'text'";
        row.Bytes = [0, 39, 92, 255];

        // Act
        var affected = await context.SaveChangesAsync(CancellationToken.None);
        var actual = await context
            .Set<TextRow>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, affected);
        Assert.Equal(row.Name, actual.Name);
        Assert.Equal(s_externalId, actual.ExternalId);
        Assert.Equal(row.Json, actual.Json);
        Assert.Equal(row.Bytes, actual.Bytes);
        using var document = JsonDocument.Parse(actual.Document);
        Assert.Equal("Caf\u00e9 'Test'",
            document
                .RootElement
                .GetProperty("value")
                .GetString());
    }

    /// <summary>An unavailable but syntactically valid option fails natively instead of selecting a silent fallback.</summary>
    /// <param name="target">The selected engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Unknown_native_collation_is_not_replaced_by_a_server_default(
        string target
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await using var context = CreateContext(database, collation: "utf8mb4_doka_missing_ci");

        // Act
        var exception =
            await Assert.ThrowsAsync<MySqlException>(() => ApplyModelAsync(context, null, CancellationToken.None));

        var actual = await ReadScalarAsync(database.Connection, "SELECT @@collation_database;", CancellationToken.None);

        // Assert
        Assert.Equal(1273, exception.Number);
        Assert.Equal(BinaryCollation, actual);
    }
}
