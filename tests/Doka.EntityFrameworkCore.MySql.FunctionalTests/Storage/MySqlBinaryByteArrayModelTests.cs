using System.Data;
using System.Linq.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies fixed binary byte-array metadata across runtime and design-time model generation.
/// </summary>
public sealed class MySqlBinaryByteArrayModelTests
{
    /// <summary>Explicit fixed-width byte arrays retain their declared SQL type and reader mapping.</summary>
    [Theory]
    [InlineData(nameof(BinaryRecord.Payload), "binary(16)")]
    [InlineData(nameof(BinaryRecord.ShortPayload), "binary(8)")]
    [InlineData(nameof(BinaryRecord.LongPayload), "binary(32)")]
    [InlineData(nameof(BinaryRecord.MisleadingSizePayload), "binary(16)")]
    [InlineData(nameof(BinaryRecord.ConvertedGuid), "binary(16)")]
    public void Explicit_fixed_binary_properties_use_the_byte_array_reader(
        string propertyName,
        string storeType
    )
    {
        // Arrange
        using var context = new BinaryByteArrayModelContext(CreateOptions());

        // Act
        var mapping = FindProperty(context.Model, propertyName)
            .GetRelationalTypeMapping();

        // Assert
        Assert.IsType<MySqlBinaryByteArrayTypeMapping>(mapping);
        Assert.Equal(storeType, mapping.StoreType);
        Assert.Equal(
            typeof(object),
            mapping.GetDataReaderMethod()
                .ReturnType);
    }

    /// <summary>Non-fixed binary mappings retain the connector-safe implicit size and BLOB choices.</summary>
    [Theory]
    [InlineData(nameof(BinaryRecord.ImplicitPayload), "varbinary(17)")]
    [InlineData(nameof(BinaryRecord.BlobPayload), "longblob")]
    public void Other_byte_array_store_families_keep_their_existing_mapping(
        string propertyName,
        string storeType
    )
    {
        // Arrange
        using var context = new BinaryByteArrayModelContext(CreateOptions());

        // Act
        var mapping = FindProperty(context.Model, propertyName)
            .GetRelationalTypeMapping();

        // Assert
        Assert.IsNotType<MySqlBinaryByteArrayTypeMapping>(mapping);
        Assert.Equal(storeType, mapping.StoreType);
        Assert.Equal(typeof(byte[]), mapping.ClrType);
    }

    /// <summary>Byte-array reader adaptation does not change either public GUID storage default.</summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Binary16, "binary(16)", typeof(MySqlGuidBinaryTypeMapping))]
    [InlineData(MySqlGuidFormat.Char36, "char(36)", typeof(MySqlGuidStringTypeMapping))]
    public void Guid_default_remains_independent_of_explicit_byte_array_storage(
        MySqlGuidFormat defaultFormat,
        string storeType,
        Type mappingType
    )
    {
        // Arrange
        using var context = new BinaryByteArrayModelContext(CreateOptions(defaultFormat));

        // Act
        var mapping = FindProperty(context.Model, nameof(BinaryRecord.RegularGuid))
            .GetRelationalTypeMapping();

        // Assert
        Assert.Equal(mappingType, mapping.GetType());
        Assert.Equal(storeType, mapping.StoreType);
        Assert.Equal(typeof(Guid), mapping.ClrType);
    }

    /// <summary>
    /// Generated runtime models preserve the custom reader after EF clones the public mapping default.
    /// </summary>
    [Fact]
    public void Generated_compiled_model_retains_fixed_binary_and_converter_mappings()
    {
        // Arrange
        using var context = new BinaryByteArrayModelContext(CreateOptions());
        using var services = CreateDesignTimeServices();
        var options = new CompiledModelCodeGenerationOptions
        {
            ContextType = typeof(BinaryByteArrayModelContext),
            ModelNamespace = "Doka.GeneratedBinaryModels",
            Language = "C#",
            UseNullableReferenceTypes = true,
        };

        var generator = services
            .GetRequiredService<ICompiledModelCodeGeneratorSelector>()
            .Select(options);

        // Act
        var files = generator.GenerateModel(
            context.GetService<IDesignTimeModel>()
                .Model,
            options);

        var assembly = Compile(files.Select(file => file.Code));
        var generatedType = assembly
            .GetTypes()
            .Single(type => type.IsSubclassOf(typeof(RuntimeModel)));

        var model = (IModel)generatedType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        using var compiledContext = new BinaryByteArrayModelContext(
            MySqlFunctionalTestOptions
                .CreateTransientBuilder<BinaryByteArrayModelContext>()
                .UseMySql(
                    "Server=localhost;Database=doka;User ID=root;Password=password;",
                    MySqlServerVersion.MySql(new Version(8, 4, 0)))
                .UseModel(model)
                .Options);

        var sql = compiledContext
            .Set<BinaryRecord>()
            .Select(row => row.Payload)
            .ToQueryString();

        var payloadMapping = FindProperty(compiledContext.Model, nameof(BinaryRecord.Payload))
            .GetRelationalTypeMapping();

        var clonedMapping = payloadMapping.WithStoreTypeAndSize("binary(16)", size: null);
        var actual = ReadProviderValue(clonedMapping, Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));

