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

    private sealed class ConventionContext : DbContext
    {
        public DbSet<DefaultKeyEntity> DefaultKeyEntities => Set<DefaultKeyEntity>();

        public DbSet<ExplicitKeyEntity> ExplicitKeyEntities => Set<ExplicitKeyEntity>();

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
        }
    }
}
