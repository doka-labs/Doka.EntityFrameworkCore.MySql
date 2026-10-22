namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Test context for generated MySQL-family invisible-column migration models.
/// </summary>
public sealed class InvisibleColumnDesignContext : DbContext
{
    /// <summary>
    /// Creates the test context.
    /// </summary>
    public InvisibleColumnDesignContext(
        DbContextOptions<InvisibleColumnDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<InvisibleColumnDesignRecord>(entity =>
        {
            entity.ToTable("InvisibleColumnDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.HiddenValue)
                .HasDefaultValue(string.Empty)
                .IsInvisible();
            entity
                .Property(record => record.VisibleValue)
                .IsInvisible(false);
        });
    }
}

/// <summary>
/// Entity used by the generated invisible-column migration model.
/// </summary>
public sealed class InvisibleColumnDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the hidden value.
    /// </summary>
    public string HiddenValue { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the explicitly visible value.
    /// </summary>
    public string VisibleValue { get; set; } = string.Empty;
}

/// <summary>
/// Test context for generated entity-splitting migration models.
/// </summary>
public sealed class EntitySplitDesignContext : DbContext
{
    /// <summary>
    /// Creates the entity-splitting design context.
    /// </summary>
    public EntitySplitDesignContext(
        DbContextOptions<EntitySplitDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<EntitySplitDesignRecord>(entity =>
        {
            entity.ToTable("EntitySplitDesignRecords");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Id).UseMySqlAutoIncrementColumn();
            entity.SplitToTable(
                "EntitySplitDesignDetails",
                split => split.Property(record => record.Description));
        });
    }
}

/// <summary>
/// Entity used by the generated entity-splitting migration model.
/// </summary>
public sealed class EntitySplitDesignRecord
{
    /// <summary>
    /// Gets or sets the generated key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the property held by the principal table.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the property held by the secondary table.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
