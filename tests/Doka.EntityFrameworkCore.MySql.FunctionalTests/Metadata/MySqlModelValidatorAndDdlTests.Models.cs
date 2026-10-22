namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the models cases and shared support.</summary>
public sealed partial class MySqlModelValidatorAndDdlTests
{
    private sealed class DdlTestContext : DbContext
    {
        public DdlTestContext(
            DbContextOptions<DdlTestContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<DdlTestEntity>(e => { e.HasKey(x => x.Id); });
    }

    private sealed class SchemaDdlTestContext : DbContext
    {
        public SchemaDdlTestContext(
            DbContextOptions<SchemaDdlTestContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasDefaultSchema("default_database");

            modelBuilder.Entity<DdlTestEntity>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.ToTable("DdlTestEntities", "table_database");
            });

            modelBuilder.Entity<DdlTestView>(entity =>
            {
                entity.HasNoKey();
                entity.ToView("DdlTestView", "view_database");
            });
        }
    }

    private sealed class FullTextIndexContext : DbContext
    {
        public FullTextIndexContext(
            DbContextOptions<FullTextIndexContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<FullTextIndexEntity>(entity =>
            {
                entity
                    .Property(item => item.Body)
                    .HasColumnType("text");
                entity
                    .HasIndex(item => item.Body)
                    .IsFullText();
            });
        }
    }

    private sealed class OrdinaryTextIndexContext : DbContext
    {
        public OrdinaryTextIndexContext(
            DbContextOptions<OrdinaryTextIndexContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<FullTextIndexEntity>(entity =>
            {
                entity
                    .Property(item => item.Body)
                    .HasColumnType("text");
                entity.HasIndex(item => item.Body);
            });
        }
    }

    private sealed class IndexWidthContext<TScenario> : DbContext
        where TScenario : class
    {
        public IndexWidthContext(
            DbContextOptions<IndexWidthContext<TScenario>> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var scenarioType = typeof(TScenario);
            var charSet = scenarioType switch
            {
                var current when current == typeof(Latin1Scenario)
                    || current == typeof(CollationOverrideScenario) => "latin1",
                var current when current == typeof(Utf8Mb3BoundaryScenario)
                    || current == typeof(Utf8Mb3OverlongScenario) => "utf8mb3",
                _ => "utf8mb4",
            };

            modelBuilder.HasCharSet(charSet);

            modelBuilder.Entity<IndexWidthEntity>(entity =>
            {
                entity.HasKey(item => item.Id);

                if (scenarioType == typeof(BinaryOverlongScenario)
                    || scenarioType == typeof(BinaryBoundaryScenario))
                {
                    entity
                        .Property(item => item.BinaryValue)
                        .HasColumnType(
                            scenarioType == typeof(BinaryBoundaryScenario)
                                ? "varbinary(3072)"
                                : "varbinary(3073)");
                    entity.HasIndex(item => item.BinaryValue);

                    return;
                }

                var length = scenarioType switch
                {
                    _ when scenarioType == typeof(Utf8Mb4BoundaryScenario) => 768,
                    _ when scenarioType == typeof(PrefixBeyondColumnScenario) => 32,
                    _ when scenarioType == typeof(CompositeBoundaryScenario) => 767,
                    _ when scenarioType == typeof(CompositeOverlongScenario) => 768,
                    _ when scenarioType == typeof(Utf8Mb3BoundaryScenario) => 1024,
                    _ when scenarioType == typeof(Utf8Mb3OverlongScenario) => 1025,
                    _ => 800,
                };

                var property = entity
                    .Property(item => item.Value)
                    .HasColumnType($"varchar({length})");

                if (scenarioType == typeof(CollationOverrideScenario))
                {
                    property.UseCollation("utf8mb4_bin");
                }

                if (scenarioType == typeof(PrimaryKeyOverlongScenario))
                {
                    entity.HasKey(item => item.Value);

                    return;
                }

                if (scenarioType == typeof(AlternateKeyOverlongScenario))
                {
                    entity.HasAlternateKey(item => item.Value);

                    return;
                }

                var index = scenarioType == typeof(CompositeBoundaryScenario)
                    || scenarioType == typeof(CompositeOverlongScenario)
                        ? entity.HasIndex(item => new { item.Value, item.Id })
                        : entity.HasIndex(item => item.Value);

                if (scenarioType == typeof(UniqueOverlongScenario))
                {
                    index.IsUnique();
                }
                else if (scenarioType == typeof(Utf8Mb4PrefixBoundaryScenario))
                {
                    index.HasPrefixLength(768);
                }
                else if (scenarioType == typeof(Utf8Mb4PrefixOverlongScenario))
                {
                    index.HasPrefixLength(769);
                }
                else if (scenarioType == typeof(PrefixBeyondColumnScenario))
                {
                    index.HasPrefixLength(33);
                }
            });
        }
    }

    private sealed class Utf8Mb4OverlongScenario;

    private sealed class Utf8Mb4BoundaryScenario;

    private sealed class Utf8Mb4PrefixBoundaryScenario;

    private sealed class Utf8Mb4PrefixOverlongScenario;

    private sealed class PrefixBeyondColumnScenario;

    private sealed class CompositeBoundaryScenario;

    private sealed class CompositeOverlongScenario;

    private sealed class Latin1Scenario;

    private sealed class Utf8Mb3BoundaryScenario;

    private sealed class Utf8Mb3OverlongScenario;

    private sealed class CollationOverrideScenario;

    private sealed class BinaryBoundaryScenario;

    private sealed class BinaryOverlongScenario;

    private sealed class UniqueOverlongScenario;

    private sealed class AlternateKeyOverlongScenario;

    private sealed class PrimaryKeyOverlongScenario;

    private sealed class ForeignKeyIndexWidthContext : DbContext
    {
        public ForeignKeyIndexWidthContext(
            DbContextOptions<ForeignKeyIndexWidthContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("utf8mb4");

            modelBuilder.Entity<ForeignKeyIndexWidthPrincipal>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.NaturalKey)
                    .HasColumnType("varchar(768)");
                entity.HasAlternateKey(item => item.NaturalKey);
            });

            modelBuilder.Entity<ForeignKeyIndexWidthDependent>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.PrincipalNaturalKey)
                    .HasColumnType("varchar(768)");
                entity
                    .HasOne<ForeignKeyIndexWidthPrincipal>()
                    .WithMany()
                    .HasForeignKey(item => item.PrincipalNaturalKey)
                    .HasPrincipalKey(item => item.NaturalKey);
            });
        }
    }

    private sealed class ForeignKeyIndexWidthPrincipal
    {
        public int Id { get; set; }

        public string NaturalKey { get; set; } = string.Empty;
    }

    private sealed class ForeignKeyIndexWidthDependent
    {
        public int Id { get; set; }

        public string PrincipalNaturalKey { get; set; } = string.Empty;
    }

    private sealed class IndexWidthEntity
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;

        public byte[] BinaryValue { get; set; } = [];
    }

    private sealed class FullTextIndexEntity
    {
        public int Id { get; set; }

        public string Body { get; set; } = string.Empty;
    }

    private sealed class DdlTestEntity
    {
        public int Id { get; set; }
    }

    private sealed class DdlTestView
    {
        public int Id { get; set; }
    }
}
