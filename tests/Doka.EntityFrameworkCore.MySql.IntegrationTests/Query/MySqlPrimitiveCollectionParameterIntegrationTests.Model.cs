namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Defines collection test mappings and converted value types.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterIntegrationTests
{
    private sealed class PrimitiveCollectionContext(
        DbContextOptions<PrimitiveCollectionContext> options
    ) : DbContext(options)
    {
        public DbSet<PrimitiveCollectionEntity> Items => Set<PrimitiveCollectionEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<PrimitiveCollectionEntity>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .ValueGeneratedNever();
                entity
                    .Property(item => item.BinaryId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity
                    .Property(item => item.OptionalBinaryId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity
                    .Property(item => item.OptionalCharId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity
                    .Property(item => item.CharId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity
                    .Property(item => item.Name)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.OptionalName)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.ConvertedName)
                    .HasConversion(
                        value => "db-" + value,
                        value => value.Substring(3))
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.CaseInsensitiveName)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_unicode_ci");
                entity
                    .Property(item => item.ConvertedText)
                    .HasConversion(
                        value => value.Value,
                        value => new TextValue(value))
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.Status)
                    .HasConversion<string>()
                    .HasMaxLength(16)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.OptionalStatus)
                    .HasConversion<string>()
                    .HasMaxLength(16)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.ConvertedPrice)
                    .HasConversion(
                        value => value.Value,
                        value => new PriceValue(value))
                    .HasPrecision(10, 1);
                entity
                    .Property(item => item.ConvertedGuid)
                    .HasConversion(
                        value => value.Value,
                        value => new StrongGuid(value))
                    .HasColumnType("binary(16)");
                entity
                    .Property(item => item.ConvertedDuration)
                    .HasConversion(
                        value => value.Value,
                        value => new ElapsedValue(value))
                    .HasPrecision(0);
                entity
                    .Property(item => item.ConvertedTimestamp)
                    .HasConversion(
                        value => value.UtcDateTime,
                        value => new DateTimeOffset(value, TimeSpan.Zero))
                    .HasPrecision(0);
                entity
                    .Property(item => item.ConvertedPayload)
                    .HasConversion(
                        value => Encoding.UTF8.GetBytes(value.Value),
                        value => new BinaryValue(Encoding.UTF8.GetString(value)))
                    .HasColumnType("varbinary(32)");
                entity
                    .Property(item => item.Payload)
                    .HasColumnType("varbinary(32)");
                entity
                    .PrimitiveCollection(item => item.GuidValues)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class CharGuidCollectionContext(
        DbContextOptions<CharGuidCollectionContext> options
    ) : DbContext(options)
    {
        public DbSet<CharGuidCollectionEntity> Items => Set<CharGuidCollectionEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<CharGuidCollectionEntity>(entity =>
            {
                entity.ToTable(CharTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .ValueGeneratedNever();
                entity
                    .PrimitiveCollection(item => item.GuidValues)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class SqlModeStringCollectionContext(
        DbContextOptions<SqlModeStringCollectionContext> options
    ) : DbContext(options)
    {
        public DbSet<SqlModeStringCollectionEntity> Items => Set<SqlModeStringCollectionEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SqlModeStringCollectionEntity>(entity =>
            {
                entity.ToTable(SqlModeTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .ValueGeneratedNever();
                entity
                    .Property(item => item.Value)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.OptionalValue)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.ConvertedValue)
                    .HasConversion(
                        value => "db-" + value,
                        value => value.Substring(3))
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.ConvertedText)
                    .HasConversion(
                        value => value.Value,
                        value => new TextValue(value))
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .PrimitiveCollection(item => item.Values)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class PrimitiveCollectionEntity
    {
        public int Id { get; set; }

        public Guid BinaryId { get; set; }

        public Guid? OptionalBinaryId { get; set; }

        public Guid? OptionalCharId { get; set; }

        public Guid CharId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? OptionalName { get; set; }

        public string ConvertedName { get; set; } = string.Empty;

        public string CaseInsensitiveName { get; set; } = string.Empty;

        public TextValue ConvertedText { get; set; }

        public CollectionStatus Status { get; set; }

        public CollectionStatus? OptionalStatus { get; set; }

        public PriceValue ConvertedPrice { get; set; }

        public StrongGuid ConvertedGuid { get; set; }

        public ElapsedValue ConvertedDuration { get; set; }

        public DateTimeOffset ConvertedTimestamp { get; set; }

        public BinaryValue ConvertedPayload { get; set; }

        public byte[] Payload { get; set; } = [];

        public Guid[] GuidValues { get; set; } = [];
    }

    private enum CollectionStatus
    {
        Inactive,
        Active,
    }

    private readonly record struct PriceValue(decimal Value);

    private readonly record struct StrongGuid(Guid Value);

    private readonly record struct ElapsedValue(TimeSpan Value);

    private readonly record struct TextValue(string Value);

    private readonly record struct BinaryValue(string Value);

    private sealed class CharGuidCollectionEntity
    {
        public int Id { get; set; }

        public Guid[] GuidValues { get; set; } = [];
    }

    private sealed class SqlModeStringCollectionEntity
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;

        public string? OptionalValue { get; set; }

        public string ConvertedValue { get; set; } = string.Empty;

        public TextValue ConvertedText { get; set; }

        public string?[] Values { get; set; } = [];
    }
}
