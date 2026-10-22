namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the models cases and shared support.</summary>
public sealed partial class MySqlNewFeatureTests
{
    // -- Entities and Contexts --

    private sealed class HiLoEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class HiLoContext : DbContext
    {
        public HiLoContext(
            DbContextOptions<HiLoContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<HiLoEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Id)
                    .UseHiLo("HiLoTestSequence");
            });
        }
    }

    private sealed class HiLoDefaultEntity
    {
        public int Id { get; set; }
    }

    private sealed class HiLoDefaultContext : DbContext
    {
        public HiLoDefaultContext(
            DbContextOptions<HiLoDefaultContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<HiLoDefaultEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Id)
                    .UseHiLo();
            });
        }
    }

    private sealed class DateTimeEntity
    {
        public int Id { get; set; }
        public DateOnly BirthDate { get; set; }
        public TimeOnly StartTime { get; set; }
    }

    private sealed class DateTimeContext : DbContext
    {
        public DateTimeContext(
            DbContextOptions<DateTimeContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<DateTimeEntity>(e => { e.HasKey(x => x.Id); });
    }

    private sealed class InvisibleEntity
    {
        public int Id { get; set; }
        public string InternalData { get; set; } = "";
    }

    private sealed class InvisibleContext : DbContext
    {
        public InvisibleContext(
            DbContextOptions<InvisibleContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<InvisibleEntity>(e =>
            {
                e.ToTable("InvisibleEntities");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.InternalData)
                    .HasDefaultValue(string.Empty)
                    .IsInvisible();
            });
        }
    }

    private sealed class EmptyInvisibleContext : DbContext
    {
        public EmptyInvisibleContext(
            DbContextOptions<EmptyInvisibleContext> options
        ) : base(options) { }
    }

    private sealed class InvisibleWithoutColumnContext : DbContext
    {
        public InvisibleWithoutColumnContext(
            DbContextOptions<InvisibleWithoutColumnContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<InvisibleEntity>(entity =>
            {
                entity.ToTable("InvisibleEntities");
                entity.HasKey(value => value.Id);
                entity.Ignore(value => value.InternalData);
            });
        }
    }

    private sealed class VisibleColumnContext : DbContext
    {
        public VisibleColumnContext(
            DbContextOptions<VisibleColumnContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<InvisibleEntity>(entity =>
            {
                entity.ToTable("InvisibleEntities");
                entity.HasKey(value => value.Id);
                entity
                    .Property(value => value.InternalData)
                    .HasDefaultValue(string.Empty)
                    .IsInvisible(false);
            });
        }
    }

    private sealed class JsonFunctionEntity
    {
        public int Id { get; set; }
        public string Data { get; set; } = "{}";
    }

    private sealed record UnsupportedJsonValue(string Value);

    private sealed class JsonFunctionContext : DbContext
    {
        public JsonFunctionContext(
            DbContextOptions<JsonFunctionContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonFunctionEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Data)
                    .HasColumnType("json");
            });
        }
    }
}
