using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.MySql.Examples;
using Microsoft.EntityFrameworkCore;

var database = ExampleDatabaseConfiguration.Create("doka_example_generated_columns");
var cancellationToken = CancellationToken.None;
var options = new DbContextOptionsBuilder<GeneratedColumnContext>()
    .UseMySql(database.ConnectionString, database.ServerVersion)
    .Options;

await using var context = new GeneratedColumnContext(options);
await context.Database.EnsureDeletedAsync(cancellationToken);

try
{
    await context.Database.EnsureCreatedAsync(cancellationToken);

    var item = new CatalogItem { Name = "Enterprise Provider" };
    context.Items.Add(item);
    await context.SaveChangesAsync(cancellationToken);
    await context.Entry(item).ReloadAsync(cancellationToken);

    if (item.NormalizedName != "enterprise provider" || item.NameLength != 19)
    {
        throw new InvalidOperationException("The generated columns were not materialized correctly.");
    }

    Console.WriteLine(
        $"{database.Target}: normalized='{item.NormalizedName}', length={item.NameLength}");
}
finally
{
    await context.Database.EnsureDeletedAsync(cancellationToken);
}

internal sealed class GeneratedColumnContext : DbContext
{
    public GeneratedColumnContext(
        DbContextOptions<GeneratedColumnContext> options
    ) : base(options) { }

    public DbSet<CatalogItem> Items => Set<CatalogItem>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<CatalogItem>(entity =>
        {
            entity.ToTable("CatalogItems");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(200);

            // The stored value is materialized on disk; the virtual value is
            // evaluated by the engine. Exercising both modes catches DDL drift.
            entity.Property(item => item.NormalizedName)
                .HasMaxLength(200)
                .HasComputedColumnSql("LOWER(`Name`)", stored: true);
            entity.Property(item => item.NameLength)
                .HasComputedColumnSql("CHAR_LENGTH(`Name`)", stored: false);
        });
    }
}

internal sealed class CatalogItem
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public int NameLength { get; set; }
}
