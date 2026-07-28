namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Regression coverage for <c>MySqlValueGenerationConvention</c>: integer primary keys must
/// default to <c>AUTO_INCREMENT</c>, but a user-set <c>ValueGeneratedNever()</c> must be
/// respected and produce an integer column without <c>AUTO_INCREMENT</c>. The earlier
/// convention shape unconditionally overrode the user-set facet, breaking every consumer
/// that managed primary keys explicitly (HasData seed, multi-tenant id assignment,
/// imported-data migration). This regression test pins both paths so the override cannot
/// silently come back.
/// </summary>
public sealed class MySqlValueGenerationConventionTests
{
    /// <summary>
    /// Default integer-key entity (no <c>ValueGeneratedNever</c>) is annotated with the
    /// AutoIncrement value-generation strategy and a <c>ValueGenerated.OnAdd</c> facet,
    /// which the migration DDL surface translates into <c>AUTO_INCREMENT</c> on the
    /// emitted column declaration.
    /// </summary>
    [Fact]
    public void Default_integer_primary_key_is_marked_for_AUTO_INCREMENT()
    {
        using var context = new ConventionContext();
        var entityType = context.Model.FindEntityType(typeof(DefaultKeyEntity))!;
        var idProperty = entityType.FindProperty(nameof(DefaultKeyEntity.Id))!;

        Assert.Equal(ValueGenerated.OnAdd, idProperty.ValueGenerated);
        Assert.Equal(MySqlValueGenerationStrategy.AutoIncrement, idProperty.GetMySqlValueGenerationStrategy());
    }

    /// <summary>
    /// Property explicitly opted out of value generation via <c>ValueGeneratedNever()</c>
    /// (or equivalent) must NOT receive AUTO_INCREMENT on its Id column. This is the
    /// production-critical contract for callers that assign their own primary keys
    /// (HasData seeds, multi-tenant Id strategies, imported-data migrations).
    /// </summary>
    [Fact]
    public void ValueGeneratedNever_integer_primary_key_does_not_emit_AUTO_INCREMENT()
    {
        using var context = new ConventionContext();
        var entityType = context.Model.FindEntityType(typeof(ExplicitKeyEntity))!;
        var idProperty = entityType.FindProperty(nameof(ExplicitKeyEntity.Id))!;

        Assert.Equal(ValueGenerated.Never, idProperty.ValueGenerated);
        Assert.Equal(MySqlValueGenerationStrategy.None, idProperty.GetMySqlValueGenerationStrategy());
    }

    /// <summary>
    /// An integer model key converted to text is not an integer store column
    /// and therefore cannot use MySQL AUTO_INCREMENT.
    /// </summary>
    [Fact]
    public void Integer_key_converted_to_text_does_not_emit_AUTO_INCREMENT()
    {
        using var context = new ConventionContext();
        var entityType = context.Model
            .FindEntityType(typeof(ConvertedKeyEntity))!;
        var idProperty = entityType
            .FindProperty(nameof(ConvertedKeyEntity.Id))!;
        var script = context.Database.GenerateCreateScript();

        Assert.Equal("varchar(64)", idProperty.GetColumnType());
        Assert.Null(idProperty.GetMySqlValueGenerationStrategy());
        Assert.DoesNotContain(
            "`Id` varchar(64) NOT NULL AUTO_INCREMENT",
            script,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Store-side integer conversions and integer-backed enums retain native
    /// AUTO_INCREMENT generation.
    /// </summary>
    [Fact]
    public void Integer_store_representations_use_AUTO_INCREMENT()
    {
        using var context = new ConventionContext();
        var convertedProperty = context.Model
            .FindEntityType(typeof(LongToIntKeyEntity))!
            .FindProperty(nameof(LongToIntKeyEntity.Id))!;
        var enumProperty = context.Model
            .FindEntityType(typeof(EnumKeyEntity))!
            .FindProperty(nameof(EnumKeyEntity.Id))!;

        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            convertedProperty.GetMySqlValueGenerationStrategy());
        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            enumProperty.GetMySqlValueGenerationStrategy());
    }

    /// <summary>
    /// Explicit Guid-to-string converters remain authoritative for both sides
    /// of a nullable foreign key.
    /// </summary>
    [Fact]
    public void Guid_to_string_foreign_key_uses_one_compatible_store_type()
    {
        using var context = new ConventionContext();
        var principalProperty = context.Model
            .FindEntityType(typeof(GuidStringPrincipal))!
            .FindProperty(nameof(GuidStringPrincipal.Id))!;
        var dependentProperty = context.Model
            .FindEntityType(typeof(GuidStringDependent))!
            .FindProperty(nameof(GuidStringDependent.PrincipalId))!;

        Assert.Equal("varchar(36)", principalProperty.GetColumnType());
        Assert.Equal(principalProperty.GetColumnType(), dependentProperty.GetColumnType());
        Assert.NotNull(principalProperty.GetTypeMapping().Converter);
        Assert.NotNull(dependentProperty.GetTypeMapping().Converter);
    }

    private sealed class ExplicitKeyEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class DefaultKeyEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class ConvertedKeyEntity
    {
        public int Id { get; set; }
    }

    private sealed class LongToIntKeyEntity
    {
        public long Id { get; set; }
    }

    private sealed class EnumKeyEntity
    {
        public ConventionKey Id { get; set; }
    }

    private sealed class GuidStringPrincipal
    {
        public Guid Id { get; set; }

        public ICollection<GuidStringDependent> Dependents { get; } =
            new List<GuidStringDependent>();
    }

    private sealed class GuidStringDependent
    {
        public int Id { get; set; }

        public Guid? PrincipalId { get; set; }

        public GuidStringPrincipal? Principal { get; set; }
    }

    private enum ConventionKey
    {
        First,
        Second,
    }

    private sealed class ConventionContext : DbContext
    {
        public DbSet<DefaultKeyEntity> DefaultKeyEntities => Set<DefaultKeyEntity>();

        public DbSet<ExplicitKeyEntity> ExplicitKeyEntities => Set<ExplicitKeyEntity>();

        public DbSet<ConvertedKeyEntity> ConvertedKeyEntities => Set<ConvertedKeyEntity>();

        public DbSet<LongToIntKeyEntity> LongToIntKeyEntities => Set<LongToIntKeyEntity>();

        public DbSet<EnumKeyEntity> EnumKeyEntities => Set<EnumKeyEntity>();

        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        ) => optionsBuilder.UseMySql(
            "Server=127.0.0.1;Database=doka_convention_probe;User ID=root;Password=root;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<DefaultKeyEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired();
            });

            modelBuilder.Entity<ExplicitKeyEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.Name).IsRequired();
            });

            modelBuilder.Entity<ConvertedKeyEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasConversion<string>();
            });

            modelBuilder.Entity<LongToIntKeyEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasConversion<int>();
            });

            modelBuilder.Entity<EnumKeyEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
            });

            modelBuilder.Entity<GuidStringPrincipal>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasConversion<string>();
            });

            modelBuilder.Entity<GuidStringDependent>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.PrincipalId).HasConversion<string?>();
                entity.HasOne(e => e.Principal)
                    .WithMany(e => e.Dependents)
                    .HasForeignKey(e => e.PrincipalId);
            });
        }
    }
}
