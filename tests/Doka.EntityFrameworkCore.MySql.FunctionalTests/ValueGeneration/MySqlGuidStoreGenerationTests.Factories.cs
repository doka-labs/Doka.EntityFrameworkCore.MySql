using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlGuidStoreGenerationTests
{
    /// <summary>Explicit property factories remain authoritative beside a Guid SQL default and text converter.</summary>
    /// <param name="converterOnMapping">Whether the converter belongs to the mapping rather than the property.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sql_default_preserves_custom_guid_text_factory(
        bool converterOnMapping
    )
    {
        // Arrange
        using var context = CreateStoreContext("ordinary");
        var modelBuilder = new ModelBuilder();
        var entity = modelBuilder.Entity<StoreRow>();
        var property = entity.Property(row => row.Id)
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("UUID()");

        entity.HasKey(row => row.Id);

        var converter = new GuidToStringConverter();

        if (!converterOnMapping)
        {
            property.HasConversion(converter);
        }

        // EF Core 11 consumes factories from property metadata, not converter mapping hints.
        property.HasValueGenerator<FixedGuidGenerator>();
        property.Metadata.SetProviderClrType(typeof(string));
        property.Metadata.SetTypeMapping(
            new FactoryGuidTextTypeMapping(converter, static (_, _) => new FixedGuidGenerator()));

        var selectedProperty = (IProperty)property.Metadata;

        // Act
        var selected = context.GetService<IValueGeneratorSelector>()
            .TrySelect(selectedProperty, selectedProperty.DeclaringType, out var generator);

        var value = generator?.Next(context.Entry(new StoreRow()));

        // Assert
        Assert.True(selected);

        var fixedGenerator = Assert.IsType<FixedGuidGenerator>(generator);

        Assert.Equal(s_factoryGuid, Assert.IsType<Guid>(value));
        Assert.False(fixedGenerator.GeneratesTemporaryValues);
        Assert.Equal(ValueGenerated.OnAdd, property.Metadata.ValueGenerated);
        Assert.Equal("UUID()", property.Metadata.GetDefaultValueSql());

        if (converterOnMapping)
        {
            Assert.Null(property.Metadata.GetValueConverter());
        }
        else
        {
            Assert.Same(converter, property.Metadata.GetValueConverter());
        }
    }

    /// <summary>A mapping-only factory does not override EF Core 11's SQL-default temporary-key contract.</summary>
    [Fact]
    public void Sql_default_does_not_consume_a_mapping_owned_guid_factory()
    {
        // Arrange
        using var context = CreateStoreContext("ordinary");
        var modelBuilder = new ModelBuilder();
        var entity = modelBuilder.Entity<StoreRow>();
        var property = entity.Property(row => row.Id)
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("UUID()");

        entity.HasKey(row => row.Id);

        var factoryInvoked = false;

        property.Metadata.SetProviderClrType(typeof(string));
        property.Metadata.SetTypeMapping(
            new FactoryGuidTextTypeMapping(
                new GuidToStringConverter(),
                (_, _) =>
                {
                    factoryInvoked = true;

                    return new FixedGuidGenerator();
                }));

        var selectedProperty = (IProperty)property.Metadata;

        // Act
        var selected = context.GetService<IValueGeneratorSelector>()
            .TrySelect(selectedProperty, selectedProperty.DeclaringType, out var generator);

        var value = generator?.Next(context.Entry(new StoreRow()));

        // Assert
        Assert.True(selected);
        Assert.NotNull(generator);
        Assert.True(generator.GeneratesTemporaryValues);
        Assert.NotEqual(Guid.Empty, Assert.IsType<Guid>(value));
        Assert.NotEqual(s_factoryGuid, Assert.IsType<Guid>(value));
        Assert.False(factoryInvoked);
        Assert.Null(property.Metadata.GetValueGeneratorFactory());
        Assert.Equal("UUID()", property.Metadata.GetDefaultValueSql());
    }

    /// <summary>Factory identity stays authoritative even when its generator type matches EF's inferred factory.</summary>
    [Fact]
    public void Sql_default_preserves_custom_factory_returning_sequential_guid_generator()
    {
        // Arrange
        using var context = CreateStoreContext("ordinary");
        var modelBuilder = new ModelBuilder();
        var entity = modelBuilder.Entity<StoreRow>();
        var property = entity.Property(row => row.Id)
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("UUID()");

        entity.HasKey(row => row.Id);

        var expectedGenerator = new SequentialGuidValueGenerator();

        // Identity proves the explicit property delegate wins even beside a mapping-owned factory.
        property.HasValueGenerator((_, _) => expectedGenerator);
        property.Metadata.SetProviderClrType(typeof(string));
        property.Metadata.SetTypeMapping(
            new FactoryGuidTextTypeMapping(new GuidToStringConverter(), static (_, _) => new FixedGuidGenerator()));

        var selectedProperty = (IProperty)property.Metadata;

        // Act
        var selected = context.GetService<IValueGeneratorSelector>()
            .TrySelect(selectedProperty, selectedProperty.DeclaringType, out var generator);

        var value = generator?.Next(context.Entry(new StoreRow()));

        // Assert
        Assert.True(selected);

        var sequentialGenerator = Assert.IsType<SequentialGuidValueGenerator>(generator);

        Assert.Same(expectedGenerator, sequentialGenerator);
        Assert.False(sequentialGenerator.GeneratesTemporaryValues);
        Assert.NotEqual(Guid.Empty, Assert.IsType<Guid>(value));
        Assert.Null(property.Metadata.GetValueConverter());
        Assert.Equal(ValueGenerated.OnAdd, property.Metadata.ValueGenerated);
        Assert.Equal("UUID()", property.Metadata.GetDefaultValueSql());
    }

    /// <summary>Supplies a custom factory without using obsolete factory accessors.</summary>
    private sealed class FactoryGuidTextTypeMapping : RelationalTypeMapping
    {
        /// <summary>Creates a mapping with an independently supplied factory delegate.</summary>
        public FactoryGuidTextTypeMapping(
            GuidToStringConverter converter,
            Func<IProperty, ITypeBase, ValueGenerator> factory
        ) : base(
            new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(typeof(string), converter: converter, valueGeneratorFactory: factory),
                "varchar(36)")) { }

        /// <summary>Preserves the supplied mapping parameters when EF requests a clone.</summary>
        private FactoryGuidTextTypeMapping(
            RelationalTypeMappingParameters parameters
        ) : base(parameters) { }

        /// <inheritdoc />
        protected override RelationalTypeMapping Clone(
            RelationalTypeMappingParameters parameters
        ) => new FactoryGuidTextTypeMapping(parameters);
    }
}
