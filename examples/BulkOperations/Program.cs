using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.MySql.Examples;
using Microsoft.EntityFrameworkCore;

var database = ExampleDatabaseConfiguration.Create("doka_example_bulk_operations");
var cancellationToken = CancellationToken.None;
var options = new DbContextOptionsBuilder<BulkOperationsContext>()
    .UseMySql(
        database.ConnectionString,
        database.ServerVersion,
        provider => provider.MaxBatchSize(25))
    .Options;

await using var context = new BulkOperationsContext(options);
await context.Database.EnsureDeletedAsync(cancellationToken);

try
{
    await context.Database.EnsureCreatedAsync(cancellationToken);

    var readings = Enumerable
        .Range(1, 100)
        .Select(sequence => new DeviceReading
        {
            Device = $"sensor-{sequence % 4}",
            Sequence = sequence,
            Processed = false,
        })
        .ToArray();

    context.Readings.AddRange(readings);
    var inserted = await context.SaveChangesAsync(cancellationToken);

    // ExecuteUpdate and ExecuteDelete translate directly to set-based SQL and
    // avoid materializing each affected row in the change tracker.
    var updated = await context.Readings
        .Where(reading => reading.Sequence <= 60)
        .ExecuteUpdateAsync(
            setters => setters.SetProperty(reading => reading.Processed, true),
            cancellationToken);
    var deleted = await context.Readings
        .Where(reading => reading.Processed)
        .ExecuteDeleteAsync(cancellationToken);
    var remaining = await context.Readings
        .AsNoTracking()
        .CountAsync(cancellationToken);

    if (inserted != 100 || updated != 60 || deleted != 60 || remaining != 40)
    {
        throw new InvalidOperationException("The bulk-operation invariants were not satisfied.");
    }

    Console.WriteLine(
        $"{database.Target}: inserted={inserted}, updated={updated}, deleted={deleted}, remaining={remaining}");
}
finally
{
    await context.Database.EnsureDeletedAsync(cancellationToken);
}

internal sealed class BulkOperationsContext : DbContext
{
    public BulkOperationsContext(
        DbContextOptions<BulkOperationsContext> options
    ) : base(options) { }

    public DbSet<DeviceReading> Readings => Set<DeviceReading>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<DeviceReading>(entity =>
        {
            entity.ToTable("DeviceReadings");
            entity.HasKey(reading => reading.Id);
            entity.Property(reading => reading.Device).HasMaxLength(64);
            entity.HasIndex(reading => new { reading.Device, reading.Sequence }).IsUnique();
        });
    }
}

internal sealed class DeviceReading
{
    public int Id { get; set; }

    public string Device { get; set; } = string.Empty;

    public int Sequence { get; set; }

    public bool Processed { get; set; }
}
