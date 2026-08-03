using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.MySql.Examples;
using Microsoft.EntityFrameworkCore;

var database = ExampleDatabaseConfiguration.Create("doka_example_multi_tenancy");
var cancellationToken = CancellationToken.None;
var options = new DbContextOptionsBuilder<TenantContext>()
    .UseMySql(database.ConnectionString, database.ServerVersion)
    .Options;

await using var lifecycleContext = new TenantContext(options, "lifecycle");
await lifecycleContext.Database.EnsureDeletedAsync(cancellationToken);

try
{
    await lifecycleContext.Database.EnsureCreatedAsync(cancellationToken);

    // Reusing the external ID proves that uniqueness is scoped to a tenant,
    // rather than accidentally becoming global across every customer.
    await using (var tenantA = new TenantContext(options, "tenant-a"))
    {
        tenantA.Documents.Add(new TenantDocument { ExternalId = "shared", Title = "Tenant A document" });
        await tenantA.SaveChangesAsync(cancellationToken);
    }

    await using (var tenantB = new TenantContext(options, "tenant-b"))
    {
        tenantB.Documents.Add(new TenantDocument { ExternalId = "shared", Title = "Tenant B document" });
        await tenantB.SaveChangesAsync(cancellationToken);
    }

    await using var verificationContext = new TenantContext(options, "tenant-a");
    var visibleDocuments = await verificationContext.Documents
        .AsNoTracking()
        .Select(document => document.Title)
        .ToListAsync(cancellationToken);
    var totalDocuments = await verificationContext.Documents
        .IgnoreQueryFilters()
        .AsNoTracking()
        .CountAsync(cancellationToken);

    if (visibleDocuments is not ["Tenant A document"] || totalDocuments != 2)
    {
        throw new InvalidOperationException("The tenant query filter did not isolate the expected row.");
    }

    Console.WriteLine(
        $"{database.Target}: tenant-a visible={visibleDocuments.Count}, unfiltered total={totalDocuments}");
}
finally
{
    await lifecycleContext.Database.EnsureDeletedAsync(cancellationToken);
}

internal sealed class TenantContext : DbContext
{
    private readonly string _tenantId;

    public TenantContext(
        DbContextOptions<TenantContext> options,
        string tenantId
    ) : base(options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        _tenantId = tenantId;
    }

    public DbSet<TenantDocument> Documents => Set<TenantDocument>();

    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default
    )
    {
        // Query filters protect reads only. Validate writes separately so an
        // attached entity cannot cross the tenant boundary unnoticed.
        EnforceTenantOwnership();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<TenantDocument>(entity =>
        {
            entity.ToTable("TenantDocuments");
            entity.HasKey(document => document.Id);
            entity.Property(document => document.TenantId).HasMaxLength(64);
            entity.Property(document => document.ExternalId).HasMaxLength(128);
            entity.Property(document => document.Title).HasMaxLength(200);

            // Customer-visible IDs need to be unique only inside one tenant.
            entity.HasIndex(document => new { document.TenantId, document.ExternalId }).IsUnique();

            // EF parameterizes the context-bound tenant value, allowing the
            // model and query shape to remain reusable between tenants.
            entity.HasQueryFilter(document => document.TenantId == _tenantId);
        });
    }

    private void EnforceTenantOwnership()
    {
        foreach (var entry in ChangeTracker.Entries<TenantDocument>())
        {
            // Assign an omitted tenant, but never overwrite an explicit
            // mismatch because doing so would conceal a boundary violation.
            if (entry.State == EntityState.Added && string.IsNullOrEmpty(entry.Entity.TenantId))
            {
                entry.Entity.TenantId = _tenantId;
            }

            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && !string.Equals(entry.Entity.TenantId, _tenantId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A tenant context cannot write another tenant's row.");
            }
        }
    }
}

internal sealed class TenantDocument
{
    public int Id { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public string ExternalId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}
