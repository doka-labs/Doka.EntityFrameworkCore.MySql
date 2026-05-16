using Doka.EntityFrameworkCore.MySql;
using Microsoft.EntityFrameworkCore;

// -- Retry and Resilience with Doka.EntityFrameworkCore.MySql --
//
// Demonstrates: EnableRetryOnFailure(), transient error handling, connection timeout configuration.

var connectionString = Environment.GetEnvironmentVariable("DOKA_MYSQL_CONNECTION_STRING")
    ?? "Server=localhost;Port=33068;Database=retry_example;User ID=root;Password=root_password;";

var options = new DbContextOptionsBuilder<RetryContext>()
    .UseMySql(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0)), mySqlOptions =>
    {
        // Enable retry on transient failures with up to 3 retries and 5-second max delay.
        mySqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5));
    })
    .Options;

using var context = new RetryContext(options);

Console.WriteLine("Retry and resilience configured:");
Console.WriteLine("  MaxRetryCount: 3");
Console.WriteLine("  MaxRetryDelay: 5s");
Console.WriteLine("Retry and resilience example completed successfully.");

public class RetryContext : DbContext
{
    public RetryContext(DbContextOptions<RetryContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RetryEntity>(e => { e.ToTable("RetryEntities"); });
    }
}

public class RetryEntity
{
    public int Id { get; set; }
    public string Value { get; set; } = string.Empty;
}
