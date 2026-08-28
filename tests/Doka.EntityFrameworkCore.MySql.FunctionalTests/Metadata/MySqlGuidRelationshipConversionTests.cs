using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies GUID conversion ownership across branching relationship chains.
/// </summary>
public sealed class MySqlGuidRelationshipConversionTests
{
    /// <summary>
    /// A provider-level Char36 default is one consistent conversion contract even
    /// when the same dependent property participates in multiple relationship chains.
    /// </summary>
    [Fact]
    public void Default_char36_builds_branching_guid_relationship_model()
    {
        using var context = new DefaultChar36RelationshipContext(
            CreateOptions<DefaultChar36RelationshipContext>());

        var properties = GetRelationshipProperties(context.GetService<IDesignTimeModel>().Model);

        Assert.All(properties, property =>
        {
            Assert.Equal(typeof(Guid), Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType);
            Assert.Equal(MySqlGuidFormat.Char36, property.GetMySqlGuidFormat());
            Assert.Equal("char(36)", property.GetColumnType());
            Assert.Null(property.GetValueConverter());
            Assert.Null(property.GetProviderClrType());
            Assert.IsType<GuidToStringConverter>(property.GetRelationalTypeMapping().Converter);
        });
    }

    /// <summary>
    /// An application-owned converter inherited through each relationship branch
    /// remains authoritative over the provider default.
    /// </summary>
    [Fact]
    public void Consistent_application_converters_are_preserved_across_relationship_chains()
    {
        using var context = new ApplicationChar36RelationshipContext(
            CreateOptions<ApplicationChar36RelationshipContext>());

        var properties = GetRelationshipProperties(context.GetService<IDesignTimeModel>().Model);

        Assert.All(properties, property =>
        {
            Assert.Equal(typeof(Guid), Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType);
            Assert.Null(property.GetMySqlGuidFormat());
            var converter = Assert.IsType<GuidToStringConverter>(property.GetValueConverter());
            Assert.Equal(typeof(string), converter.ProviderClrType);
        });
    }

    /// <summary>
    /// An application-owned provider CLR type is inherited without being replaced
    /// by the provider-level default.
    /// </summary>
    [Fact]
    public void Consistent_application_provider_types_are_preserved_across_relationship_chains()
    {
        using var context = new ApplicationProviderTypeRelationshipContext(
            CreateOptions<ApplicationProviderTypeRelationshipContext>());

        var properties = GetRelationshipProperties(context.GetService<IDesignTimeModel>().Model);

        Assert.All(properties, property =>
        {
            Assert.Equal(typeof(Guid), Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType);
            Assert.Null(property.GetMySqlGuidFormat());
            Assert.Null(property.GetValueConverter());
            Assert.Equal(typeof(string), property.GetProviderClrType());
        });
    }

    /// <summary>
    /// A converter and provider CLR type inherited through different branches
    /// remain a model error instead of being hidden by the provider default.
    /// </summary>
    [Fact]
    public void Conflicting_application_converter_and_provider_type_are_rejected()
    {
        using var context = new ConflictingConverterAndProviderTypeRelationshipContext(
            CreateOptions<ConflictingConverterAndProviderTypeRelationshipContext>());

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("conflicting conversions", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(GuidToStringConverter), exception.Message, StringComparison.Ordinal);
        Assert.Contains("string", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(GuidRelationshipZLeaf.ReferenceId), exception.Message, StringComparison.Ordinal);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext => MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>().UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.DefaultGuidFormat(MySqlGuidFormat.Char36))
        .Options;

    private static IReadOnlyList<IReadOnlyProperty> GetRelationshipProperties(
        IModel model
    ) =>
    [
        model.FindEntityType(typeof(GuidRelationshipARoot))!.FindProperty(nameof(GuidRelationshipARoot.Id))!,
        model.FindEntityType(typeof(GuidRelationshipBLeft))!.FindProperty(nameof(GuidRelationshipBLeft.Id))!,
        model.FindEntityType(typeof(GuidRelationshipCRight))!.FindProperty(nameof(GuidRelationshipCRight.Id))!,
        model.FindEntityType(typeof(GuidRelationshipZLeaf))!.FindProperty(nameof(GuidRelationshipZLeaf.ReferenceId))!,
        model
            .FindEntityType(typeof(GuidRelationshipZNullableLeaf))!
            .FindProperty(nameof(GuidRelationshipZNullableLeaf.ReferenceId))!,
    ];

