namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the models responsibility for MySqlMigrationDdlCoverageTests.</summary>
public sealed partial class MySqlMigrationDdlCoverageTests
{
    // -- Entities / Contexts --

    private sealed class DdlCoverageContext : DbContext
    {
        public DdlCoverageContext(
            DbContextOptions<DdlCoverageContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<DdlEntity>(e => { e.HasKey(x => x.Id); });
    }

    private sealed class DdlEntity
    {
        public int Id { get; set; }
    }

    private sealed class JsonFuncContext : DbContext
    {
        public JsonFuncContext(
            DbContextOptions<JsonFuncContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Data)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class JsonEntity
    {
        public int Id { get; set; }
        public string Data { get; set; } = "{}";
    }

    private sealed class TemporalContext : DbContext
    {
        public TemporalContext(
            DbContextOptions<TemporalContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<TemporalEntity>(e => e.HasKey(x => x.Id));
    }

    private sealed class TemporalEntity
    {
        public int Id { get; set; }
        public TimeOnly StartTime { get; set; }
    }

    private sealed class TimestampMappingContext : DbContext
    {
        public TimestampMappingContext(
            DbContextOptions<TimestampMappingContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<TimestampMappingEntity>(entity =>
        {
            entity.ToTable("Entries");
            entity.HasKey(candidate => candidate.Id);
            entity
                .Property(candidate => candidate.OccurredAt)
                .HasColumnType("timestamp(6)");
        });
    }

    private sealed class TimestampMappingEntity
    {
        public int Id { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    private sealed class ConstraintTestContext : DbContext
    {
        public ConstraintTestContext(
            DbContextOptions<ConstraintTestContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ParentEntity>(e =>
            {
                e.ToTable("Parents");
                e.HasKey(x => x.Id);
            });

            // The long table name makes the convention-generated foreign-key name exceed
            // the shared MySQL/MariaDB identifier limit before normalization.
            modelBuilder.Entity<ChildEntity>(e =>
            {
                e.ToTable("ChildrenWithVeryLongTableNameThatExceedsTheSixtyFourCharacterLimit");
                e.HasKey(x => x.Id);
                e
                    .HasOne<ParentEntity>()
                    .WithMany()
                    .HasForeignKey(x => x.ParentId);
            });
        }
    }

    private sealed class ParentEntity
    {
        public int Id { get; set; }
    }

    private sealed class ChildEntity
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
    }

    private sealed class HiLoShortContext : DbContext
    {
        public HiLoShortContext(
            DbContextOptions<HiLoShortContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<HiLoShortEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Id)
                    .UseHiLo("ShortSeq");
            });
        }
    }

    private sealed class HiLoShortEntity
    {
        public short Id { get; set; }
    }
}
