namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Defines native UUID models alongside explicit binary, text, and application-converter mappings.</summary>
public sealed partial class MySqlMariaDbUuidIntegrationTests
{
    internal sealed class NativeUuidContext(
        DbContextOptions<NativeUuidContext> options
    ) : DbContext(options)
    {
        /// <summary>Provides principals and their native UUID relationships.</summary>
        public DbSet<NativeUuidPrincipal> Principals => Set<NativeUuidPrincipal>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<NativeUuidPrincipal>(entity =>
            {
                entity.ToTable(PrincipalTable);
                entity.HasKey(principal => principal.Id);
                entity.Property(principal => principal.BinaryId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity.Property(principal => principal.CharId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity.Property(principal => principal.ConvertedId)
                    .HasConversion<byte[]>();
                entity.HasData(new NativeUuidPrincipal
                {
                    Id = s_seededId,
                    OptionalNativeId = s_optionalId,
                    BinaryId = Guid.Parse("89a78261-ea26-494e-a520-b518f51ed3d1"),
                    CharId = Guid.Parse("1e326748-be2c-4856-bf8d-cffc79c192cf"),
                    ConvertedId = s_convertedSeedId,
                    Name = "seeded",
                });
            });

            modelBuilder.Entity<NativeUuidDependent>(entity =>
            {
                entity.ToTable(DependentTable);
                entity.HasKey(dependent => dependent.Id);
                entity.HasOne(dependent => dependent.Principal)
                    .WithMany(principal => principal.Dependents)
                    .HasForeignKey(dependent => dependent.PrincipalId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasData(new NativeUuidDependent
                {
                    Id = 1,
                    PrincipalId = s_seededId,
                    Name = "seeded-related",
                });
            });
        }
    }

    internal sealed class NativeUuidPrincipal
    {
        public Guid Id { get; set; }

        public Guid? OptionalNativeId { get; set; }

        public Guid BinaryId { get; set; }

        public Guid CharId { get; set; }

        public Guid ConvertedId { get; set; }

        public string Name { get; set; } = string.Empty;

        public ICollection<NativeUuidDependent> Dependents { get; } = [];
    }

    internal sealed class NativeUuidDependent
    {
        public int Id { get; set; }

        public Guid PrincipalId { get; set; }

        public string Name { get; set; } = string.Empty;

        public NativeUuidPrincipal Principal { get; set; } = null!;
    }

    private sealed class NativeUuidOptInContext(
        DbContextOptions<NativeUuidOptInContext> options
    ) : DbContext(options)
    {
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<NativeUuidOptInEntity>(entity =>
            {
                entity.ToTable(OptInTable);
                entity.HasKey(value => value.Id);
                entity.Property(value => value.NativeId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Uuid);
            });
        }
    }

    private sealed class NativeUuidOptInEntity
    {
        public int Id { get; set; }

        public Guid NativeId { get; set; }

        public Guid BinaryId { get; set; }
    }

    internal sealed class VersionedUuidContext(
        DbContextOptions<VersionedUuidContext> options
    ) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<VersionedUuidEntity>(entity =>
            {
                entity.ToTable(VersionedTable);
                entity.HasKey(value => value.NativeId);
                entity.Property(value => value.BinaryId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity.Property(value => value.CharId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
            });
        }
    }

    internal sealed class VersionedUuidEntity
    {
        public int Id { get; set; }

        public Guid NativeId { get; set; }

        public Guid BinaryId { get; set; }

        public Guid CharId { get; set; }
    }

    private sealed class Char36BytesContext(
        DbContextOptions<Char36BytesContext> options
    ) : DbContext(options)
    {
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<Char36BytesEntity>(entity =>
            {
                entity.ToTable(Char36BytesTable);
                entity.HasKey(value => value.Id);
                entity.Property(value => value.ConvertedId)
                    .HasConversion<byte[]>();
            });
        }
    }

    private sealed class Char36BytesEntity
    {
        public int Id { get; set; }

        public Guid ConvertedId { get; set; }
    }
}
