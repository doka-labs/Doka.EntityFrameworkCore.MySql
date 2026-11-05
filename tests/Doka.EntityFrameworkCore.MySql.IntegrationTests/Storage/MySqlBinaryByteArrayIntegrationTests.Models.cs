namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlBinaryByteArrayIntegrationTests
{
    /// <summary>Provides opaque binary values beside genuine and converted GUID properties.</summary>
    internal sealed class BinaryContext : DbContext
    {
        /// <summary>Uses the fixture's engine and execution-strategy options.</summary>
        public BinaryContext(
            DbContextOptions<BinaryContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<BinaryItem>();
            entity.ToTable(TableName);
            entity
                .Property(item => item.Id)
                .ValueGeneratedNever();

            entity
                .Property(item => item.Bytes)
                .HasColumnType("binary(16)")
                .IsRequired();

            entity
                .Property(item => item.OptionalBytes)
                .HasColumnType("binary(16)");

            entity
                .Property(item => item.Binary8)
                .HasColumnType("binary(8)")
                .IsRequired();

            entity
                .Property(item => item.Binary32)
                .HasColumnType("binary(32)")
                .IsRequired();

            entity
                .Property(item => item.Blob)
                .HasColumnType("longblob")
                .IsRequired();

            entity
                .Property(item => item.InferredBytes)
                .HasMaxLength(16)
                .IsRequired();

            // WHY: A deliberately little-endian converter detects accidental reinterpretation of provider bytes.
            entity
                .Property(item => item.ConvertedToken)
                .HasConversion(value => value.ToByteArray(), value => new Guid(value))
                .HasColumnType("binary(16)");
        }
    }

    /// <summary>Separates opaque binary data from normal and user-converted GUID controls.</summary>
    internal sealed class BinaryItem
    {
        /// <summary>Identifies the independently seeded payload case.</summary>
        public int Id { get; set; }

        /// <summary>Stores opaque bytes in the driver-sensitive fixed width.</summary>
        public byte[] Bytes { get; set; } = [];

        /// <summary>Exercises SQL NULL independently of the reader adapter.</summary>
        public byte[]? OptionalBytes { get; set; }

        /// <summary>Controls a fixed binary width below the driver's GUID width.</summary>
        public byte[] Binary8 { get; set; } = [];

        /// <summary>Controls a fixed binary width above the driver's GUID width.</summary>
        public byte[] Binary32 { get; set; } = [];

        /// <summary>Controls an explicitly selected BLOB mapping.</summary>
        public byte[] Blob { get; set; } = [];

        /// <summary>Controls the existing inferred VARBINARY(17) mapping.</summary>
        public byte[] InferredBytes { get; set; } = [];

        /// <summary>Verifies that ordinary GUID properties keep their configured format.</summary>
        public Guid Token { get; set; }

        /// <summary>Uses a user converter whose byte order differs from the driver's GUID order.</summary>
        public Guid ConvertedToken { get; set; }
    }
}
