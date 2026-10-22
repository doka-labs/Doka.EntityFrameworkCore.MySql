namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Test context for generated database-default migration metadata.
/// </summary>
public sealed class DatabaseCollationDesignContext : DbContext
{
    /// <summary>
    /// Creates the database-default design-time context.
    /// </summary>
    public DatabaseCollationDesignContext(
        DbContextOptions<DatabaseCollationDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder
            .HasCharSet("utf8mb4")
            .UseCollation("utf8mb4_unicode_ci");

        modelBuilder.Entity<DatabaseCollationDesignRecord>(entity =>
        {
            entity.ToTable("DatabaseCollationDesignRecords");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Name).HasMaxLength(64);
        });
    }
}

/// <summary>
/// Entity used by the generated database-default migration model.
/// </summary>
public sealed class DatabaseCollationDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the inherited-collation value.
    /// </summary>
    public string Name { get; set; } = null!;
}
