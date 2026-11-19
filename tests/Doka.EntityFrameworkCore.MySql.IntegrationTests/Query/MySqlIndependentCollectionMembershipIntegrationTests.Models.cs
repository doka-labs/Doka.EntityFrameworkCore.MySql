namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Defines indexed storage and nullable converted values for independent membership.</summary>
public sealed partial class MySqlIndependentCollectionMembershipIntegrationTests
{
    private sealed class MembershipContext(
        DbContextOptions<MembershipContext> options,
        bool nativeUuid
    ) : DbContext(options)
    {
        public DbSet<MembershipEntity> Items => Set<MembershipEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<MembershipEntity>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(row => row.Id);
                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.BinaryId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);

                entity
                    .Property(row => row.OptionalBinaryId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);

                entity
                    .Property(row => row.CharId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);

                entity
                    .Property(row => row.OptionalCharId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);

                // WHY: MySQL has no native UUID type. Only MariaDB cases exercise
                // these properties as UUID; the shared corpus stays valid on both families.
                var nativeFormat = nativeUuid
                    ? MySqlGuidFormat.Uuid
                    : MySqlGuidFormat.Char36;

                entity
                    .Property(row => row.NativeId)
                    .HasMySqlGuidFormat(nativeFormat);

                entity
                    .Property(row => row.OptionalNativeId)
                    .HasMySqlGuidFormat(nativeFormat);

                entity
                    .HasIndex(row => row.BinaryId)
                    .IsUnique();

                entity
                    .HasIndex(row => row.CharId)
                    .IsUnique();

                entity
                    .HasIndex(row => row.NativeId)
                    .IsUnique();

                entity
                    .Property(row => row.Name)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");

                entity
                    .Property(row => row.Status)
                    .HasConversion<string>()
                    .HasMaxLength(16);
            });
        }
    }

    private sealed class MembershipEntity
    {
        public int Id { get; set; }

        public Guid BinaryId { get; set; }

        public Guid? OptionalBinaryId { get; set; }

        public Guid CharId { get; set; }

        public Guid? OptionalCharId { get; set; }

        public Guid NativeId { get; set; }

        public Guid? OptionalNativeId { get; set; }

        public string? Name { get; set; }

        public MembershipStatus? Status { get; set; }
    }

    private enum MembershipStatus
    {
        Inactive,
        Active,
    }
}
