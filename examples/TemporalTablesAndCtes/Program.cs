using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.MySql.Examples;
using Microsoft.EntityFrameworkCore;

var database = ExampleDatabaseConfiguration.Create("doka_example_temporal_ctes");
var cancellationToken = CancellationToken.None;
var options = new DbContextOptionsBuilder<TemporalCteContext>()
    .UseMySql(database.ConnectionString, database.ServerVersion)
    .Options;

await using var context = new TemporalCteContext(options);
await context.Database.EnsureDeletedAsync(cancellationToken);

try
{
    await context.Database.EnsureCreatedAsync(cancellationToken);

    context.Documents.Add(new TemporalDocument { Name = "draft" });
    context.CteItems.AddRange(
        new CteItem { Name = "one", Score = 10 },
        new CteItem { Name = "two", Score = 20 },
        new CteItem { Name = "three", Score = 30 });
    await context.SaveChangesAsync(cancellationToken);

    await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);

    var document = await context.Documents.SingleAsync(cancellationToken);
    document.Name = "published";
    await context.SaveChangesAsync(cancellationToken);

    await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);

    context.Documents.Remove(document);
    await context.SaveChangesAsync(cancellationToken);
    context.ChangeTracker.Clear();

    var versions = await context.Documents
        .TemporalAll()
        .OrderBy(version => EF.Property<DateTime>(version, "ValidFrom"))
        .Select(version => new TemporalDocumentVersion(
            version.Name,
            EF.Property<DateTime>(version, "ValidFrom"),
            EF.Property<DateTime>(version, "ValidTo")))
        .ToListAsync(cancellationToken);

    if (versions.Count != 2
        || versions[0].Name != "draft"
        || versions[1].Name != "published"
        || context.ChangeTracker.Entries<TemporalDocument>().Any())
    {
        throw new InvalidOperationException(
            "TemporalAll did not return both historical versions without tracking them.");
    }

    var firstVersionStart = AsUtc(versions[0].ValidFrom);
    var firstVersionEnd = AsUtc(versions[0].ValidTo);
    var firstVersionMidpoint = firstVersionStart.AddTicks(
        (firstVersionEnd - firstVersionStart).Ticks / 2);

    var asOfName = await context.Documents
        .TemporalAsOf(firstVersionMidpoint)
        .Select(version => version.Name)
        .SingleAsync(cancellationToken);

    if (asOfName != "draft")
    {
        throw new InvalidOperationException(
            "TemporalAsOf did not return the version current at the requested UTC instant.");
    }

    const int upperBound = 3;

    // The SQL identifiers are fixed application text. Only the recursive bound
    // is interpolated, so EF Core binds it as a database parameter. The returned
    // query remains composable and the additional LINQ predicate executes on the
    // server instead of materializing the CTE first.
    var cteItems = await context.CteItems
        .FromSqlInterpolated($"""
            WITH RECURSIVE `numbers` (`Value`) AS (
                SELECT 1
                UNION ALL
                SELECT `Value` + 1
                FROM `numbers`
                WHERE `Value` < {upperBound}
            )
            SELECT item.`Id`, item.`Name`, item.`Score`
            FROM `CteItems` AS item
            INNER JOIN `numbers` AS number ON number.`Value` = item.`Id`
            """)
        .AsNoTracking()
        .Where(item => item.Score >= 20)
        .OrderBy(item => item.Id)
        .Select(item => item.Name)
        .ToListAsync(cancellationToken);

    if (!cteItems.SequenceEqual(["two", "three"])
        || context.ChangeTracker.Entries().Any())
    {
        throw new InvalidOperationException(
            "The recursive CTE did not compose, parameterize, and execute without tracking.");
    }

    Console.WriteLine(
        $"{database.Target}: temporalVersions={versions.Count}, cteRows={cteItems.Count}");
}
finally
{
    await context.Database.EnsureDeletedAsync(cancellationToken);
}

static DateTime AsUtc(
    DateTime value
) => value.Kind == DateTimeKind.Utc
    ? value
    : DateTime.SpecifyKind(value, DateTimeKind.Utc);

internal sealed class TemporalCteContext : DbContext
{
    public TemporalCteContext(
        DbContextOptions<TemporalCteContext> options
    ) : base(options) { }

    public DbSet<TemporalDocument> Documents => Set<TemporalDocument>();

    public DbSet<CteItem> CteItems => Set<CteItem>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<TemporalDocument>(entity =>
        {
            entity.ToTable(
                "TemporalDocuments",
                table => table.IsTemporal(temporal =>
                {
                    temporal.UseHistoryTable("TemporalDocumentHistory");
                    temporal.HasPeriodStart("ValidFrom").HasColumnName("ValidFrom");
                    temporal.HasPeriodEnd("ValidTo").HasColumnName("ValidTo");
                }));
            entity.HasKey(document => document.Id);
            entity.Property(document => document.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<CteItem>(entity =>
        {
            entity.ToTable("CteItems");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(200);
        });
    }
}

internal sealed class TemporalDocument
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

internal sealed class CteItem
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Score { get; set; }
}

internal sealed record TemporalDocumentVersion(
    string Name,
    DateTime ValidFrom,
    DateTime ValidTo);
