using Microsoft.EntityFrameworkCore;

namespace Doka.EntityFrameworkCore.MySql.Examples.MigrationsWorkflow;

/// <summary>
/// Represents the executable migration workflow model used by deployment examples
/// and release gates.
/// </summary>
public sealed class MigrationWorkflowContext : DbContext
{
    /// <summary>
    /// Creates a migration workflow context.
    /// </summary>
    /// <param name="options">Configured provider options.</param>
    public MigrationWorkflowContext(
        DbContextOptions<MigrationWorkflowContext> options
    ) : base(options)
    {
    }

    /// <summary>
    /// Gets the rows managed by the migration workflow model.
    /// </summary>
    public DbSet<MigrationWorkflowItem> Items => Set<MigrationWorkflowItem>();

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<MigrationWorkflowItem>(entity =>
        {
            entity.ToTable("MigrationWorkflowItems");
            entity.HasKey(item => item.Id);
            entity
                .Property(item => item.Name)
                .HasMaxLength(200);
            entity
                .Property(item => item.EffectiveDate)
                .HasDefaultValue(new DateOnly(2028, 2, 3));
            entity
                .Property(item => item.EffectiveTime)
                .HasPrecision(6)
                .HasDefaultValue(new TimeOnly(4, 5, 6, 654, 321));
            entity
                .Property(item => item.OccurredAt)
                .HasColumnType("timestamp(6)")
                .HasDefaultValueSql("'2026-08-21 12:34:56.000000'");
            entity.HasData(
                new MigrationWorkflowItem
                {
                    Id = 1,
                    Name = "migration-safety-readback",
                    EffectiveDate = new DateOnly(2026, 8, 17),
                    EffectiveTime = new TimeOnly(12, 34, 56, 123, 456),
                    OccurredAt = new DateTime(2026, 8, 21, 12, 34, 56),
                });
        });
    }
}

/// <summary>
/// Represents one row in the executable migration workflow model.
/// </summary>
public sealed class MigrationWorkflowItem
{
    /// <summary>
    /// Gets or sets the primary key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the date populated by the database migration default.
    /// </summary>
    public DateOnly EffectiveDate { get; set; }

    /// <summary>
    /// Gets or sets the time populated by the database migration default.
    /// </summary>
    public TimeOnly EffectiveTime { get; set; }

    /// <summary>
    /// Gets or sets the timestamp whose nullable-to-required migration uses an
    /// explicit SQL repair expression.
    /// </summary>
    public DateTime OccurredAt { get; set; }
}
