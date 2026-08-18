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

    // Exercise every EF Core save overload through a valid tenant write. The
    // overload matrix is part of the reusable isolation pattern, not merely a
    // test convenience, because callers may choose sync or async persistence.
    // Each tenant also reuses the same external IDs, proving that uniqueness
    // is scoped to a tenant rather than accidentally becoming global.
    using (var syncDefault = new TenantContext(options, "tenant-a"))
    {
        syncDefault.Documents.Add(new TenantDocument { ExternalId = "sync", Title = "Tenant A sync" });
        syncDefault.SaveChanges();
    }

    using (var syncBoolean = new TenantContext(options, "tenant-b"))
    {
        syncBoolean.Documents.Add(new TenantDocument { ExternalId = "sync", Title = "Tenant B sync" });
        syncBoolean.SaveChanges(acceptAllChangesOnSuccess: true);
    }

    await using (var asyncDefault = new TenantContext(options, "tenant-a"))
    {
        asyncDefault.Documents.Add(new TenantDocument { ExternalId = "async", Title = "Tenant A async" });
        await asyncDefault.SaveChangesAsync(cancellationToken);
    }

    await using (var asyncBoolean = new TenantContext(options, "tenant-b"))
    {
        asyncBoolean.Documents.Add(new TenantDocument { ExternalId = "async", Title = "Tenant B async" });
        await asyncBoolean.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    AssertMismatchedTenantRejected(options, context => context.SaveChanges());
    AssertMismatchedTenantRejected(
        options,
        context => context.SaveChanges(acceptAllChangesOnSuccess: true));
    await AssertMismatchedTenantRejectedAsync(
        options,
        (context, token) => context.SaveChangesAsync(token),
        cancellationToken);
    await AssertMismatchedTenantRejectedAsync(
        options,
        (context, token) => context.SaveChangesAsync(acceptAllChangesOnSuccess: true, token),
        cancellationToken);

    await using var verificationContext = new TenantContext(options, "tenant-a");
    var visibleDocuments = await verificationContext.Documents
        .AsNoTracking()
        .Select(document => document.Title)
        .ToListAsync(cancellationToken);

    var totalDocuments = await verificationContext.Documents
        .IgnoreQueryFilters()
        .AsNoTracking()
        .CountAsync(cancellationToken);

    if (visibleDocuments.Count != 2 || totalDocuments != 4)
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

static void AssertMismatchedTenantRejected(
    DbContextOptions<TenantContext> options,
    Action<TenantContext> save
)
{
    using var context = CreateMismatchedTenantContext(options);

    try
    {
        save(context);
    }
    catch (TenantBoundaryViolationException)
    {
        return;
    }

    throw new InvalidOperationException("A synchronous save overload bypassed the tenant write boundary.");
}

static async Task AssertMismatchedTenantRejectedAsync(
    DbContextOptions<TenantContext> options,
    Func<TenantContext, CancellationToken, Task<int>> save,
    CancellationToken cancellationToken
)
{
    await using var context = CreateMismatchedTenantContext(options);

    try
    {
        _ = await save(context, cancellationToken);
    }
    catch (TenantBoundaryViolationException)
    {
        return;
    }

    throw new InvalidOperationException("An asynchronous save overload bypassed the tenant write boundary.");
}

static TenantContext CreateMismatchedTenantContext(
    DbContextOptions<TenantContext> options
)
{
    var context = new TenantContext(options, "tenant-a");
    context.Documents.Add(
        new TenantDocument
        {
            TenantId = "tenant-b",
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = "Rejected cross-tenant write",
        });

    return context;
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

    public override int SaveChanges()
    {
        EnforceTenantOwnership();

        // Call the boolean base overload directly. Calling base.SaveChanges()
        // would dispatch back through this class's boolean override and run the
        // ownership scan twice for one persistence operation.
        return base.SaveChanges(acceptAllChangesOnSuccess: true);
    }

    public override int SaveChanges(
        bool acceptAllChangesOnSuccess
    )
    {
        EnforceTenantOwnership();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default
    )
    {
        // Query filters protect reads only. Validate writes separately so an
        // attached entity cannot cross the tenant boundary unnoticed.
        EnforceTenantOwnership();
        // Bypass the convenience overload for the same single-scan guarantee
        // as the synchronous path.
        return base.SaveChangesAsync(
            acceptAllChangesOnSuccess: true,
            cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        EnforceTenantOwnership();

        return base.SaveChangesAsync(
            acceptAllChangesOnSuccess,
            cancellationToken);
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
                throw new TenantBoundaryViolationException();
            }
        }
    }
}

/// <summary>
/// Identifies an attempted write across the context's tenant boundary without
/// coupling callers to exception-message text.
/// </summary>
internal sealed class TenantBoundaryViolationException : InvalidOperationException
{
    /// <summary>
    /// Initializes the exception with the stable tenant-boundary diagnostic.
    /// </summary>
    public TenantBoundaryViolationException()
        : base("A tenant context cannot write another tenant's row.")
    { }
}

internal sealed class TenantDocument
{
    public int Id { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public string ExternalId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}
