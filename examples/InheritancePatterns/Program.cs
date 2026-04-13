using Doka.EntityFrameworkCore.MySql;
using Microsoft.EntityFrameworkCore;

// ── Inheritance Patterns with Doka.EntityFrameworkCore.MySql ──
//
// Demonstrates: TPH with discriminator, OwnsOne (same-table owned type).

var connectionString = Environment.GetEnvironmentVariable("DOKA_MYSQL_CONNECTION_STRING")
    ?? "Server=localhost;Port=33068;Database=inheritance_example;User ID=root;Password=root_password;";

var options = new DbContextOptionsBuilder<InheritanceContext>()
    .UseMySql(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0)))
    .Options;

using var context = new InheritanceContext(options);
context.Database.EnsureCreated();

// TPH: Insert different animal types into the same table.
context.Animals.Add(new Dog { Name = "Rex", Breed = "Shepherd" });
context.Animals.Add(new Cat { Name = "Whiskers", IsIndoor = true });
context.SaveChanges();

// Query with OfType<T>() to filter by discriminator.
var dogs = context.Animals.OfType<Dog>().ToList();
Console.WriteLine($"Dogs: {dogs.Count}");

// OwnsOne: Customer with embedded Address.
context.Customers.Add(new Customer
{
    Name = "Alice",
    Address = new Address { Street = "123 Main St", City = "Berlin" },
});
context.SaveChanges();

var customer = context.Customers.First();
Console.WriteLine($"Customer: {customer.Name}, City: {customer.Address?.City}");

context.Database.EnsureDeleted();
Console.WriteLine("Inheritance patterns example completed successfully.");

// ── Entities ──

public abstract class Animal
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class Dog : Animal
{
    public string Breed { get; set; } = string.Empty;
}

public class Cat : Animal
{
    public bool IsIndoor { get; set; }
}

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Address? Address { get; set; }
}

public class Address
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}

public class InheritanceContext : DbContext
{
    public InheritanceContext(DbContextOptions<InheritanceContext> options) : base(options) { }

    public DbSet<Animal> Animals => Set<Animal>();
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Animal>(e =>
        {
            e.ToTable("Animals");
            e.HasDiscriminator<string>("Type")
                .HasValue<Dog>("Dog")
                .HasValue<Cat>("Cat");
            e.Property("Type").HasMaxLength(64);
            e.Property(a => a.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<Customer>(e =>
        {
            e.ToTable("Customers");
            e.Property(c => c.Name).HasMaxLength(100);
            e.OwnsOne(c => c.Address);
        });
    }
}
