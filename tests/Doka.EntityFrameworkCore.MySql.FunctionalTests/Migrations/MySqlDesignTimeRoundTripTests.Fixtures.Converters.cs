using System.Text.Json.Nodes;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Test context for provider-owned converter design-time models.
/// </summary>
public sealed class ProviderConverterDesignContext : DbContext
{
    /// <summary>
    /// Creates the provider-owned converter design context.
    /// </summary>
    public ProviderConverterDesignContext(
        DbContextOptions<ProviderConverterDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<ProviderConverterDesignRecord>(entity =>
        {
            entity.ToTable("ProviderConverterDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.Element)
                .HasDefaultValue(JsonElement.Parse("""{"kind":"default-element"}"""));
            entity
                .Property(record => record.Document)
                .IsRequired()
                .HasDefaultValue(JsonDocument.Parse("""{"kind":"default-document"}"""));
            entity
                .Property(record => record.Node)
                .IsRequired()
                .HasDefaultValue(JsonNode.Parse("""{"kind":"default-node"}"""));
            entity
                .Property(record => record.ObjectValue)
                .IsRequired()
                .HasDefaultValue((JsonObject)JsonNode.Parse("""{"kind":"default-object"}""")!);
            entity
                .Property(record => record.Array)
                .IsRequired()
                .HasDefaultValue((JsonArray)JsonNode.Parse("""["default-array"]""")!);
            entity.Property(record => record.Version).IsRowVersion();
            entity.HasData(
                new
                {
                    Id = 1,
                    Element = JsonElement.Parse("""{"kind":"element","value":1}"""),
                    Document = JsonDocument.Parse("""{"kind":"document","value":2}"""),
                    Node = JsonNode.Parse("""{"kind":"node","value":3}"""),
                    ObjectValue = (JsonObject)JsonNode.Parse("""{"kind":"object","value":4}""")!,
                    Array = (JsonArray)JsonNode.Parse("""["array",5,true]""")!,
                    Version = MySqlBytesToDateTimeConverter.ToBytes(
                        new DateTime(2026, 8, 31, 12, 34, 56, DateTimeKind.Utc)),
                });
        });
    }
}

/// <summary>
/// Entity used by the provider-owned converter design model.
/// </summary>
public sealed class ProviderConverterDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the JSON element.
    /// </summary>
    public JsonElement Element { get; set; }

    /// <summary>
    /// Gets or sets the JSON document.
    /// </summary>
    public JsonDocument? Document { get; set; }

    /// <summary>
    /// Gets or sets the JSON node.
    /// </summary>
    public JsonNode? Node { get; set; }

    /// <summary>
    /// Gets or sets the JSON object.
    /// </summary>
    public JsonObject? ObjectValue { get; set; }

    /// <summary>
    /// Gets or sets the JSON array.
    /// </summary>
    public JsonArray? Array { get; set; }

    /// <summary>
    /// Gets or sets the row version.
    /// </summary>
    public byte[] Version { get; set; } = [];
}
