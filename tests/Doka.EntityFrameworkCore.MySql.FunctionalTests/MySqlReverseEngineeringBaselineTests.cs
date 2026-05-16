using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the reverse-engineering baseline and scaffolded-code contract.
/// </summary>
public sealed class MySqlReverseEngineeringBaselineTests
{
    /// <summary>
    /// Verifies that the supported reverse-engineering baseline emits modern nullable scaffolded code
    /// and explicit provider configuration.
    /// </summary>
    [Fact]
    public void Reverse_engineering_scaffolds_modern_context_code_for_the_supported_baseline()
    {
        var scaffoldedModel = ScaffoldModel(CreatePhase2DatabaseModel(), detectedServerVersionText: "8.4.6");

        Assert.Contains("#nullable enable", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains("=> Set<", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.DoesNotContain("= null!;", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains(".UseMySql(", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains(
            "modelBuilder.HasCharSet(\"utf8mb4\")",
            scaffoldedModel.ContextFile.Code,
            StringComparison.Ordinal);
        Assert.Contains(
            "MySqlServerVersion.MySql(new System.Version(8, 4, 6))",
            scaffoldedModel.ContextFile.Code,
            StringComparison.Ordinal);
        Assert.Contains("HasCharSet(\"utf8mb4\")", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains("UseStorageEngine(\"InnoDB\")", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains(
            "#nullable enable",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
        Assert.Contains(
            "HasComputedColumnSql(\"JSON_LENGTH(`Payload`)\", true)",
            scaffoldedModel.ContextFile.Code,
            StringComparison.Ordinal);
        Assert.Contains("HasColumnType(\"json\")", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that textual GUID columns remain text properties unless the provider-specific
    /// reverse-engineering opt-in is enabled.
    /// </summary>
    [Fact]
    public void Reverse_engineering_without_text_guid_opt_in_keeps_char36_columns_as_string()
    {
        var scaffoldedModel = ScaffoldModel(
            CreateTextGuidDatabaseModel("char(36)"),
            detectedServerVersionText: "8.4.6");

        Assert.Contains(
            "public string ExternalId { get; set; }",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Guid ExternalId",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the explicit reverse-engineering opt-in scaffolds textual GUID columns
    /// as <see cref="Guid"/> properties.
    /// </summary>
    [Fact]
    public void Reverse_engineering_with_text_guid_opt_in_scaffolds_char36_columns_as_guid()
    {
        var scaffoldedModel = ScaffoldModel(
            CreateTextGuidDatabaseModel("char(36)"),
            detectedServerVersionText: "8.4.6",
            configure: options => options.ScaffoldTextGuidsAsGuids());

        Assert.Contains(
            "public Guid ExternalId { get; set; }",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
        Assert.Contains(
            "HasMySqlGuidFormat(MySqlGuidFormat.Char36)",
            scaffoldedModel.ContextFile.Code,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a plain signed tinyint column reverse-engineers to the numeric CLR type instead of text or bool.
    /// </summary>
    [Fact]
    public void Reverse_engineering_maps_plain_tinyint_columns_to_sbyte()
    {
        var scaffoldedModel = ScaffoldModel(
            CreateSingleColumnDatabaseModel("legacy_numeric_entry", "TinyValue", "tinyint"),
            detectedServerVersionText: "8.4.6");

        Assert.Contains(
            "public sbyte TinyValue { get; set; }",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public bool TinyValue { get; set; }",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public string TinyValue { get; set; }",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
    }

    private static ScaffoldedModel ScaffoldModel(
        DatabaseModel databaseModel,
        string detectedServerVersionText,
        Action<MySqlReverseEngineeringOptionsBuilder>? configure = null
    )
    {
        using var serviceProvider =
            CreateDesignTimeServiceProvider(databaseModel, detectedServerVersionText, configure);
        using var scope = serviceProvider.CreateScope();
        var scaffolder = scope.ServiceProvider.GetRequiredService<IReverseEngineerScaffolder>();

        return scaffolder.ScaffoldModel(
            "Server=localhost;Database=phase2;User ID=root;Password=secret;",
            new DatabaseModelFactoryOptions(Array.Empty<string>(), Array.Empty<string>()),
            new ModelReverseEngineerOptions(),
            new ModelCodeGenerationOptions
            {
                ContextName = "ReverseDbContext",
                ContextNamespace = "Phase2.Scaffolding",
                ModelNamespace = "Phase2.Scaffolding.Models",
                RootNamespace = "Phase2.Scaffolding",
                Language = "C#",
                ContextDir = "Generated",
                ProjectDir = "Generated",
                ConnectionString = "Server=localhost;Database=phase2;User ID=root;Password=secret;",
                SuppressConnectionStringWarning = true,
                UseNullableReferenceTypes = true,
            });
    }

    private static ServiceProvider CreateDesignTimeServiceProvider(
        DatabaseModel databaseModel,
        string detectedServerVersionText,
        Action<MySqlReverseEngineeringOptionsBuilder>? configure
    )
    {
        var services = new ServiceCollection();
#pragma warning disable EF1001
        var reporter = new OperationReporter(new OperationReportHandler(_ => { }, _ => { }, _ => { }, _ => { }));
#pragma warning restore EF1001

        services.AddEntityFrameworkDesignTimeServices(reporter, () => new ServiceCollection().BuildServiceProvider());
        services.AddEntityFrameworkDokaMySqlDesignTime(configure);
        services.AddSingleton<IDatabaseModelFactory>(serviceProvider => new StubDatabaseModelFactory(
            databaseModel,
            detectedServerVersionText,
            serviceProvider.GetRequiredService<MySqlScaffoldingContext>()));

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static DatabaseModel CreatePhase2DatabaseModel()
    {
        var databaseModel = new DatabaseModel
        {
            DatabaseName = "phase2",
            Collation = "utf8mb4_0900_ai_ci",
        };

        databaseModel.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");

        var table = new DatabaseTable
        {
            Database = databaseModel,
            Name = "phase_two_record",
        };

        table.SetAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_bin");
        table.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");
        table.SetAnnotation(MySqlAnnotationNames.StorageEngine, "InnoDB");
        databaseModel.Tables.Add(table);

        var idColumn = new DatabaseColumn
        {
            Table = table,
            Name = "Id",
            StoreType = "int",
            IsNullable = false,
            ValueGenerated = ValueGenerated.OnAdd,
        };
        var payloadColumn = new DatabaseColumn
        {
            Table = table,
            Name = "Payload",
            StoreType = "json",
            IsNullable = false,
        };
        var storedCountColumn = new DatabaseColumn
        {
            Table = table,
            Name = "StoredCount",
            StoreType = "int",
            IsNullable = false,
            ComputedColumnSql = "JSON_LENGTH(`Payload`)",
            IsStored = true,
        };

        table.Columns.Add(idColumn);
        table.Columns.Add(payloadColumn);
        table.Columns.Add(storedCountColumn);

        var primaryKey = new DatabasePrimaryKey
        {
            Table = table,
            Name = "PK_phase_two_record",
        };

        primaryKey.Columns.Add(idColumn);
        table.PrimaryKey = primaryKey;

        return databaseModel;
    }

    private static DatabaseModel CreateTextGuidDatabaseModel(
        string storeType
    ) => CreateSingleColumnDatabaseModel("legacy_guid_entry", "ExternalId", storeType);

    private static DatabaseModel CreateSingleColumnDatabaseModel(
        string tableName,
        string columnName,
        string storeType
    )
    {
        var databaseModel = new DatabaseModel
        {
            DatabaseName = "phase2",
            Collation = "utf8mb4_0900_ai_ci",
        };

        var table = new DatabaseTable
        {
            Database = databaseModel,
            Name = tableName,
        };

        databaseModel.Tables.Add(table);

        var idColumn = new DatabaseColumn
        {
            Table = table,
            Name = "Id",
            StoreType = "int",
            IsNullable = false,
            ValueGenerated = ValueGenerated.OnAdd,
        };
        var externalIdColumn = new DatabaseColumn
        {
            Table = table,
            Name = columnName,
            StoreType = storeType,
            IsNullable = false,
        };

        table.Columns.Add(idColumn);
        table.Columns.Add(externalIdColumn);

        var primaryKey = new DatabasePrimaryKey
        {
            Table = table,
            Name = "PK_legacy_guid_entry",
        };

        primaryKey.Columns.Add(idColumn);
        table.PrimaryKey = primaryKey;

        return databaseModel;
    }

    private sealed class StubDatabaseModelFactory : IDatabaseModelFactory
    {
        private readonly DatabaseModel _databaseModel;
        private readonly string _detectedServerVersionText;
        private readonly MySqlScaffoldingContext _scaffoldingContext;

        public StubDatabaseModelFactory(
            DatabaseModel databaseModel,
            string detectedServerVersionText,
            MySqlScaffoldingContext scaffoldingContext
        )
        {
            _databaseModel = databaseModel ?? throw new ArgumentNullException(nameof(databaseModel));
            _detectedServerVersionText = detectedServerVersionText
                ?? throw new ArgumentNullException(nameof(detectedServerVersionText));
            _scaffoldingContext = scaffoldingContext ?? throw new ArgumentNullException(nameof(scaffoldingContext));
        }

        public DatabaseModel Create(
            string connectionString,
            DatabaseModelFactoryOptions options
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            ArgumentNullException.ThrowIfNull(options);

            _scaffoldingContext.SetDetectedServerVersionText(_detectedServerVersionText);

            return _databaseModel;
        }

        public DatabaseModel Create(
            DbConnection connection,
            DatabaseModelFactoryOptions options
        )
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(options);

            _scaffoldingContext.SetDetectedServerVersionText(_detectedServerVersionText);

            return _databaseModel;
        }
    }
}
