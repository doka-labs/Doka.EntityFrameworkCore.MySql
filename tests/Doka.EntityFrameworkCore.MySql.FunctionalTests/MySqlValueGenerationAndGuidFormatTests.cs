namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the value-generation and GUID-format baseline.
/// </summary>
public sealed class MySqlValueGenerationAndGuidFormatTests
{
    /// <summary>
    /// Verifies that integer keys default to auto-increment while GUID keys do not generate implicitly.
    /// </summary>
    [Fact]
    public void Model_defaults_integer_keys_to_autoincrement_and_guid_keys_to_none()
    {
        using var context = new ValueGenerationContext(CreateOptions());
        var intKeyProperty = context.Model.FindEntityType(typeof(IntKeyEntity))!.FindProperty(nameof(IntKeyEntity.Id))!;
        var guidKeyProperty =
            context.Model.FindEntityType(typeof(GuidKeyEntity))!.FindProperty(nameof(GuidKeyEntity.Id))!;

        Assert.Equal(MySqlValueGenerationStrategy.AutoIncrement, intKeyProperty.GetMySqlValueGenerationStrategy());
        Assert.Equal(ValueGenerated.OnAdd, intKeyProperty.ValueGenerated);
        Assert.Equal(MySqlValueGenerationStrategy.None, guidKeyProperty.GetMySqlValueGenerationStrategy());
        Assert.Equal(ValueGenerated.Never, guidKeyProperty.ValueGenerated);
    }

    /// <summary>
    /// Verifies that the configured default GUID format flows into the model store type.
    /// </summary>
    [Fact]
    public void Default_guid_format_char36_applies_char36_store_type()
    {
        using var context = new ValueGenerationContext(CreateOptions(MySqlGuidFormat.Char36));
        var guidKeyProperty =
            context.Model.FindEntityType(typeof(GuidKeyEntity))!.FindProperty(nameof(GuidKeyEntity.Id))!;

        Assert.Equal(MySqlGuidFormat.Char36, guidKeyProperty.GetMySqlGuidFormat());
        Assert.Equal("char(36)", guidKeyProperty.GetColumnType());
        Assert.Equal(36, guidKeyProperty.GetMaxLength());
        Assert.True(guidKeyProperty.IsFixedLength());
    }

    [Fact]
    public void Explicit_client_guid_generation_assigns_values_when_entities_are_tracked()
    {
        using var context = new ValueGenerationContext(CreateOptions());
        var generatedEntity = new GeneratedGuidEntity();
        var nonGeneratedEntity = new GuidKeyEntity();

        context.Add(generatedEntity);
        context.Add(nonGeneratedEntity);

        Assert.NotEqual(Guid.Empty, generatedEntity.Id);
        Assert.Equal(Guid.Empty, nonGeneratedEntity.Id);
    }

    /// <summary>
    /// Verifies that explicit property-level CHAR(36) compatibility configuration remains available.
    /// </summary>
    [Fact]
    public void Explicit_property_level_char36_configuration_remains_available()
    {
        using var context = new ValueGenerationContext(CreateOptions());
        var property =
            context.Model.FindEntityType(typeof(ExplicitChar36Entity))!.FindProperty(nameof(ExplicitChar36Entity.Id))!;

        Assert.Equal(MySqlGuidFormat.Char36, property.GetMySqlGuidFormat());
        Assert.Equal("char(36)", property.GetColumnType());
        Assert.Equal(MySqlValueGenerationStrategy.ClientGuid, property.GetMySqlValueGenerationStrategy());
        Assert.Equal(ValueGenerated.OnAdd, property.ValueGenerated);
    }

    /// <summary>
    /// Verifies that unsupported GUID-format values fail with a format-specific diagnostic.
    /// </summary>
    [Fact]
    public void Invalid_guid_format_throws_with_the_format_parameter_name()
    {
        using var context = new InvalidGuidFormatContext(CreateOptions<InvalidGuidFormatContext>());

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => _ = context.Model);

        Assert.Equal("format", exception.ParamName);
        Assert.Contains("Unsupported MySqlGuidFormat value", exception.Message, StringComparison.Ordinal);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlGuidFormat defaultGuidFormat = MySqlGuidFormat.Binary16
    )
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.DefaultGuidFormat(defaultGuidFormat));

        return builder.Options;
    }

    private static DbContextOptions<ValueGenerationContext> CreateOptions(
        MySqlGuidFormat defaultGuidFormat = MySqlGuidFormat.Binary16
    )
    {
        return CreateOptions<ValueGenerationContext>(defaultGuidFormat);
    }

    private sealed class ValueGenerationContext : DbContext
    {
        public ValueGenerationContext(
            DbContextOptions<ValueGenerationContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<IntKeyEntity>(entity =>
            {
                entity.ToTable("Phase2IntKeyEntities");
                entity.HasKey(item => item.Id);
            });

            modelBuilder.Entity<GuidKeyEntity>(entity =>
            {
                entity.ToTable("Phase2GuidKeyEntities");
                entity.HasKey(item => item.Id);
            });

            modelBuilder.Entity<GeneratedGuidEntity>(entity =>
            {
                entity.ToTable("Phase2GeneratedGuidEntities");
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .UseMySqlClientGuidValueGeneration();
            });

            modelBuilder.Entity<ExplicitChar36Entity>(entity =>
            {
                entity.ToTable("Phase2ExplicitChar36Entities");
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36)
                    .UseMySqlClientGuidValueGeneration();
            });
        }
    }

    private sealed class InvalidGuidFormatContext : DbContext
    {
        public InvalidGuidFormatContext(
            DbContextOptions<InvalidGuidFormatContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<InvalidGuidFormatEntity>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .Metadata.SetMySqlGuidFormat((MySqlGuidFormat)999);
            });
        }
    }

    private sealed class IntKeyEntity
    {
        public int Id { get; set; }
    }

    private sealed class GuidKeyEntity
    {
        public Guid Id { get; set; }
    }

    private sealed class GeneratedGuidEntity
    {
        public Guid Id { get; set; }
    }

    private sealed class ExplicitChar36Entity
    {
        public Guid Id { get; set; }
    }

    private sealed class InvalidGuidFormatEntity
    {
        public Guid Id { get; set; }
    }
}
