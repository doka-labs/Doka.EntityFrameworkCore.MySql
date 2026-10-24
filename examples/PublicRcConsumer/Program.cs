var options = new DbContextOptionsBuilder<ConsumerContext>()
    .UseMySql(
        "Server=127.0.0.1;User ID=unused;Password=unused;Database=unused",
        MySqlServerVersion.Parse("8.4.0"),
        provider => provider.UseNetTopologySuite())
    .Options;

using var context = new ConsumerContext(options);

var sql = context.Set<ConsumerRow>()
    .Where(row => row.Name == "public-rc")
    .Select(row => row.Id)
    .ToQueryString();

if (!sql.Contains("SELECT", StringComparison.Ordinal)
    || !sql.Contains("public-rc", StringComparison.Ordinal)
    || context.Model.FindEntityType(typeof(ConsumerRow))?.FindProperty(nameof(ConsumerRow.Location)) is null
    || new MySqlCacheOptions().DefaultSlidingExpiration <= TimeSpan.Zero)
{
    throw new InvalidOperationException("The public provider, spatial or cache consumer assertion failed.");
}

// WHY: No database is contacted. The full provider/live/runtime gates already
// qualify the candidate; this fixture isolates public package consumption.
Console.WriteLine("DOKA_PUBLIC_RC_CONSUMER_PASS");

/// <summary>Exercises model creation and translation through restored packages.</summary>
internal sealed class ConsumerContext(DbContextOptions<ConsumerContext> options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<ConsumerRow>().Property(row => row.Name).HasMaxLength(64);
}

/// <summary>Includes a spatial property so the extension must participate.</summary>
internal sealed class ConsumerRow
{
    /// <summary>Gets or sets the primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the value used by the translation assertion.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the spatial extension's optional value.</summary>
    public Point? Location { get; set; }
}
