using System.Text.Json.Nodes;
using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.MySql.Examples;
using Microsoft.EntityFrameworkCore;

var database = ExampleDatabaseConfiguration.Create("doka_example_json_columns");
var cancellationToken = CancellationToken.None;
var options = new DbContextOptionsBuilder<JsonColumnContext>()
    .UseMySql(database.ConnectionString, database.ServerVersion)
    .Options;

await using var context = new JsonColumnContext(options);
await context.Database.EnsureDeletedAsync(cancellationToken);

try
{
    await context.Database.EnsureCreatedAsync(cancellationToken);

    var payload = new JsonObject
    {
        ["status"] = "active",
        ["attempts"] = 3,
        ["tags"] = new JsonArray("provider", "mysql"),
    };

    var serializedPayload = payload.ToJsonString();
    context.Documents.Add(new JsonDocumentEntity
    {
        Name = "release-candidate",
        Payload = payload,
        SearchDocument = serializedPayload,
    });
    await context.SaveChangesAsync(cancellationToken);
    context.ChangeTracker.Clear();

    var roundTrip = await context.Documents.AsNoTracking().SingleAsync(cancellationToken);

    // Both expressions stay server-side. SearchDocument is intentionally text
    // because the provider's EF.Functions JSON surface accepts SQL JSON input.
    var activeDocuments = await context.Documents
        .AsNoTracking()
        .Where(document => EF.Functions.JsonContains(document.SearchDocument, "{\"status\":\"active\"}"))
        .CountAsync(cancellationToken);

    var depth = await context.Documents
        .AsNoTracking()
        .Select(document => EF.Functions.JsonDepth(document.SearchDocument))
        .SingleAsync(cancellationToken);

    if (!JsonNode.DeepEquals(payload, roundTrip.Payload) || activeDocuments != 1 || depth != 3)
    {
        throw new InvalidOperationException("The JSON round-trip or server-side JSON query failed.");
    }

    Console.WriteLine($"{database.Target}: activeDocuments={activeDocuments}, jsonDepth={depth}");
}
finally
{
    await context.Database.EnsureDeletedAsync(cancellationToken);
}

internal sealed class JsonColumnContext : DbContext
{
    public JsonColumnContext(
        DbContextOptions<JsonColumnContext> options
    ) : base(options) { }

    public DbSet<JsonDocumentEntity> Documents => Set<JsonDocumentEntity>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<JsonDocumentEntity>(entity =>
        {
            entity.ToTable("Documents");
            entity.HasKey(document => document.Id);
            entity.Property(document => document.Name).HasMaxLength(200);

            // Payload exercises the CLR converter and deep comparer, while the
            // text property exercises query translation over the same JSON.
            entity.Property(document => document.Payload).HasColumnType("json");
            entity.Property(document => document.SearchDocument).HasColumnType("json");
        });
    }
}

internal sealed class JsonDocumentEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public JsonObject? Payload { get; set; }

    public string SearchDocument { get; set; } = string.Empty;
}
