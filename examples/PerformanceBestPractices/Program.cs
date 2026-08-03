using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.MySql.Examples;
using Microsoft.EntityFrameworkCore;

var database = ExampleDatabaseConfiguration.Create("doka_example_performance");
var cancellationToken = CancellationToken.None;
var options = new DbContextOptionsBuilder<PerformanceContext>()
    .UseMySql(
        database.ConnectionString,
        database.ServerVersion,
        provider => provider.MaxBatchSize(100))
    .Options;

await using var context = new PerformanceContext(options);
await context.Database.EnsureDeletedAsync(cancellationToken);

try
{
    await context.Database.EnsureCreatedAsync(cancellationToken);

    context.Products.AddRange(
        Enumerable.Range(1, 250).Select(index => new PerformanceProduct
        {
            Name = $"product-{index:D3}",
            Price = index,
            IsActive = index % 5 != 0,
        }));
    await context.SaveChangesAsync(cancellationToken);
    context.ChangeTracker.Clear();

    // Compiled queries remove repeated expression-compilation overhead on a
    // genuinely hot, stable query shape. They are not a default for every query.
    var activeProductsByMinimumPrice = EF.CompileAsyncQuery((
        PerformanceContext queryContext,
        decimal minimumPrice) => queryContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive && product.Price >= minimumPrice)
            .OrderBy(product => product.Price)
            .Select(product => new PerformanceProductSummary(product.Name, product.Price))
            .Take(20));

    var results = new List<PerformanceProductSummary>();
    await foreach (var product in activeProductsByMinimumPrice(context, 200m)
                       .WithCancellation(cancellationToken))
    {
        results.Add(product);
    }

    if (results.Count != 20 || context.ChangeTracker.Entries().Any())
    {
        throw new InvalidOperationException("The read path tracked entities or returned an unexpected result set.");
    }

    Console.WriteLine(
        $"{database.Target}: projected={results.Count}, tracked={context.ChangeTracker.Entries().Count()}");
}
finally
{
    await context.Database.EnsureDeletedAsync(cancellationToken);
}

internal sealed class PerformanceContext : DbContext
{
    public PerformanceContext(
        DbContextOptions<PerformanceContext> options
    ) : base(options) { }

    public DbSet<PerformanceProduct> Products => Set<PerformanceProduct>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<PerformanceProduct>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(product => product.Id);
            entity.Property(product => product.Name).HasMaxLength(200);
            entity.Property(product => product.Price).HasPrecision(18, 2);
            entity.HasIndex(product => new { product.IsActive, product.Price });
        });
    }
}

internal sealed class PerformanceProduct
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public bool IsActive { get; set; }
}

internal sealed class PerformanceProductSummary
{
    public PerformanceProductSummary(
        string name,
        decimal price
    )
    {
        Name = name;
        Price = price;
    }

    public string Name { get; }

    public decimal Price { get; }
}