        // Assert
        Assert.IsType<MySqlBinaryByteArrayTypeMapping>(payloadMapping);
        Assert.Equal(
            FindProperty(context.Model, nameof(BinaryRecord.Payload))
                .GetRelationalTypeMapping()
                .Size,
            payloadMapping.Size);

        Assert.IsType<MySqlBinaryByteArrayTypeMapping>(clonedMapping);
        Assert.Null(clonedMapping.Size);
        Assert.Equal("binary(16)", clonedMapping.StoreType);
        Assert.Equal(BinaryRecord.DefaultPayload, actual);

        var convertedMapping = FindProperty(compiledContext.Model, nameof(BinaryRecord.ConvertedGuid))
            .GetRelationalTypeMapping();

        Assert.IsType<MySqlBinaryByteArrayTypeMapping>(convertedMapping);
        Assert.Equal(
            typeof(object),
            convertedMapping.GetDataReaderMethod()
                .ReturnType);

        Assert.IsType<GuidToBytesConverter>(convertedMapping.Converter);
        Assert.Equal("binary(16)", convertedMapping.StoreType);
        Assert.Contains("`Payload`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Snapshots and migration designers preserve byte-array CLR values and the original fixed binary declaration.
    /// </summary>
    [Fact]
    public void Snapshot_and_designer_roundtrip_without_binary_schema_changes()
    {
        // Arrange
        using var context = new BinaryByteArrayModelContext(CreateOptions());
        using var services = CreateDesignTimeServices();
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        var generator = services
            .GetRequiredService<IMigrationsCodeGeneratorSelector>()
            .Select("C#");

        const string generatedNamespace = "Doka.GeneratedBinaryMigrations";

        // Act
        var snapshotCode = generator.GenerateSnapshot(
            generatedNamespace,
            typeof(BinaryByteArrayModelContext),
            "BinarySnapshot",
            model);

        var migrationCode = generator.GenerateMigration(generatedNamespace, "BinaryMigration", [], []);
        var designerCode = generator.GenerateMetadata(
            generatedNamespace,
            typeof(BinaryByteArrayModelContext),
            "BinaryMigration",
            "20261105000000_BinaryMigration",
            model);

        var assembly = Compile([snapshotCode, migrationCode, designerCode]);
        var snapshot = (ModelSnapshot)Activator.CreateInstance(
            assembly.GetType($"{generatedNamespace}.BinarySnapshot", throwOnError: true)!)!;

        var migration = (Migration)Activator.CreateInstance(
            assembly.GetType($"{generatedNamespace}.BinaryMigration", throwOnError: true)!)!;

        var snapshotModel = InitializeDesignModel(context, snapshot.Model);
        var designerModel = InitializeDesignModel(context, migration.TargetModel);
        var differ = context.GetService<IMigrationsModelDiffer>();
        var snapshotDifferences = differ.GetDifferences(snapshotModel.GetRelationalModel(), model.GetRelationalModel());
        var designerDifferences = differ.GetDifferences(designerModel.GetRelationalModel(), model.GetRelationalModel());

        // Assert
        Assert.Empty(snapshotDifferences);
        Assert.Empty(designerDifferences);
        Assert.Contains("Property<byte[]>(\"Payload\")", snapshotCode, StringComparison.Ordinal);
        Assert.Contains("HasColumnType(\"binary(16)\")", snapshotCode, StringComparison.Ordinal);
        Assert.Equal(
            typeof(byte[]),
            FindProperty(snapshotModel, nameof(BinaryRecord.Payload))
                .ClrType);

        Assert.Equal(
            BinaryRecord.DefaultPayload,
            FindProperty(snapshotModel, nameof(BinaryRecord.Payload))
                .GetDefaultValue());
    }

    /// <summary>Initial migrations retain byte-array operation metadata and exact default-value hex bytes.</summary>
    [Fact]
    public void Initial_migration_keeps_byte_array_columns_and_binary_default_literals()
    {
        // Arrange
        using var context = new BinaryByteArrayModelContext(CreateOptions());
        using var services = CreateDesignTimeServices();
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        var generator = services
            .GetRequiredService<IMigrationsCodeGeneratorSelector>()
            .Select("C#");

        // Act
        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel());

        var table = operations
            .OfType<CreateTableOperation>()
            .Single();

        var column = table.Columns.Single(item => item.Name == nameof(BinaryRecord.Payload));
        var migrationCode = generator.GenerateMigration(
            "Doka.GeneratedBinaryMigrations",
            "BinaryInitial",
            operations,
            []);

        var sql = string.Join(
            Environment.NewLine,
            context
                .GetService<IMigrationsSqlGenerator>()
                .Generate(operations, model)
                .Select(command => command.CommandText));

        // Assert
        Assert.Equal(typeof(byte[]), column.ClrType);
        Assert.Equal("binary(16)", column.ColumnType);
        Assert.Equal(BinaryRecord.DefaultPayload, Assert.IsType<byte[]>(column.DefaultValue));
        Assert.Contains("table.Column<byte[]>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("X'00112233445566778899AABBCCDDEEFF'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Unannotated database BINARY(16) columns still reverse-engineer as GUIDs under the established policy.
    /// </summary>
    [Fact]
    public void Reverse_engineering_does_not_infer_byte_array_from_ambiguous_binary16_storage()
    {
        // Arrange
        using var context = new BinaryByteArrayModelContext(CreateOptions());
        using var services = CreateDesignTimeServices();
        using var scope = services.CreateScope();
        var database = new DatabaseModel { DatabaseName = "BinaryScaffold" };
        var table = new DatabaseTable
        {
            Database = database,
            Name = "BinaryRecord",
        };

        var id = new DatabaseColumn
        {
            Table = table,
            Name = "Id",
            StoreType = "int",
            IsNullable = false,
        };

        var payload = new DatabaseColumn
        {
            Table = table,
            Name = "Payload",
            StoreType = "binary(16)",
            IsNullable = false,
        };

        var primaryKey = new DatabasePrimaryKey
        {
            Table = table,
            Name = "PK_BinaryRecord",
        };

        primaryKey.Columns.Add(id);
        table.PrimaryKey = primaryKey;
        table.Columns.Add(id);
        table.Columns.Add(payload);
        database.Tables.Add(table);

        var scaffoldingContext = services.GetRequiredService<MySqlScaffoldingContext>();

        scaffoldingContext.Begin();

        try
        {
            // Act
            var model = scope
                .ServiceProvider
                .GetRequiredService<IScaffoldingModelFactory>()
                .Create(database, new ModelReverseEngineerOptions());

            var property = InitializeDesignModel(context, model)
                .GetEntityTypes()
                .Single()
                .FindProperty("Payload")!;

            // Assert
            // WHY: SQL metadata cannot distinguish a GUID from arbitrary 16-byte data;
            // reader adaptation must not silently change that existing inference policy.
            Assert.Equal(typeof(Guid), property.ClrType);
            Assert.Equal("binary(16)", property.GetColumnType());
        }
        finally
        {
            scaffoldingContext.Abort();
        }
    }

    /// <summary>Creates provider-owned transient options without contacting a database.</summary>
    private static DbContextOptions<BinaryByteArrayModelContext> CreateOptions(
        MySqlGuidFormat defaultGuidFormat = MySqlGuidFormat.Binary16
    ) => MySqlFunctionalTestOptions
        .CreateTransientBuilder<BinaryByteArrayModelContext>()
        .UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.DefaultGuidFormat(defaultGuidFormat))
        .Options;

    /// <summary>Uses the same standalone design-time registration as migrations and reverse engineering.</summary>
    private static ServiceProvider CreateDesignTimeServices()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddEntityFrameworkDokaMySqlDesignTime();

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>Finds one property in the single-entity test model.</summary>
    private static IProperty FindProperty(
        IModel model,
        string propertyName
    ) => model
        .GetEntityTypes()
        .Single()
        .FindProperty(propertyName)!;

    /// <summary>Executes the reader expression retained by a generated runtime-model mapping.</summary>
    private static byte[] ReadProviderValue(
        RelationalTypeMapping mapping,
        Guid value
    )
    {
        using var table = new DataTable();

        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(value);

        using var reader = table.CreateDataReader();

        Assert.True(reader.Read());

        // WHY: Compiled models clone the mapping; execute its actual reader
        // rather than only checking the generated type name.
        var readerParameter = Expression.Parameter(typeof(DbDataReader), "reader");
        var read = Expression.Call(readerParameter, mapping.GetDataReaderMethod(), Expression.Constant(0));
        var materialize = Expression
            .Lambda<Func<DbDataReader, byte[]>>(
                mapping.CustomizeDataReaderExpression(read),
                readerParameter)
            .Compile();

        return materialize(reader);
    }

    /// <summary>Initializes reconstructed design metadata before comparing its relational model.</summary>
    private static IModel InitializeDesignModel(
        DbContext context,
        IModel model
    ) => context
        .GetService<IModelRuntimeInitializer>()
        .Initialize(
            model,
            designTime: true,
            context.GetService<IDiagnosticsLogger<DbLoggerCategory.Model.Validation>>());

    /// <summary>Compiles generated sources against the exact assemblies loaded by this test process.</summary>
    private static Assembly Compile(
        IEnumerable<string> sources
    )
    {
        var platformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [];

        var references = platformAssemblies
            .Concat(
                AppDomain
                    .CurrentDomain
                    .GetAssemblies()
                    .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                    .Select(assembly => assembly.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            $"Doka.GeneratedBinaryModels.{Guid.NewGuid():N}",
            sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        Assert.True(
            result.Success,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));

        return Assembly.Load(stream.ToArray());
    }

    /// <summary>Provides a compact model for byte-array and genuine GUID storage controls.</summary>
    public sealed class BinaryByteArrayModelContext : DbContext
    {
        /// <summary>Creates the model without opening a database connection.</summary>
        public BinaryByteArrayModelContext(
            DbContextOptions<BinaryByteArrayModelContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<BinaryRecord>();

            entity
                .Property(row => row.Payload)
                .HasColumnType("binary(16)")
                .HasDefaultValue(BinaryRecord.DefaultPayload);

            entity
                .Property(row => row.ShortPayload)
                .HasColumnType("binary(8)");

            entity
                .Property(row => row.LongPayload)
                .HasColumnType("binary(32)");

            // WHY: A logical size facet must not hide the physical BINARY(16) driver shape.
            entity
                .Property(row => row.MisleadingSizePayload)
                .HasMaxLength(8)
                .HasColumnType("binary(16)");

            entity
                .Property(row => row.ImplicitPayload)
                .HasMaxLength(16);

            entity
                .Property(row => row.BlobPayload)
                .HasColumnType("longblob");

            entity
                .Property(row => row.ConvertedGuid)
                .HasConversion<GuidToBytesConverter>()
                .HasColumnType("binary(16)");
        }
    }

    /// <summary>
    /// Separates fixed-byte and GUID properties so reader changes cannot hide a storage-format regression.
    /// </summary>
    public sealed class BinaryRecord
    {
        /// <summary>Gets the asymmetric byte sequence used to expose accidental mixed-endian conversion.</summary>
        public static byte[] DefaultPayload => Convert.FromHexString("00112233445566778899AABBCCDDEEFF");

        /// <summary>Gets or sets the primary key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets explicit fixed 16-byte data.</summary>
        public byte[] Payload { get; set; } = [];

        /// <summary>Gets or sets fixed-width bytes outside the GUID size.</summary>
        public byte[] ShortPayload { get; set; } = [];

        /// <summary>Gets or sets fixed-width bytes larger than the GUID size.</summary>
        public byte[] LongPayload { get; set; } = [];

        /// <summary>Gets or sets explicitly declared fixed bytes with a different logical size hint.</summary>
        public byte[] MisleadingSizePayload { get; set; } = [];

        /// <summary>Gets or sets implicitly mapped bounded bytes.</summary>
        public byte[] ImplicitPayload { get; set; } = [];

        /// <summary>Gets or sets explicit BLOB data.</summary>
        public byte[] BlobPayload { get; set; } = [];

        /// <summary>Gets or sets a GUID whose user-selected converter produces bytes.</summary>
        public Guid ConvertedGuid { get; set; }

        /// <summary>Gets or sets a GUID using the provider's configured default.</summary>
        public Guid RegularGuid { get; set; }
    }
}
