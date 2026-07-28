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
            entity.HasData(
                new MigrationWorkflowItem
                {
                    Id = 1,
                    Name = "migration-safety-readback",
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
}
