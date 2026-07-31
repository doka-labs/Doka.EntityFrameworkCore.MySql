namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

public sealed class BenchmarkContext : DbContext
{
    public BenchmarkContext(
        DbContextOptions<BenchmarkContext> options
    ) : base(options) { }

    public DbSet<BasicBenchmarkEntity> BasicEntities => Set<BasicBenchmarkEntity>();

    public DbSet<TranslationBenchmarkEntity> TranslationEntities => Set<TranslationBenchmarkEntity>();

    public DbSet<SaveChangeBenchmarkEntity> SaveChangeEntities => Set<SaveChangeBenchmarkEntity>();

    public DbSet<SpatialBenchmarkEntity> SpatialEntities => Set<SpatialBenchmarkEntity>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<BasicBenchmarkEntity>(entity =>
        {
            entity
                .Property(property => property.Name)
                .HasMaxLength(128);
            entity
                .Property(property => property.Payload)
                .HasColumnType("json");
        });

        modelBuilder.Entity<TranslationBenchmarkEntity>(entity =>
        {
            entity
                .Property(property => property.Name)
                .HasMaxLength(128);
            entity
                .Property(property => property.BinaryPayload)
                .HasMaxLength(256);
        });

        modelBuilder.Entity<SpatialBenchmarkEntity>(entity =>
        {
            entity
                .Property(property => property.Location)
                .HasColumnType("point")
                .HasSrid(4326);
        });
    }
}

public sealed class EmptyMigrationContext : DbContext
{
    public EmptyMigrationContext(
        DbContextOptions<EmptyMigrationContext> options
    ) : base(options) { }
}

public sealed class RichMigrationContext : DbContext
{
    public RichMigrationContext(
        DbContextOptions<RichMigrationContext> options
    ) : base(options) { }

    public DbSet<BasicBenchmarkEntity> BasicEntities => Set<BasicBenchmarkEntity>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<BasicBenchmarkEntity>(entity =>
        {
            entity
                .Property(property => property.Name)
                .HasMaxLength(128);
            entity
                .Property(property => property.Payload)
                .HasColumnType("json");
        });
    }
}

public sealed class SpatialMigrationContext : DbContext
{
    public SpatialMigrationContext(
        DbContextOptions<SpatialMigrationContext> options
    ) : base(options) { }

    public DbSet<BasicBenchmarkEntity> BasicEntities => Set<BasicBenchmarkEntity>();

    public DbSet<SpatialBenchmarkEntity> SpatialEntities => Set<SpatialBenchmarkEntity>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<BasicBenchmarkEntity>(entity =>
        {
            entity
                .Property(property => property.Name)
                .HasMaxLength(128);
            entity
                .Property(property => property.Payload)
                .HasColumnType("json");
        });

        modelBuilder.Entity<SpatialBenchmarkEntity>(entity =>
        {
            entity
                .Property(property => property.Location)
                .HasColumnType("point")
                .HasSrid(4326);
        });
    }
}

public sealed class BasicBenchmarkEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string Payload { get; set; } = string.Empty;
}

public sealed class SaveChangeBenchmarkEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class TranslationBenchmarkEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public double Score { get; set; }

    public int SignedValue { get; set; }

    public int ShiftCount { get; set; }

    public Guid Token { get; set; }

    public byte[] BinaryPayload { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public TimeSpan Duration { get; set; }
}

public sealed class SpatialBenchmarkEntity
{
    public int Id { get; set; }

    public Point Location { get; set; } = default!;
}

internal sealed class LargeBenchmarkContext : DbContext
{
    private readonly int _entityTypeCount;

    public LargeBenchmarkContext(
        DbContextOptions<LargeBenchmarkContext> options,
        int entityTypeCount
    ) : base(options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entityTypeCount);
        _entityTypeCount = entityTypeCount;
    }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        for (var index = 0; index < _entityTypeCount; index++)
        {
            var entityName = $"LargeBenchmarkEntity{index:D3}";
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>(
                entityName,
                entity =>
                {
                    entity.ToTable(entityName);
                    entity.IndexerProperty<int>("Id");
                    entity
                        .IndexerProperty<string>("Name")
                        .HasMaxLength(128);
                    entity
                        .IndexerProperty<string>("Payload")
                        .HasColumnType("json");
                    entity.HasKey("Id");
                });
        }
    }
}

/// <summary>
/// Materialized two-column projection used by projection benchmarks and workload
/// evidence. A concrete type prevents a terminal aggregate from replacing row
/// materialization.
/// </summary>
public sealed class BenchmarkProjection
{
    /// <summary>Gets the projected entity identifier.</summary>
    public int Id { get; init; }

    /// <summary>Gets the projected entity name.</summary>
    public string Name { get; init; } = string.Empty;
}
