namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures the steady-state allocations of the provider-owned temporal and CTE
/// SQL-generation paths without opening a database connection.
/// </summary>
/// <remarks>
/// The release contract gates managed allocations only. Wall-clock measurements
/// remain visible in BenchmarkDotNet output, but are not used as a release gate
/// because unrelated workstation load would make that evidence non-reproducible.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
[InvocationCount(512)]
public class TemporalProviderBenchmarks
{
    private const string ConnectionString =
        "Server=localhost;Database=benchmark_temporal;User ID=root;Password=benchmark;";

    private const string RecursiveCteSql =
        """
        WITH RECURSIVE sequence (`Id`, `Name`) AS (
            SELECT 1, CAST('root' AS CHAR(128))
            UNION ALL
            SELECT `Id` + 1, `Name`
            FROM sequence
            WHERE `Id` < 8
        )
        SELECT `Id`, `Name`
        FROM sequence
        """;

    private static readonly DateTime s_from =
        new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime s_to =
        new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime s_pointInTime =
        new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly MigrationOperation[] s_columnDefaultOperations = CreateColumnDefaultOperations();

    private DbContextOptions<TemporalQueryBenchmarkContext> _mysqlQueryOptions = null!;
    private DbContextOptions<TemporalQueryBenchmarkContext> _mariaDbQueryOptions = null!;
    private DbContextOptions<EmptyMigrationContext> _mysqlEmptyOptions = null!;
    private DbContextOptions<EmptyMigrationContext> _mariaDbEmptyOptions = null!;
    private DbContextOptions<TemporalMigrationBenchmarkContext> _mysqlMigrationOptions = null!;
    private DbContextOptions<TemporalMigrationBenchmarkContext> _mariaDbMigrationOptions = null!;

    /// <summary>
    /// Builds the engine-specific option sets outside the measured operations.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        var mySqlVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        var mariaDbVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));

