namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Test context for generated spatial SRID migration models.
/// </summary>
public sealed class SpatialSridDesignContext : DbContext
{
    /// <summary>
    /// Creates the spatial design-time context.
    /// </summary>
    public SpatialSridDesignContext(
        DbContextOptions<SpatialSridDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<SpatialSridDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.Location)
                .HasSrid(4326);
            entity.HasData(
                new SpatialSridDesignRecord
                {
                    Id = 1,
                    Location = new Point(13.4050, 52.5200)
                    {
                        SRID = 4326,
                    },
                });
        });
    }
}

/// <summary>
/// Entity used by the generated spatial SRID migration model.
/// </summary>
public sealed class SpatialSridDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the SRID-constrained location.
    /// </summary>
    public Point Location { get; set; } = null!;
}
