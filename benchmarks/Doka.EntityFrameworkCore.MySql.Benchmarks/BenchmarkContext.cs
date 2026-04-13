namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

public sealed class BenchmarkContext : DbContext
{
    public BenchmarkContext(
        DbContextOptions<BenchmarkContext> options
    ) : base(options) { }

    public DbSet<BasicBenchmarkEntity> BasicEntities => Set<BasicBenchmarkEntity>();

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

public sealed class SpatialBenchmarkEntity
{
    public int Id { get; set; }

    public Point Location { get; set; } = default!;
}