        _mysqlQueryOptions = CreateOptions<TemporalQueryBenchmarkContext>(mySqlVersion);
        _mariaDbQueryOptions = CreateOptions<TemporalQueryBenchmarkContext>(mariaDbVersion);
        _mysqlEmptyOptions = CreateOptions<EmptyMigrationContext>(mySqlVersion);
        _mariaDbEmptyOptions = CreateOptions<EmptyMigrationContext>(mariaDbVersion);
        _mysqlMigrationOptions = CreateOptions<TemporalMigrationBenchmarkContext>(mySqlVersion);
        _mariaDbMigrationOptions = CreateOptions<TemporalMigrationBenchmarkContext>(mariaDbVersion);
    }

    /// <summary>
    /// Generates every temporal operator plus parameterized non-recursive and
    /// recursive CTE composition for both engine families.
    /// </summary>
    /// <returns>The aggregate SQL length, which prevents dead-code elimination.</returns>
    [Benchmark]
    public int GenerateTemporalAndCteQuerySql()
    {
        using var mySqlContext = new TemporalQueryBenchmarkContext(_mysqlQueryOptions);
        using var mariaDbContext = new TemporalQueryBenchmarkContext(_mariaDbQueryOptions);

        return GenerateQuerySql(mySqlContext) + GenerateQuerySql(mariaDbContext);
    }

    /// <summary>
    /// Generates native MariaDB and provider-emulated MySQL temporal-table DDL
    /// plus create, add, and alter temporal column defaults.
    /// </summary>
    /// <returns>The aggregate command length, which prevents dead-code elimination.</returns>
    [Benchmark]
    public int GenerateTemporalMigrationSql()
    {
        using var mysqlSource = new EmptyMigrationContext(_mysqlEmptyOptions);
        using var mysqlTarget = new TemporalMigrationBenchmarkContext(_mysqlMigrationOptions);
        using var mariaDbSource = new EmptyMigrationContext(_mariaDbEmptyOptions);
        using var mariaDbTarget = new TemporalMigrationBenchmarkContext(_mariaDbMigrationOptions);

        return GenerateMigrationSql(mysqlSource, mysqlTarget)
            + GenerateColumnDefaultSql(mysqlTarget)
            + GenerateMigrationSql(mariaDbSource, mariaDbTarget)
            + GenerateColumnDefaultSql(mariaDbTarget);
    }

    private static int GenerateQuerySql(
        TemporalQueryBenchmarkContext context
    )
    {
        const int minimumId = 7;

        var totalLength = context
            .TemporalEntities.TemporalAsOf(s_pointInTime)
            .Where(entity => entity.Id >= minimumId)
            .ToQueryString()
            .Length;

        totalLength += context
            .TemporalEntities.TemporalAll()
            .Where(entity => entity.Id >= minimumId)
            .ToQueryString()
            .Length;
        totalLength += context
            .TemporalEntities.TemporalFromTo(s_from, s_to)
            .Where(entity => entity.Id >= minimumId)
            .ToQueryString()
            .Length;
        totalLength += context
            .TemporalEntities.TemporalBetween(s_from, s_to)
            .Where(entity => entity.Id >= minimumId)
            .ToQueryString()
            .Length;
        totalLength += context
            .TemporalEntities.TemporalContainedIn(s_from, s_to)
            .Where(entity => entity.Id >= minimumId)
            .ToQueryString()
            .Length;
        totalLength += context
            .CteEntities.FromSqlInterpolated(
                $"""
                 WITH filtered AS (
                     SELECT `Id`, `Name`
                     FROM `CteBenchmarkEntities`
                     WHERE `Id` >= {minimumId}
                 )
                 SELECT `Id`, `Name`
                 FROM filtered
                 """)
            .Where(entity => entity.Name != string.Empty)
            .ToQueryString()
            .Length;
        totalLength += context
            .CteEntities.FromSqlRaw(RecursiveCteSql)
            .Where(entity => entity.Id <= 8)
            .ToQueryString()
            .Length;

        return totalLength;
    }

    private static int GenerateMigrationSql(
        DbContext sourceContext,
        DbContext targetContext
    )
    {
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var migrationsSqlGenerator = targetContext.GetService<IMigrationsSqlGenerator>();
        var operations = differ.GetDifferences(
            sourceContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel(),
            targetContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel());

        return migrationsSqlGenerator
            .Generate(operations, targetContext.Model)
            .Sum(command => command.CommandText.Length);
    }

    private static int GenerateColumnDefaultSql(
        DbContext context
    ) => context
        .GetService<IMigrationsSqlGenerator>()
        .Generate(s_columnDefaultOperations, context.Model)
        .Sum(command => command.CommandText.Length);

    private static MigrationOperation[] CreateColumnDefaultOperations()
    {
        var createTable = new CreateTableOperation { Name = "DefaultBenchmarkEntries" };
        createTable.Columns.Add(
            CreateDefaultColumn<AddColumnOperation>(
                "CreatedDate",
                typeof(DateOnly),
                "date",
                new DateOnly(2026, 8, 17)));
        createTable.Columns.Add(
            CreateDefaultColumn<AddColumnOperation>(
                "CreatedTime",
                typeof(TimeOnly),
                "time(6)",
                new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1_234_567))));

        return
        [
            createTable,
            CreateDefaultColumn<AddColumnOperation>(
                "AddedDate",
                typeof(DateOnly),
                columnType: null,
                new DateOnly(2027, 1, 2)),
            CreateDefaultColumn<AddColumnOperation>(
                "AddedTime",
                typeof(TimeOnly),
                "time(6)",
                new TimeOnly(3, 4, 5).Add(TimeSpan.FromTicks(7_654_321))),
            CreateAlterDefaultColumn(
                "AddedDate",
                typeof(DateOnly),
                "date",
                new DateOnly(2028, 2, 3),
                new DateOnly(2027, 1, 2)),
            CreateAlterDefaultColumn(
                "AddedTime",
                typeof(TimeOnly),
                "time(6)",
                new TimeOnly(4, 5, 6).Add(TimeSpan.FromTicks(6_543_219)),
                new TimeOnly(3, 4, 5).Add(TimeSpan.FromTicks(7_654_321))),
        ];
    }

    private static TOperation CreateDefaultColumn<TOperation>(
        string name,
        Type clrType,
        string? columnType,
        object defaultValue
    )
        where TOperation : ColumnOperation, new() => new()
        {
        Table = "DefaultBenchmarkEntries",
        Name = name,
        ClrType = clrType,
        ColumnType = columnType,
        IsNullable = false,
        DefaultValue = defaultValue,
    };

    private static AlterColumnOperation CreateAlterDefaultColumn(
        string name,
        Type clrType,
        string columnType,
        object defaultValue,
        object oldDefaultValue
    ) => new()
    {
        Table = "DefaultBenchmarkEntries",
        Name = name,
        ClrType = clrType,
        ColumnType = columnType,
        IsNullable = false,
        DefaultValue = defaultValue,
        OldColumn =
        {
            ClrType = clrType,
            ColumnType = columnType,
            IsNullable = false,
            DefaultValue = oldDefaultValue,
        },
    };

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlServerVersion serverVersion
    )
        where TContext : DbContext => new DbContextOptionsBuilder<TContext>().UseMySql(ConnectionString, serverVersion)
        .Options;
}

internal sealed class TemporalQueryBenchmarkContext : DbContext
{
    public TemporalQueryBenchmarkContext(
        DbContextOptions<TemporalQueryBenchmarkContext> options
    ) : base(options) { }

    public DbSet<TemporalBenchmarkEntity> TemporalEntities => Set<TemporalBenchmarkEntity>();

    public DbSet<CteBenchmarkEntity> CteEntities => Set<CteBenchmarkEntity>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<TemporalBenchmarkEntity>(entity =>
        {
            entity.ToTable(
                "TemporalBenchmarkEntities",
                table => table.IsTemporal(temporal => temporal.UseHistoryTable("TemporalBenchmarkEntityHistory")));
            entity
                .Property(property => property.Name)
                .HasMaxLength(128);
        });

        modelBuilder.Entity<CteBenchmarkEntity>(entity =>
        {
            entity.ToTable("CteBenchmarkEntities");
            entity
                .Property(property => property.Name)
                .HasMaxLength(128);
        });
    }
}

internal sealed class TemporalMigrationBenchmarkContext : DbContext
{
    public TemporalMigrationBenchmarkContext(
        DbContextOptions<TemporalMigrationBenchmarkContext> options
    ) : base(options) { }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<TemporalBenchmarkEntity>(entity =>
        {
            entity.ToTable(
                "TemporalBenchmarkEntities",
                table => table.IsTemporal(temporal => temporal.UseHistoryTable("TemporalBenchmarkEntityHistory")));
            entity
                .Property(property => property.Name)
                .HasMaxLength(128);
        });
    }
}

internal sealed class TemporalBenchmarkEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

internal sealed class CteBenchmarkEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
