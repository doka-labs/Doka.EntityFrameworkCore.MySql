using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.MySql.Examples;
using Microsoft.EntityFrameworkCore;

var database = ExampleDatabaseConfiguration.Create("doka_example_charset_collation");
var cancellationToken = CancellationToken.None;
var options = new DbContextOptionsBuilder<CollationContext>()
    .UseMySql(database.ConnectionString, database.ServerVersion)
    .Options;

await using var context = new CollationContext(options);
await context.Database.EnsureDeletedAsync(cancellationToken);

try
{
    await context.Database.EnsureCreatedAsync(cancellationToken);

    context.Labels.AddRange(
        new Label { Title = "Alpha", SearchCode = "DOKA" },
        new Label { Title = "alpha", SearchCode = "provider" });
    await context.SaveChangesAsync(cancellationToken);

    var binaryMatches = await context.Labels
        .AsNoTracking()
        .Where(label => label.Title == "alpha")
        .CountAsync(cancellationToken);
    var caseInsensitiveMatches = await context.Labels
        .AsNoTracking()
        .Where(label => label.SearchCode == "doka")
        .CountAsync(cancellationToken);

    if (binaryMatches != 1 || caseInsensitiveMatches != 1)
    {
        throw new InvalidOperationException("The configured collation semantics were not preserved.");
    }

    Console.WriteLine(
        $"{database.Target}: binaryMatches={binaryMatches}, caseInsensitiveMatches={caseInsensitiveMatches}");
}
finally
{
    await context.Database.EnsureDeletedAsync(cancellationToken);
}

internal sealed class CollationContext : DbContext
{
    public CollationContext(
        DbContextOptions<CollationContext> options
    ) : base(options) { }

    public DbSet<Label> Labels => Set<Label>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        // A model default keeps newly introduced string properties on utf8mb4
        // unless a narrower schema contract overrides them deliberately.
        modelBuilder.HasCharSet("utf8mb4");

        modelBuilder.Entity<Label>(entity =>
        {
            entity.ToTable("Labels");
            entity.HasCharSet("utf8mb4");
            entity.UseStorageEngine("InnoDB");
            entity.HasKey(label => label.Id);

            // Identity-like titles remain byte- and case-sensitive, while the
            // search code models a user-facing case-insensitive lookup.
            entity.Property(label => label.Title)
                .HasMaxLength(200)
                .UseCollation("utf8mb4_bin");
            entity.Property(label => label.SearchCode)
                .HasMaxLength(64)
                .UseCollation("utf8mb4_unicode_ci");

            // The prefix is part of the provider's index metadata and avoids
            // indexing all bytes of a long utf8mb4 value.
            entity.HasIndex(label => label.Title).HasPrefixLength(32);
        });
    }
}

internal sealed class Label
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string SearchCode { get; set; } = string.Empty;
}
