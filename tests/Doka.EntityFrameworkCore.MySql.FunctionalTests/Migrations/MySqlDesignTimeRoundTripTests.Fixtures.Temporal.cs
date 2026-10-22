namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Test context for generated system-time migration models.
/// </summary>
public sealed class SystemTimeDesignContext : DbContext
{
    /// <summary>
    /// Creates the test context.
    /// </summary>
    public SystemTimeDesignContext(
        DbContextOptions<SystemTimeDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<SystemTimeDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.ToTable(
                "TemporalRecords",
                table => table.IsTemporal(temporal =>
                {
                    temporal.UseHistoryTable("TemporalRecordsHistory");
                    temporal
                        .HasPeriodStart("ValidFrom")
                        .HasColumnName("valid_from");
                    temporal
                        .HasPeriodEnd("ValidTo")
                        .HasColumnName("valid_to");
                }));
            entity.OwnsOne(record => record.Details);
        });
    }
}

/// <summary>
/// Empty model used to verify initial temporal migration operations.
/// </summary>
public sealed class EmptyTemporalDesignContext : DbContext
{
    /// <summary>
    /// Creates the empty test context.
    /// </summary>
    public EmptyTemporalDesignContext(
        DbContextOptions<EmptyTemporalDesignContext> options
    ) : base(options) { }
}

/// <summary>
/// Entity used by the generated system-time migration model.
/// </summary>
public sealed class SystemTimeDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the table-split details.
    /// </summary>
    public SystemTimeDesignDetails Details { get; set; } = new();
}

/// <summary>
/// Owned details used by the generated system-time migration model.
/// </summary>
public sealed class SystemTimeDesignDetails
{
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description { get; set; } = null!;
}

/// <summary>
/// Test context for generated application-time migration models.
/// </summary>
public sealed class ApplicationTimeDesignContext : DbContext
{
    /// <summary>
    /// Creates the test context.
    /// </summary>
    public ApplicationTimeDesignContext(
        DbContextOptions<ApplicationTimeDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<ApplicationTimeDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.ToTable(
                "ApplicationTimeRecords",
                table => table.HasApplicationTimePeriod<ApplicationTimeDesignRecord>(applicationTime =>
                {
                    applicationTime.HasPeriodName("BusinessValidity");
                    applicationTime
                        .HasPeriodStart(record => record.BusinessFrom)
                        .HasColumnName("business_from");
                    applicationTime
                        .HasPeriodEnd(record => record.BusinessTo)
                        .HasColumnName("business_to");
                }));
        });
    }
}

/// <summary>
/// Entity used by the generated application-time migration model.
/// </summary>
public sealed class ApplicationTimeDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the inclusive application-time boundary.
    /// </summary>
    public DateTime BusinessFrom { get; set; }

    /// <summary>
    /// Gets or sets the exclusive application-time boundary.
    /// </summary>
    public DateTime BusinessTo { get; set; }
}

/// <summary>
/// Test context for generated bitemporal migration models.
/// </summary>
public sealed class BitemporalDesignContext : DbContext
{
    /// <summary>
    /// Creates the test context.
    /// </summary>
    public BitemporalDesignContext(
        DbContextOptions<BitemporalDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<BitemporalDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.ToTable(
                "BitemporalRecords",
                table =>
                {
                    table.IsTemporal(temporal =>
                    {
                        temporal
                            .HasPeriodStart("RecordedFrom")
                            .HasColumnName("recorded_from");
                        temporal
                            .HasPeriodEnd("RecordedTo")
                            .HasColumnName("recorded_to");
                    });
                    table.HasApplicationTimePeriod(applicationTime =>
                    {
                        applicationTime.HasPeriodName("BusinessValidity");
                        applicationTime
                            .HasPeriodStart("BusinessFrom")
                            .HasColumnName("business_from");
                        applicationTime
                            .HasPeriodEnd("BusinessTo")
                            .HasColumnName("business_to");
                        applicationTime.UseWithoutOverlaps();
                    });
                });
        });
    }
}

/// <summary>
/// Entity used by the generated bitemporal migration model.
/// </summary>
public sealed class BitemporalDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }
}
