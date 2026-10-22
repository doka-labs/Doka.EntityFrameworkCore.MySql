using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlGuidStoreGenerationTests
{
    /// <summary>Custom mapping and converter factories remain authoritative beside a Guid SQL default.</summary>
    /// <param name="factoryOnMapping">Whether the factory belongs to the mapping rather than converter hints.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sql_default_preserves_custom_guid_text_factory(
        bool factoryOnMapping
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

        var converter = factoryOnMapping
            ? new GuidToStringConverter()
            : new GuidToStringConverter(
                new ConverterMappingHints(valueGeneratorFactory: static (_, _) => new FixedGuidGenerator()));

        if (!factoryOnMapping)
        {
            property.HasConversion(converter);
        }

        // A mapping-owned converter must not make a custom factory look like EF's inferred factory.
        property.Metadata.SetProviderClrType(typeof(string));
        property.Metadata.SetTypeMapping(new FactoryGuidTextTypeMapping(converter, factoryOnMapping));
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

        if (factoryOnMapping)
        {
            Assert.Null(property.Metadata.GetValueConverter());
        }
        else
        {
            Assert.Same(converter, property.Metadata.GetValueConverter());
        }
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

        // Returning the same generator instance proves the custom delegate was consumed.
        property.Metadata.SetProviderClrType(typeof(string));
        property.Metadata.SetTypeMapping(
            new FactoryGuidTextTypeMapping(new GuidToStringConverter(), (_, _) => expectedGenerator));

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
        /// <summary>Creates a mapping whose factory comes from either the mapping or converter hints.</summary>
        public FactoryGuidTextTypeMapping(
            GuidToStringConverter converter,
            bool factoryOnMapping
        ) : base(
            new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(
                    typeof(string),
                    converter: converter,
                    valueGeneratorFactory: factoryOnMapping ? static (_, _) => new FixedGuidGenerator() : null),
                "varchar(36)")) { }

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
