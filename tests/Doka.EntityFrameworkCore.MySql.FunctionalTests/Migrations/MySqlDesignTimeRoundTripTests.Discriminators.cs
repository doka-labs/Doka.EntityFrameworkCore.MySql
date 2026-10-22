namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlDesignTimeRoundTripTests
{
    /// <summary>
    /// Incomplete discriminator metadata survives both generated design-time model
    /// surfaces while the default complete mapping remains implicit.
    /// </summary>
    [Fact]
    public void Discriminator_completeness_snapshot_and_designer_preserve_explicit_and_default_values()
    {
        // Arrange
        using var context = new DiscriminatorCompletenessDesignContext(
            CreateOptions<DiscriminatorCompletenessDesignContext>(
                MySqlServerVersion.MySql(new Version(8, 4, 0))));

        // Act
        var generated = GenerateAndCompile(context);
        var sourceModel = context.GetService<IDesignTimeModel>().Model;

        // Assert
        Assert.Contains(".IsComplete(false)", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".IsComplete(false)", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".IsComplete(true)", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".IsComplete(true)", generated.DesignerCode, StringComparison.Ordinal);
        AssertDiscriminatorCompleteness(sourceModel);
        AssertDiscriminatorCompleteness(generated.SnapshotModel);
        AssertDiscriminatorCompleteness(generated.DesignerModel);
        AssertConvertedDiscriminatorSnapshot(generated.SnapshotModel);
        AssertConvertedDiscriminatorSnapshot(generated.DesignerModel);
    }

    /// <summary>
    /// Model differentiation leaves application converter discriminator values in their model types.
    /// </summary>
    [Fact]
    public void Model_differ_preserves_converted_discriminator_model_values()
    {
        // Arrange
        using var context = new DiscriminatorCompletenessDesignContext(
            CreateOptions<DiscriminatorCompletenessDesignContext>(
                MySqlServerVersion.MySql(new Version(8, 4, 0))));

        var sourceModel = context.GetService<IDesignTimeModel>().Model;

        // Act
        _ = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, sourceModel.GetRelationalModel());

        // Assert
        AssertConvertedDiscriminatorModel(sourceModel);
    }

    /// <summary>
    /// Native integer and enum discriminators using EF's default property name retain
    /// their provider type and values in both generated design-time model surfaces.
    /// </summary>
    [Fact]
    public void Non_string_discriminator_snapshot_and_designer_preserve_type_and_values()
    {
        // Arrange
        using var context = new DiscriminatorCompletenessDesignContext(
            CreateOptions<DiscriminatorCompletenessDesignContext>(
                MySqlServerVersion.MySql(new Version(8, 4, 0))));

        // Act
        var generated = GenerateAndCompile(context);

        // Assert
        Assert.Equal(
            2,
            generated.SnapshotCode.Split(
                ".HasDiscriminator<int>(\"Discriminator\")",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            2,
            generated.DesignerCode.Split(
                ".HasDiscriminator<int>(\"Discriminator\")",
                StringSplitOptions.None).Length - 1);
        AssertNonStringDiscriminatorSnapshot(generated.SnapshotModel);
        AssertNonStringDiscriminatorSnapshot(generated.DesignerModel);
    }

    private static void AssertDiscriminatorCompleteness(
        IModel model
    )
    {
        var incomplete = model.FindEntityType(typeof(IncompleteDiscriminatorDesignRecord));
        var complete = model.FindEntityType(typeof(CompleteDiscriminatorDesignRecord));
        var converted = model.FindEntityType(typeof(ConvertedDiscriminatorDesignRecord));
        var childConfiguredConverted = model.FindEntityType(
            typeof(ChildConfiguredConvertedDiscriminatorDesignRecord));

        Assert.NotNull(incomplete);
        Assert.NotNull(complete);
        Assert.NotNull(converted);
        Assert.NotNull(childConfiguredConverted);
        Assert.False(incomplete.GetIsDiscriminatorMappingComplete());
        Assert.True(complete.GetIsDiscriminatorMappingComplete());
        Assert.False(converted.GetIsDiscriminatorMappingComplete());
        Assert.False(childConfiguredConverted.GetIsDiscriminatorMappingComplete());
    }

    private static void AssertConvertedDiscriminatorSnapshot(
        IModel model
    )
    {
        var root = model.FindEntityType(typeof(ConvertedDiscriminatorDesignRecord));
        var known = model.FindEntityType(typeof(KnownConvertedDiscriminatorDesignRecord));
        var childConfiguredRoot = model.FindEntityType(typeof(ChildConfiguredConvertedDiscriminatorDesignRecord));
        var childConfiguredKnown = model.FindEntityType(
            typeof(KnownChildConfiguredConvertedDiscriminatorDesignRecord));

        Assert.NotNull(root);
        Assert.NotNull(known);
        Assert.NotNull(childConfiguredRoot);
        Assert.NotNull(childConfiguredKnown);
        Assert.Equal(typeof(string), root.FindDiscriminatorProperty()!.ClrType);
        Assert.Equal("K", known.GetDiscriminatorValue());
        Assert.Equal(typeof(string), childConfiguredRoot.FindDiscriminatorProperty()!.ClrType);
        Assert.Equal("K", childConfiguredKnown.GetDiscriminatorValue());
    }

    private static void AssertConvertedDiscriminatorModel(
        IModel model
    )
    {
        var root = model.FindEntityType(typeof(ConvertedDiscriminatorDesignRecord));
        var known = model.FindEntityType(typeof(KnownConvertedDiscriminatorDesignRecord));
        var childConfiguredRoot = model.FindEntityType(typeof(ChildConfiguredConvertedDiscriminatorDesignRecord));
        var childConfiguredKnown = model.FindEntityType(
            typeof(KnownChildConfiguredConvertedDiscriminatorDesignRecord));

        Assert.NotNull(root);
        Assert.NotNull(known);
        Assert.NotNull(childConfiguredRoot);
        Assert.NotNull(childConfiguredKnown);
        Assert.Equal(typeof(DesignDiscriminator), root.FindDiscriminatorProperty()!.ClrType);
        Assert.Equal(DesignDiscriminator.Known, known.GetDiscriminatorValue());
        Assert.Equal(typeof(DesignDiscriminator), childConfiguredRoot.FindDiscriminatorProperty()!.ClrType);
        Assert.Equal(DesignDiscriminator.Known, childConfiguredKnown.GetDiscriminatorValue());
    }

    private static void AssertNonStringDiscriminatorSnapshot(
        IModel model
    )
    {
        var intRoot = model.FindEntityType(typeof(IntDiscriminatorDesignRecord));
        var knownInt = model.FindEntityType(typeof(KnownIntDiscriminatorDesignRecord));
        var enumRoot = model.FindEntityType(typeof(EnumDiscriminatorDesignRecord));
        var knownEnum = model.FindEntityType(typeof(KnownEnumDiscriminatorDesignRecord));

        Assert.NotNull(intRoot);
        Assert.NotNull(knownInt);
        Assert.NotNull(enumRoot);
        Assert.NotNull(knownEnum);
        Assert.Equal(typeof(int), intRoot.FindDiscriminatorProperty()!.ClrType);
        Assert.Equal(7, knownInt.GetDiscriminatorValue());
        Assert.Equal(typeof(int), enumRoot.FindDiscriminatorProperty()!.ClrType);
        Assert.Equal(7, knownEnum.GetDiscriminatorValue());
    }
}
