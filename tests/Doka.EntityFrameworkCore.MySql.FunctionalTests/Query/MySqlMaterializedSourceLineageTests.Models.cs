namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMaterializedSourceLineageTests
{
    /// <summary>Creates one offline model with explicit family configuration.</summary>
    private static LineageContext CreateContext(bool mariaDb)
    {
        var version = mariaDb
            ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

        var options = MySqlFunctionalTestOptions.CreateTransientBuilder<LineageContext>()
            .UseMySql("Server=localhost;Database=lineage;User ID=unused;", version)
            .Options;

        return new LineageContext(options);
    }

    /// <summary>Creates an application-time model without enabling an unsupported MySQL table feature.</summary>
    private static ApplicationContext CreateApplicationContext()
    {
        var options = MySqlFunctionalTestOptions.CreateTransientBuilder<ApplicationContext>()
            .UseMySql(
                "Server=localhost;Database=lineage;User ID=unused;",
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .Options;

        return new ApplicationContext(options);
    }

    /// <summary>Contains one properly configured application-time root and a separate reference table.</summary>
    private sealed class ApplicationContext : DbContext
    {
        /// <summary>Initializes the application-time model.</summary>
        public ApplicationContext(DbContextOptions<ApplicationContext> options) : base(options) { }

        /// <summary>Gets the mutation-only application-time entity root.</summary>
        public DbSet<HistoricalParent> Parents => Set<HistoricalParent>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<HistoricalParent>(entity =>
            {
                entity.ToTable("LineageApplicationParents", table => table.HasApplicationTimePeriod());
                entity.HasOne(parent => parent.Child).WithMany().HasForeignKey(parent => parent.ChildId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<HistoricalChild>().ToTable("LineageApplicationChildren");
        }
    }

    /// <summary>Contains exact shared mappings and independently stored historical navigation targets.</summary>
    private sealed class LineageContext : DbContext
    {
        /// <summary>Initializes the offline lineage model.</summary>
        public LineageContext(DbContextOptions<LineageContext> options) : base(options) { }

        /// <summary>Gets temporal parents with one separately stored reference.</summary>
        public DbSet<HistoricalParent> Parents => Set<HistoricalParent>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.SharedTypeEntity<SharedNode>("First", entity =>
            {
                entity.ToTable("LineageFirst");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.TargetId).HasColumnName("FirstTargetId");
                entity.HasOne(row => row.Target).WithMany().HasForeignKey(row => row.TargetId);
            });

            modelBuilder.SharedTypeEntity<SharedNode>("Second", entity =>
            {
                entity.ToTable("LineageSecond");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.TargetId).HasColumnName("SecondTargetId");
                entity.HasOne(row => row.Target).WithMany().HasForeignKey(row => row.TargetId);
            });

            modelBuilder.Entity<SharedTarget>().ToTable("LineageTargets");
            modelBuilder.Entity<HistoricalParent>(entity =>
            {
                entity.ToTable("LineageParents", table => table.IsTemporal());
                entity.HasOne(parent => parent.Child).WithMany().HasForeignKey(parent => parent.ChildId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<HistoricalChild>(entity =>
            {
                entity.ToTable("LineageChildren", table => table.IsTemporal());
                entity.HasOne(child => child.Next).WithMany().HasForeignKey(child => child.NextId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }

    /// <summary>Shares one CLR shape across two explicitly named entity mappings.</summary>
    private sealed class SharedNode
    {
        /// <summary>Gets or sets the mapped key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the scalar source payload.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the differently named foreign-key column.</summary>
        public int TargetId { get; set; }

        /// <summary>Gets or sets the configured navigation.</summary>
        public SharedTarget Target { get; set; } = null!;
    }

    /// <summary>Provides a separately stored navigation target for both named mappings.</summary>
    private sealed class SharedTarget
    {
        /// <summary>Gets or sets the target key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the target payload.</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Provides an independently versioned parent table.</summary>
    private sealed class HistoricalParent
    {
        /// <summary>Gets or sets the versioned entity key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the separately stored child key.</summary>
        public int ChildId { get; set; }

        /// <summary>Gets or sets the separately versioned reference.</summary>
        public HistoricalChild Child { get; set; } = null!;
    }

    /// <summary>Provides separately versioned targets with a second reference hop.</summary>
    private sealed class HistoricalChild
    {
        /// <summary>Gets or sets the versioned entity key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the versioned payload.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the optional next reference key.</summary>
        public int? NextId { get; set; }

        /// <summary>Gets or sets the next separately versioned reference.</summary>
        public HistoricalChild? Next { get; set; }
    }
}
