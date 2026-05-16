using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the spatial migrations, scaffolding, and diagnostics contract.
/// </summary>
public sealed class MySqlNetTopologySuiteScaffoldingAndMigrationsTests
{
    /// <summary>
    /// Verifies that spatial reverse engineering preserves the optional-package requirement,
    /// the SRID contract, and the spatial-index DSL when the package is active.
    /// </summary>
    [Fact]
    public void Reverse_engineering_with_the_optional_spatial_package_scaffolds_the_spatial_contract()
    {
        var scaffoldedModel = ScaffoldModel(
            CreateSpatialDatabaseModel(),
            detectedServerVersionText: "8.4.6",
            includeNetTopologySuite: true);

        Assert.Contains("UseNetTopologySuite()", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains("HasSrid(4326)", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains("IsSpatial()", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains(
            "Point",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
        Assert.Contains(
            "Location",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that spatial reverse engineering emits a warning and skips spatial artifacts
    /// when the optional package is not active in the design-time graph.
    /// </summary>
    [Fact]
    public void Reverse_engineering_without_the_optional_spatial_package_warns_and_skips_spatial_artifacts()
    {
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        var scaffoldedModel = ScaffoldModel(
            CreateSpatialDatabaseModel(),
            detectedServerVersionText: "8.4.6",
            includeNetTopologySuite: false,
            loggerFactory: loggerFactory);

        Assert.DoesNotContain("UseNetTopologySuite()", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.DoesNotContain("HasSrid(", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.DoesNotContain("IsSpatial()", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Point",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
        Assert.Contains(
            "public int Id { get; set; }",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);

        var warningEntry = Assert.Single(
            sink.Entries,
            entry => entry.EventId.Id == MySqlEventId.MissingSpatialPackageDuringScaffolding.Id);

        Assert.Equal(LogLevel.Warning, warningEntry.LogLevel);
        Assert.Equal(MySqlLoggerCategory.Scaffolding, warningEntry.Category);
        Assert.Contains("optional", warningEntry.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that the migrations pipeline emits the approved MySQL spatial SRID and SPATIAL INDEX SQL.
    /// </summary>
    [Fact]
    public void MySql_spatial_migrations_emit_srid_and_spatial_index_sql()
    {
        using var sourceContext = new EmptySpatialContext(CreateOptions<EmptySpatialContext>(isMariaDb: false));
        using var targetContext =
            new SpatialMigrationsContext(CreateOptions<SpatialMigrationsContext>(isMariaDb: false));
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var migrationsSqlGenerator = targetContext.GetService<IMigrationsSqlGenerator>();
        var operations = differ.GetDifferences(
            sourceContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel(),
            targetContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel());
        var sql = string.Join(
            Environment.NewLine,
            migrationsSqlGenerator
                .Generate(operations, targetContext.Model)
                .Select(command => command.CommandText));

        Assert.Contains("`Location` point SRID 4326 NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE SPATIAL INDEX", sql, StringComparison.Ordinal);
        Assert.Contains("(`Location`)", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that MariaDB keeps the spatial-index contract but omits the unsupported SRID column clause.
    /// </summary>
    [Fact]
    public void MariaDb_spatial_migrations_omit_the_column_srid_clause_but_keep_spatial_indexes()
    {
        using var sourceContext = new EmptySpatialContext(CreateOptions<EmptySpatialContext>(isMariaDb: true));
        using var targetContext =
            new SpatialMigrationsContext(CreateOptions<SpatialMigrationsContext>(isMariaDb: true));
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var migrationsSqlGenerator = targetContext.GetService<IMigrationsSqlGenerator>();
        var operations = differ.GetDifferences(
            sourceContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel(),
            targetContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel());
        var sql = string.Join(
            Environment.NewLine,
            migrationsSqlGenerator
                .Generate(operations, targetContext.Model)
                .Select(command => command.CommandText));

        Assert.Contains("`Location` point NOT NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SRID 4326", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE SPATIAL INDEX", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that invalid multi-column spatial indexes fail validation with the approved diagnostic.
    /// </summary>
    [Fact]
    public void Invalid_multi_column_spatial_indexes_fail_validation_explicitly()
    {
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        using var context =
            new InvalidSpatialIndexContext(CreateOptions<InvalidSpatialIndexContext>(false, loggerFactory));

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        var entry = Assert.Single(
            sink.Entries,
            logEntry => logEntry.EventId.Id == MySqlEventId.InvalidSpatialIndexConfiguration.Id);

        Assert.Contains("exactly one property", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.Equal(MySqlLoggerCategory.Configuration, entry.Category);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        bool isMariaDb,
        ILoggerFactory? loggerFactory = null
    )
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        var serverVersion = isMariaDb
            ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

        if (loggerFactory is not null)
        {
            builder.UseLoggerFactory(loggerFactory);
        }

        builder.UseMySql(
            "Server=localhost;Database=phase3;User ID=root;Password=password;",
            serverVersion,
            options => options.UseNetTopologySuite());

        return builder.Options;
    }

    private static ScaffoldedModel ScaffoldModel(
        DatabaseModel databaseModel,
        string detectedServerVersionText,
        bool includeNetTopologySuite,
        ILoggerFactory? loggerFactory = null
    )
    {
        using var serviceProvider = CreateDesignTimeServiceProvider(
            databaseModel,
            detectedServerVersionText,
            includeNetTopologySuite,
            loggerFactory);
        using var scope = serviceProvider.CreateScope();
        var scaffolder = scope.ServiceProvider.GetRequiredService<IReverseEngineerScaffolder>();

        return scaffolder.ScaffoldModel(
            "Server=localhost;Database=phase3;User ID=root;Password=secret;",
            new DatabaseModelFactoryOptions(Array.Empty<string>(), Array.Empty<string>()),
            new ModelReverseEngineerOptions(),
            new ModelCodeGenerationOptions
            {
                ContextName = "SpatialReverseDbContext",
                ContextNamespace = "Phase3.Scaffolding",
                ModelNamespace = "Phase3.Scaffolding.Models",
                RootNamespace = "Phase3.Scaffolding",
                Language = "C#",
                ContextDir = "Generated",
                ProjectDir = "Generated",
                ConnectionString = "Server=localhost;Database=phase3;User ID=root;Password=secret;",
                SuppressConnectionStringWarning = true,
                UseNullableReferenceTypes = true,
            });
    }

    private static ServiceProvider CreateDesignTimeServiceProvider(
        DatabaseModel databaseModel,
        string detectedServerVersionText,
        bool includeNetTopologySuite,
        ILoggerFactory? loggerFactory
    )
    {
        var services = new ServiceCollection();
#pragma warning disable EF1001
        var reporter = new OperationReporter(new OperationReportHandler(_ => { }, _ => { }, _ => { }, _ => { }));
#pragma warning restore EF1001

        services.AddEntityFrameworkDesignTimeServices(reporter, () => new ServiceCollection().BuildServiceProvider());
        services.AddEntityFrameworkDokaMySqlDesignTime();

        if (includeNetTopologySuite)
        {
            services.AddEntityFrameworkDokaMySqlNetTopologySuite();
        }

        if (loggerFactory is not null)
        {
            services.AddSingleton(loggerFactory);
        }

        services.AddSingleton<IDatabaseModelFactory>(serviceProvider => new StubDatabaseModelFactory(
            databaseModel,
            detectedServerVersionText,
            serviceProvider.GetRequiredService<MySqlScaffoldingContext>()));

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static DatabaseModel CreateSpatialDatabaseModel()
    {
        var databaseModel = new DatabaseModel
        {
            DatabaseName = "phase3",
            Collation = "utf8mb4_0900_ai_ci",
        };

        var table = new DatabaseTable
        {
            Database = databaseModel,
            Name = "spatial_feature",
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
        var locationColumn = new DatabaseColumn
        {
            Table = table,
            Name = "Location",
            StoreType = "point",
            IsNullable = false,
        };

        locationColumn.SetAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId, 4326);

        table.Columns.Add(idColumn);
        table.Columns.Add(locationColumn);

        table.PrimaryKey = new DatabasePrimaryKey
        {
            Table = table,
            Name = "PK_spatial_feature",
            Columns = { idColumn },
        };

        var spatialIndex = new DatabaseIndex
        {
            Table = table,
            Name = "IX_spatial_feature_Location",
        };

        spatialIndex.Columns.Add(locationColumn);
        spatialIndex.SetAnnotation(MySqlAnnotationNames.SpatialIndex, true);
        table.Indexes.Add(spatialIndex);

        return databaseModel;
    }

    private sealed class EmptySpatialContext : DbContext
    {
        public EmptySpatialContext(
            DbContextOptions options
        ) : base(options) { }
    }

    private sealed class SpatialMigrationsContext : DbContext
    {
        public SpatialMigrationsContext(
            DbContextOptions options
        ) : base(options) { }

        public DbSet<SpatialEntity> SpatialEntities => Set<SpatialEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SpatialEntity>(entity =>
            {
                entity
                    .Property(item => item.Location)
                    .HasSrid(4326);
                entity
                    .HasIndex(item => item.Location)
                    .IsSpatial();
            });
        }
    }

    private sealed class InvalidSpatialIndexContext : DbContext
    {
        public InvalidSpatialIndexContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<InvalidSpatialIndexEntity>(entity =>
            {
                entity
                    .HasIndex(item => new
                    {
                        item.Location,
                        item.AlternateLocation
                    })
                    .IsSpatial();
            });
        }
    }

    private sealed class SpatialEntity
    {
        public int Id { get; set; }

        public Point Location { get; set; } = null!;
    }

    private sealed class InvalidSpatialIndexEntity
    {
        public int Id { get; set; }

        public Point Location { get; set; } = null!;

        public Point AlternateLocation { get; set; } = null!;
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

            _scaffoldingContext.Begin();
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

            _scaffoldingContext.Begin();
            _scaffoldingContext.SetDetectedServerVersionText(_detectedServerVersionText);

            return _databaseModel;
        }
    }
}
