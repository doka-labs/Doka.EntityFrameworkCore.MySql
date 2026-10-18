namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    private sealed class MappingContext<TNode>(
        DbContextOptions<MappingContext<TNode>> options
    ) : DbContext(options)
        where TNode : class
    {
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            if (typeof(TNode) == typeof(TptLeaf))
            {
                modelBuilder.Entity<TptRoot>(entity =>
                {
                    entity.ToTable(MappingTable);
                    entity.UseTptMappingStrategy();
                    entity.Property(node => node.Id).ValueGeneratedNever();
                    entity.Property(node => node.Value).HasMaxLength(64);
                });
                modelBuilder.Entity<TptLeaf>(entity =>
                {
                    entity.ToTable(MappingExtraTable);
                    entity.Property(node => node.Detail).HasMaxLength(64);
                });
            }
            else if (typeof(TNode) == typeof(TpcRoot))
            {
                modelBuilder.Entity<TpcRoot>(entity =>
                {
                    entity.UseTpcMappingStrategy();
                    entity.Property(node => node.Id).ValueGeneratedNever();
                    entity.Property(node => node.Value).HasMaxLength(64);
                    entity.Property(node => node.Detail).HasMaxLength(64);
                });
                modelBuilder.Entity<TpcFirst>().ToTable(MappingTable);
                modelBuilder.Entity<TpcSecond>().ToTable(MappingExtraTable);
            }
            else if (typeof(TNode) == typeof(SplitMappedItem))
            {
                modelBuilder.Entity<SplitMappedItem>(entity =>
                {
                    entity.ToTable(MappingTable);
                    entity.Property(node => node.Id).ValueGeneratedNever();
                    entity.Property(node => node.Value).HasMaxLength(64);
                    entity.Property(node => node.Detail).HasMaxLength(64);
                    entity.SplitToTable(MappingExtraTable, table =>
                    {
                        table.Property(node => node.Id);
                        table.Property(node => node.Value);
                    });
                });
            }
            else if (typeof(TNode) == typeof(SharedMappedDetail))
            {
                modelBuilder.Entity<SharedMappedOwner>(entity =>
                {
                    entity.ToTable(MappingTable);
                    entity.Property(node => node.Id).ValueGeneratedNever();
                    entity.Property(node => node.Payload).HasMaxLength(64);
                    entity.HasOne(node => node.Details)
                        .WithOne()
                        .HasForeignKey<SharedMappedDetail>(node => node.Id);
                    entity.Navigation(node => node.Details).IsRequired();
                });
                modelBuilder.Entity<SharedMappedDetail>(entity =>
                {
                    entity.ToTable(MappingTable);
                    entity.Property(node => node.Id).ValueGeneratedNever();
                    entity.Property(node => node.Value).HasMaxLength(64);
                    entity.Property(node => node.Detail).HasMaxLength(64);
                });
            }
            else if (typeof(TNode) == typeof(OwnedMappedItem))
            {
                modelBuilder.Entity<OwnedMappedItem>(entity =>
                {
                    entity.ToTable(MappingTable);
                    entity.Property(node => node.Id).ValueGeneratedNever();
                    entity.Property(node => node.Value).HasMaxLength(64);
                    entity.OwnsOne(node => node.Contact, owned =>
                    {
                        owned.Property(contact => contact.Detail).HasMaxLength(64);
                    });
                    entity.Navigation(node => node.Contact).IsRequired();
                });
            }
            else if (typeof(TNode) == typeof(ShadowMappedItem))
            {
                modelBuilder.Entity<ShadowMappedItem>(entity =>
                {
                    entity.ToTable(MappingTable);
                    entity.Property<int>("Id").ValueGeneratedNever();
                    entity.Property<string>("Tenant").HasMaxLength(32);
                    entity.HasKey("Tenant", "Id");
                    entity.Property(node => node.Value).HasMaxLength(64);
                    entity.Property(node => node.Detail).HasMaxLength(64);
                });
            }
        }
    }

    private abstract class TptRoot
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;
    }

    private sealed class TptLeaf : TptRoot
    {
        public string Detail { get; set; } = string.Empty;
    }

    private abstract class TpcRoot
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;
    }

    private sealed class TpcFirst : TpcRoot;

    private sealed class TpcSecond : TpcRoot;

    private sealed class SplitMappedItem
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;
    }

    private sealed class SharedMappedOwner
    {
        public int Id { get; set; }

        public string Payload { get; set; } = string.Empty;

        public SharedMappedDetail Details { get; set; } = null!;
    }

    private sealed class SharedMappedDetail
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;
    }

    private sealed class OwnedMappedItem
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;

        public OwnedMappedContact Contact { get; set; } = null!;
    }

    private sealed class OwnedMappedContact
    {
        public string Detail { get; set; } = string.Empty;
    }

    private sealed class ShadowMappedItem
    {
        public string Value { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;
    }
}
