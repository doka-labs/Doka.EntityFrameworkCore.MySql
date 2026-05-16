using Doka.EntityFrameworkCore.MySql;
using Microsoft.EntityFrameworkCore;

// -- Getting Started with Doka.EntityFrameworkCore.MySql --
//
// This example demonstrates the minimal setup for using the Doka MySQL provider:
// 1. Configure UseMySql() with a connection string and server version
// 2. Define a simple entity
// 3. Create the database and insert/query a row
//
// Prerequisites: A running MySQL 8.4 instance (use docker-compose.yml)

var connectionString = Environment.GetEnvironmentVariable("DOKA_MYSQL_CONNECTION_STRING")
    ?? "Server=localhost;Port=33068;Database=getting_started;User ID=root;Password=root_password;";

var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
optionsBuilder.UseMySql(connectionString, serverVersion);

using var context = new AppDbContext(optionsBuilder.Options);

// Create the database and tables if they don't exist.
context.Database.EnsureCreated();

// Insert a new entity.
context.Products.Add(new Product { Name = "Widget", Price = 9.99m });
context.SaveChanges();

// Query all products.
var products = context.Products.ToList();
foreach (var product in products)
{
    Console.WriteLine($"Product #{product.Id}: {product.Name} -- {product.Price:C}");
}

// Clean up.
context.Database.EnsureDeleted();

Console.WriteLine("Getting started example completed successfully.");

// -- DbContext --

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(100);
            entity.Property(p => p.Price).HasPrecision(18, 2);
        });
    }
}

// -- Entity --

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
