namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the models responsibility for MySqlTypeMappingBaselineTests.</summary>
public sealed partial class MySqlTypeMappingBaselineTests
{
    private sealed class TypeMappingContext : DbContext
    {
        public TypeMappingContext(
            DbContextOptions<TypeMappingContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.UseCollation("utf8mb4_bin");

            modelBuilder.Entity<TypeMappingEntity>(entity =>
            {
                entity.ToTable("Phase2TypeMappingEntities");
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.BoundedText)
                    .HasMaxLength(32);
                entity
                    .Property(item => item.FixedText)
                    .IsFixedLength()
                    .HasMaxLength(8);
                entity
                    .Property(item => item.BoundedBinary)
                    .HasMaxLength(64);
                entity
                    .Property(item => item.StringBackedStatus)
                    .HasConversion<string>()
                    .HasMaxLength(24);
                entity
                    .Property(item => item.ExplicitAmount)
                    .HasPrecision(18, 2);
                entity
                    .Property(item => item.AnsiHintText)
                    .HasMaxLength(20)
                    .IsUnicode(false);
                entity
                    .Property(item => item.CollatedText)
                    .HasMaxLength(40)
                    .UseCollation("utf8mb4_0900_as_cs");
                entity
                    .Property(item => item.ObservedAt)
                    .HasColumnType("timestamp(3)");
            });
        }
    }

    private sealed class ImplicitKeyedTextContext : DbContext
    {
        public ImplicitKeyedTextContext(
            DbContextOptions<ImplicitKeyedTextContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ImplicitKeyedTextEntity>(entity =>
            {
                entity.ToTable("Phase2ImplicitKeyedTextEntities");
                entity.HasKey(item => item.Code);
            });
        }
    }

    private sealed class ImplicitIndexedBinaryContext : DbContext
    {
        public ImplicitIndexedBinaryContext(
            DbContextOptions<ImplicitIndexedBinaryContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ImplicitIndexedBinaryEntity>(entity =>
            {
                entity.ToTable("Phase2ImplicitIndexedBinaryEntities");
                entity.HasKey(item => item.Id);
                entity.HasIndex(item => item.Token);
            });
        }
    }

    private sealed class TemporalPrecisionContext(
        DbContextOptions<TemporalPrecisionContext> options,
        int? precision,
        string? storeType
    ) : DbContext(options)
    {
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var property = modelBuilder
                .Entity<TemporalPrecisionEntity>()
                .Property(entity => entity.Value);

            if (precision is not null)
            {
                property.HasPrecision(precision.Value);
            }

            if (storeType is not null)
            {
                property.HasColumnType(storeType);
            }
        }
    }

    private sealed class JsonTemporalElementPrecisionContext(
        DbContextOptions<JsonTemporalElementPrecisionContext> options
    ) : DbContext(options)
    {
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonTemporalElementEntity>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity
                    .PrimitiveCollection(item => item.DateTimes)
                    .ElementType(element => element.HasPrecision(12));
                entity
                    .PrimitiveCollection(item => item.Times)
                    .ElementType(element => element.HasPrecision(12));
                entity
                    .PrimitiveCollection(item => item.Durations)
                    .ElementType(element => element.HasPrecision(12));
            });
        }
    }

    private sealed class ConnectionOpeningProbe : DbConnectionInterceptor
    {
        public int OpenCount { get; private set; }

        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result
        )
        {
            OpenCount++;

            return result;
        }
    }

    private sealed class TypeMappingEntity
    {
        public int Id { get; set; }

        public string BoundedText { get; set; } = string.Empty;

        public string FixedText { get; set; } = string.Empty;

        public string UnboundedText { get; set; } = string.Empty;

        public byte[] BoundedBinary { get; set; } = Array.Empty<byte>();

        public byte[] UnboundedBinary { get; set; } = Array.Empty<byte>();

        public StatusCode Status { get; set; }

        public StatusCode StringBackedStatus { get; set; }

        public decimal Amount { get; set; }

        public decimal ExplicitAmount { get; set; }

        public DateTime CreatedAt { get; set; }

        public TimeOnly OpenAt { get; set; }

        public DateTime ObservedAt { get; set; }

        public string AnsiHintText { get; set; } = string.Empty;

        public string CollatedText { get; set; } = string.Empty;
    }

    private sealed class ImplicitKeyedTextEntity
    {
        public string Code { get; set; } = string.Empty;
    }

    private sealed class ImplicitIndexedBinaryEntity
    {
        public int Id { get; set; }

        public byte[] Token { get; set; } = Array.Empty<byte>();
    }

    private sealed class TemporalPrecisionEntity
    {
        public int Id { get; set; }

        public DateTime Value { get; set; }
    }

    private sealed class JsonTemporalElementEntity
    {
        public int Id { get; set; }

        public List<DateTime> DateTimes { get; set; } = [];

        public List<TimeOnly> Times { get; set; } = [];

        public List<TimeSpan> Durations { get; set; } = [];
    }

    private enum StatusCode : short
    {
        Unknown = 0,
        Ready = 1,
    }

    private sealed class ValueConverterContext : DbContext
    {
        public ValueConverterContext(
            DbContextOptions<ValueConverterContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ValueConverterEntity>(entity =>
            {
                entity.ToTable("ValueConverterEntities");
                entity.HasKey(item => item.Id);

                entity
                    .Property(item => item.IsActiveText)
                    .HasConversion(v => v ? "Y" : "N", v => v == "Y")
                    .HasMaxLength(1);

                entity
                    .Property(item => item.ExplicitText)
                    .HasConversion(v => v ? "Y" : "N", v => v == "Y")
                    .HasMaxLength(1)
                    .HasColumnType("longtext");

                entity
                    .Property(item => item.ExplicitBlob)
                    .HasConversion(
                        value => BitConverter.GetBytes(value.UtcTicks),
                        bytes => new DateTimeOffset(BitConverter.ToInt64(bytes), TimeSpan.Zero))
                    .HasMaxLength(12)
                    .HasColumnType("longblob");

                entity
                    .Property(item => item.Price)
                    .HasConversion(v => v.Amount, v => new Money(v))
                    .HasPrecision(18, 2);

                entity
                    .Property(item => item.DurationTicks)
                    .HasConversion<long>();

                entity
                    .Property(item => item.Website)
                    .HasConversion(v => v == null ? null : v.AbsoluteUri, v => v == null ? null : new Uri(v))
                    .HasMaxLength(2048);

                entity
                    .Property(item => item.IsVerifiedInt)
                    .HasConversion<int>();

                entity
                    .Property(item => item.LastModified)
                    .HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(v, TimeSpan.Zero));
            });
        }
    }

    private sealed class ValueConverterEntity
    {
        public int Id { get; set; }

        public bool IsActiveText { get; set; }

        public bool ExplicitText { get; set; }

        public DateTimeOffset ExplicitBlob { get; set; }

        public bool IsVerifiedInt { get; set; }

        public Money Price { get; set; }

        public TimeSpan DurationTicks { get; set; }

        public Uri? Website { get; set; }

        public DateTimeOffset LastModified { get; set; }
    }

    private readonly record struct Money(decimal Amount);
}
