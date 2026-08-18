using Microsoft.EntityFrameworkCore.Design.Internal;
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
    /// Verifies that a shared design-time service provider isolates all mutable scaffolding
    /// metadata between concurrent operations and consumes that metadata after code generation.
    /// </summary>
    [Fact]
    public async Task Concurrent_reverse_engineering_isolates_server_and_spatial_state()
    {
        using var databaseModelBoundary = new Barrier(2);
        using var providerCodeBoundary = new Barrier(2);
        await using var serviceProvider = CreateConcurrentDesignTimeServiceProvider(
            databaseModelBoundary,
            providerCodeBoundary);

        var mySqlTask = Task.Run(() => ScaffoldAndAssertStateConsumed(
            serviceProvider,
            "Server=localhost;Database=spatial_mysql;User ID=root;Password=secret;"));
        var mariaDbTask = Task.Run(() => ScaffoldAndAssertStateConsumed(
            serviceProvider,
            "Server=localhost;Database=plain_mariadb;User ID=root;Password=secret;"));

        var results = await Task.WhenAll(mySqlTask, mariaDbTask);
        var mySqlCode = results[0].ContextFile.Code;
        var mariaDbCode = results[1].ContextFile.Code;

        Assert.Contains(
            "MySqlServerVersion.MySql(new System.Version(8, 4, 6))",
            mySqlCode,
            StringComparison.Ordinal);
        Assert.Contains("UseNetTopologySuite()", mySqlCode, StringComparison.Ordinal);
        Assert.DoesNotContain("MariaDb(", mySqlCode, StringComparison.Ordinal);

        Assert.Contains(
            "MySqlServerVersion.MariaDb(new System.Version(11, 8, 2))",
            mariaDbCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain("UseNetTopologySuite()", mariaDbCode, StringComparison.Ordinal);
        Assert.DoesNotContain("MySqlServerVersion.MySql(", mariaDbCode, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that both ordinary failures and cancellation remove partially written
    /// scaffolding state before control returns to the caller.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_or_cancelled_reverse_engineering_releases_operation_state(
        bool cancel
    )
    {
        const string connectionString =
            "Server=localhost;Database=failing_operation;User ID=root;Password=secret;";

        using var serviceProvider = CreateFailingDesignTimeServiceProvider(cancel);

        var exception = Record.Exception(
            () => ScaffoldModel(serviceProvider, connectionString));

        if (cancel)
        {
            Assert.IsType<OperationCanceledException>(exception);
        }
        else
        {
            Assert.IsType<InvalidOperationException>(exception);
        }

        AssertNoActiveScaffoldingState(serviceProvider, connectionString);
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
    /// Verifies that MariaDB enforces the spatial SRID through its documented CHECK
    /// mechanism while preserving the spatial-index contract.
    /// </summary>
    [Fact]
    public void MariaDb_spatial_migrations_emit_srid_check_and_spatial_index_sql()
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

        Assert.Contains(
            "`Location` point NOT NULL CHECK (ST_SRID(`Location`) = 4326)",
            sql,
            StringComparison.Ordinal);
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
            logEntry => logEntry.EventId == MySqlEventId.InvalidSpatialIndexConfiguration
                && logEntry.Category == DbLoggerCategory.Model.Validation.Name);

        Assert.Contains("exactly one property", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.Equal(DbLoggerCategory.Model.Validation.Name, entry.Category);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        bool isMariaDb,
        ILoggerFactory? loggerFactory = null
    )
        where TContext : DbContext
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>();
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

        return ScaffoldModel(
            serviceProvider,
            "Server=localhost;Database=phase3;User ID=root;Password=secret;");
    }

    private static ScaffoldedModel ScaffoldModel(
        ServiceProvider serviceProvider,
        string connectionString
    )
    {
        using var scope = serviceProvider.CreateScope();
        var scaffolder = scope.ServiceProvider.GetRequiredService<IReverseEngineerScaffolder>();

        return scaffolder.ScaffoldModel(
            connectionString,
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
                ConnectionString = connectionString,
                SuppressConnectionStringWarning = true,
                UseNullableReferenceTypes = true,
            });
    }

    private static ScaffoldedModel ScaffoldAndAssertStateConsumed(
        ServiceProvider serviceProvider,
        string connectionString
    )
    {
        var scaffoldedModel = ScaffoldModel(serviceProvider, connectionString);

        AssertNoActiveScaffoldingState(serviceProvider, connectionString);

        return scaffoldedModel;
    }

    private static void AssertNoActiveScaffoldingState(
        ServiceProvider serviceProvider,
        string connectionString
    )
    {
        var codeGenerator = serviceProvider.GetRequiredService<IProviderConfigurationCodeGenerator>();
        var exception = Assert.Throws<InvalidOperationException>(
            () => codeGenerator.GenerateUseProvider(connectionString));

        Assert.Contains(
            "No MySQL scaffolding operation is active",
            exception.Message,
            StringComparison.Ordinal);
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

    private static ServiceProvider CreateFailingDesignTimeServiceProvider(
        bool cancel
    )
    {
        var services = new ServiceCollection();
#pragma warning disable EF1001
        var reporter = new OperationReporter(new OperationReportHandler(_ => { }, _ => { }, _ => { }, _ => { }));
#pragma warning restore EF1001

        services.AddEntityFrameworkDesignTimeServices(
            reporter,
            () => new ServiceCollection().BuildServiceProvider());
        services.AddEntityFrameworkDokaMySqlDesignTime();
        services.AddSingleton<IDatabaseModelFactory>(serviceProvider =>
            new FailingStubDatabaseModelFactory(
                serviceProvider.GetRequiredService<MySqlScaffoldingContext>(),
                cancel));

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static ServiceProvider CreateConcurrentDesignTimeServiceProvider(
        Barrier databaseModelBoundary,
        Barrier providerCodeBoundary
    )
    {
        var services = new ServiceCollection();
#pragma warning disable EF1001
        var reporter = new OperationReporter(new OperationReportHandler(_ => { }, _ => { }, _ => { }, _ => { }));
#pragma warning restore EF1001

        services.AddEntityFrameworkDesignTimeServices(
            reporter,
            () => new ServiceCollection().BuildServiceProvider());
        services.AddEntityFrameworkDokaMySqlDesignTime();
        services.AddEntityFrameworkDokaMySqlNetTopologySuite();
        services.AddSingleton<IDatabaseModelFactory>(serviceProvider =>
            new ConcurrentStubDatabaseModelFactory(
                CreateSpatialDatabaseModel(),
                CreateNonSpatialDatabaseModel(),
                serviceProvider.GetRequiredService<MySqlScaffoldingContext>(),
                databaseModelBoundary));

        EfCoreServiceDecorator
            .Decorate<IProviderConfigurationCodeGenerator, SynchronizingProviderConfigurationCodeGenerator>(
                services,
                (inner, serviceProvider) => new SynchronizingProviderConfigurationCodeGenerator(
                    inner,
                    serviceProvider.GetRequiredService<ProviderCodeGeneratorDependencies>(),
                    providerCodeBoundary));

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

    private static DatabaseModel CreateNonSpatialDatabaseModel()
    {
        var databaseModel = new DatabaseModel
        {
            DatabaseName = "plain_mariadb",
            Collation = "utf8mb4_general_ci",
        };
        var table = new DatabaseTable
        {
            Database = databaseModel,
            Name = "plain_feature",
        };
        var idColumn = new DatabaseColumn
        {
            Table = table,
            Name = "Id",
            StoreType = "int",
            IsNullable = false,
            ValueGenerated = ValueGenerated.OnAdd,
        };

        databaseModel.Tables.Add(table);
        table.Columns.Add(idColumn);
        table.PrimaryKey = new DatabasePrimaryKey
        {
            Table = table,
            Name = "PK_plain_feature",
            Columns = { idColumn },
        };

        return databaseModel;
    }

    private static void WaitAt(
        Barrier boundary,
        string boundaryName
    )
    {
        if (!boundary.SignalAndWait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException(
                $"Concurrent scaffolding did not reach the '{boundaryName}' boundary.");
        }
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

    private sealed class ConcurrentStubDatabaseModelFactory : IDatabaseModelFactory
    {
        private readonly DatabaseModel _spatialModel;
        private readonly DatabaseModel _nonSpatialModel;
        private readonly MySqlScaffoldingContext _scaffoldingContext;
        private readonly Barrier _databaseModelBoundary;

        public ConcurrentStubDatabaseModelFactory(
            DatabaseModel spatialModel,
            DatabaseModel nonSpatialModel,
            MySqlScaffoldingContext scaffoldingContext,
            Barrier databaseModelBoundary
        )
        {
            _spatialModel = spatialModel ?? throw new ArgumentNullException(nameof(spatialModel));
            _nonSpatialModel = nonSpatialModel ?? throw new ArgumentNullException(nameof(nonSpatialModel));
            _scaffoldingContext = scaffoldingContext
                ?? throw new ArgumentNullException(nameof(scaffoldingContext));
            _databaseModelBoundary = databaseModelBoundary
                ?? throw new ArgumentNullException(nameof(databaseModelBoundary));
        }

        public DatabaseModel Create(
            string connectionString,
            DatabaseModelFactoryOptions options
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            ArgumentNullException.ThrowIfNull(options);

            var databaseName = new MySqlConnectionStringBuilder(connectionString).Database;
            var operation = databaseName switch
            {
                "spatial_mysql" => (_spatialModel, "8.4.6"),
                "plain_mariadb" => (_nonSpatialModel, "11.8.2-MariaDB"),
                _ => throw new InvalidOperationException(
                    $"Unexpected concurrent scaffolding database '{databaseName}'."),
            };

            _scaffoldingContext.Begin();
            _scaffoldingContext.SetDetectedServerVersionText(operation.Item2);
            WaitAt(_databaseModelBoundary, "database-model");

            return operation.Item1;
        }

        public DatabaseModel Create(
            DbConnection connection,
            DatabaseModelFactoryOptions options
        )
        {
            ArgumentNullException.ThrowIfNull(connection);

            return Create(connection.ConnectionString, options);
        }
    }

    private sealed class FailingStubDatabaseModelFactory : IDatabaseModelFactory
    {
        private readonly MySqlScaffoldingContext _scaffoldingContext;
        private readonly bool _cancel;

        public FailingStubDatabaseModelFactory(
            MySqlScaffoldingContext scaffoldingContext,
            bool cancel
        )
        {
            _scaffoldingContext = scaffoldingContext
                ?? throw new ArgumentNullException(nameof(scaffoldingContext));
            _cancel = cancel;
        }

        public DatabaseModel Create(
            string connectionString,
            DatabaseModelFactoryOptions options
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            ArgumentNullException.ThrowIfNull(options);

            _scaffoldingContext.Begin();
            _scaffoldingContext.SetDetectedServerVersionText("8.4.6");

            throw _cancel
                ? new OperationCanceledException("Scaffolding was cancelled by the test operation.")
                : new InvalidOperationException("Scaffolding failed in the test database-model factory.");
        }

        public DatabaseModel Create(
            DbConnection connection,
            DatabaseModelFactoryOptions options
        )
        {
            ArgumentNullException.ThrowIfNull(connection);

            return Create(connection.ConnectionString, options);
        }
    }

    private sealed class SynchronizingProviderConfigurationCodeGenerator : ProviderCodeGenerator
    {
        private readonly IProviderConfigurationCodeGenerator _inner;
        private readonly Barrier _providerCodeBoundary;

        public SynchronizingProviderConfigurationCodeGenerator(
            IProviderConfigurationCodeGenerator inner,
            ProviderCodeGeneratorDependencies dependencies,
            Barrier providerCodeBoundary
        ) : base(dependencies)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _providerCodeBoundary = providerCodeBoundary
                ?? throw new ArgumentNullException(nameof(providerCodeBoundary));
        }

        public override MethodCallCodeFragment GenerateUseProvider(
            string connectionString,
            MethodCallCodeFragment? providerOptions
        )
        {
            WaitAt(_providerCodeBoundary, "provider-code");
            return _inner.GenerateUseProvider(connectionString, providerOptions);
        }
    }
}
