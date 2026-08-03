using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.MySql.Examples;
using Microsoft.EntityFrameworkCore;

var database = ExampleDatabaseConfiguration.Create("doka_example_guid_formats");
var cancellationToken = CancellationToken.None;
var options = new DbContextOptionsBuilder<GuidFormatContext>()
    .UseMySql(
        database.ConnectionString,
        database.ServerVersion,
        // Keep the schema decision explicit even though Binary16 is the
        // current default; examples should not depend on an implicit default.
        provider => provider.DefaultGuidFormat(MySqlGuidFormat.Binary16))
    .Options;

await using var context = new GuidFormatContext(options);
await context.Database.EnsureDeletedAsync(cancellationToken);

try
{
    await context.Database.EnsureCreatedAsync(cancellationToken);

    var legacyId = Guid.NewGuid();
    var account = new Account { LegacyReference = legacyId, Name = "Doka Labs" };
    context.Accounts.Add(account);
    await context.SaveChangesAsync(cancellationToken);
    context.ChangeTracker.Clear();

    var roundTrip = await context.Accounts
        .AsNoTracking()
        .SingleAsync(candidate => candidate.Id == account.Id, cancellationToken);

    if (roundTrip.Id == Guid.Empty || roundTrip.LegacyReference != legacyId)
    {
        throw new InvalidOperationException("The binary and textual GUID values did not round-trip.");
    }

    Console.WriteLine(
        $"{database.Target}: binaryId={roundTrip.Id}, char36Reference={roundTrip.LegacyReference}");
}
finally
{
    await context.Database.EnsureDeletedAsync(cancellationToken);
}

internal sealed class GuidFormatContext : DbContext
{
    public GuidFormatContext(
        DbContextOptions<GuidFormatContext> options
    ) : base(options) { }

    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("Accounts");
            entity.HasKey(account => account.Id);

            // New keys use the compact format and are generated client-side;
            // the second property demonstrates an existing char(36) contract.
            entity.Property(account => account.Id)
                .HasMySqlGuidFormat(MySqlGuidFormat.Binary16)
                .UseMySqlClientGuidValueGeneration();
            entity.Property(account => account.LegacyReference)
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
            entity.Property(account => account.Name).HasMaxLength(200);
        });
    }
}

internal sealed class Account
{
    public Guid Id { get; set; }

    public Guid LegacyReference { get; set; }

    public string Name { get; set; } = string.Empty;
}