    private abstract class GuidRelationshipContext(DbContextOptions options) : DbContext(options)
    {
        protected abstract void ConfigureRootId(
            PropertyBuilder<Guid> property
        );

        protected abstract void ConfigureRightId(
            PropertyBuilder<Guid> property
        );

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<GuidRelationshipARoot>(entity =>
            {
                entity.HasKey(item => item.Id);
                ConfigureRootId(entity.Property(item => item.Id));
            });

            modelBuilder.Entity<GuidRelationshipBLeft>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity
                    .HasOne(item => item.Root)
                    .WithOne()
                    .HasForeignKey<GuidRelationshipBLeft>(item => item.Id);
            });

            modelBuilder.Entity<GuidRelationshipCRight>(entity =>
            {
                entity.HasKey(item => item.Id);
                ConfigureRightId(entity.Property(item => item.Id));
                entity
                    .HasOne(item => item.Root)
                    .WithOne()
                    .HasForeignKey<GuidRelationshipCRight>(item => item.Id);
            });

            modelBuilder.Entity<GuidRelationshipZLeaf>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity
                    .HasOne(item => item.Left)
                    .WithMany()
                    .HasForeignKey(item => item.ReferenceId);
                entity
                    .HasOne(item => item.Right)
                    .WithMany()
                    .HasForeignKey(item => item.ReferenceId);
            });

            modelBuilder.Entity<GuidRelationshipZNullableLeaf>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity
                    .HasOne(item => item.Left)
                    .WithMany()
                    .HasForeignKey(item => item.ReferenceId);
                entity
                    .HasOne(item => item.Right)
                    .WithMany()
                    .HasForeignKey(item => item.ReferenceId);
            });
        }
    }

    private sealed class DefaultChar36RelationshipContext(DbContextOptions<DefaultChar36RelationshipContext> options)
        : GuidRelationshipContext(options)
    {
        protected override void ConfigureRootId(
            PropertyBuilder<Guid> property
        ) { }

        protected override void ConfigureRightId(
            PropertyBuilder<Guid> property
        ) { }
    }

    private sealed class ApplicationChar36RelationshipContext(
        DbContextOptions<ApplicationChar36RelationshipContext> options
    ) : GuidRelationshipContext(options)
    {
        protected override void ConfigureRootId(
            PropertyBuilder<Guid> property
        ) => property.HasConversion<GuidToStringConverter>();

        protected override void ConfigureRightId(
            PropertyBuilder<Guid> property
        ) { }
    }

    private sealed class ApplicationProviderTypeRelationshipContext(
        DbContextOptions<ApplicationProviderTypeRelationshipContext> options
    ) : GuidRelationshipContext(options)
    {
        protected override void ConfigureRootId(
            PropertyBuilder<Guid> property
        ) => property.HasConversion<string>();

        protected override void ConfigureRightId(
            PropertyBuilder<Guid> property
        ) { }
    }

    private sealed class ConflictingConverterAndProviderTypeRelationshipContext(
        DbContextOptions<ConflictingConverterAndProviderTypeRelationshipContext> options
    )
        : GuidRelationshipContext(options)
    {
        protected override void ConfigureRootId(
            PropertyBuilder<Guid> property
        ) => property.HasConversion<GuidToStringConverter>();

        protected override void ConfigureRightId(
            PropertyBuilder<Guid> property
        ) => property.HasConversion<string>();
    }

    private sealed class GuidRelationshipARoot
    {
        public Guid Id { get; set; }
    }

    private sealed class GuidRelationshipBLeft
    {
        public Guid Id { get; set; }

        public GuidRelationshipARoot Root { get; set; } = null!;
    }

    private sealed class GuidRelationshipCRight
    {
        public Guid Id { get; set; }

        public GuidRelationshipARoot Root { get; set; } = null!;
    }

    private sealed class GuidRelationshipZLeaf
    {
        public int Id { get; set; }

        public Guid ReferenceId { get; set; }

        public GuidRelationshipBLeft Left { get; set; } = null!;

        public GuidRelationshipCRight Right { get; set; } = null!;
    }

    private sealed class GuidRelationshipZNullableLeaf
    {
        public int Id { get; set; }

        public Guid? ReferenceId { get; set; }

        public GuidRelationshipBLeft? Left { get; set; }

        public GuidRelationshipCRight? Right { get; set; }
    }
}
