namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlDesignTimeRoundTripTests
{
    /// <summary>
    /// Provider-owned Guid seed values retain their model CLR type through
    /// snapshots and designer models, including nullable and mixed-format values.
    /// </summary>
    [Fact]
    public void Provider_owned_guid_seed_data_roundtrips_as_model_values()
    {
        // Arrange
        using var context = new GuidSeedDesignContext(
            CreateOptions<GuidSeedDesignContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Char36));

        // Act
        var generated = GenerateAndCompile(context);
        var sourceModel = context.GetService<IDesignTimeModel>().Model;
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);
        var sourceMigration = CaptureInitialMigration(context, sourceModel);
        var snapshotMigration = CaptureInitialMigration(context, generated.SnapshotModel);
        var designerMigration = CaptureInitialMigration(context, generated.DesignerModel);

        // Assert
        Assert.Contains("Id = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("ExplicitChar36 = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("Binary16 = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("OptionalChar36 = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("PrincipalId = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("OptionalPrincipalId = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("Id = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains("ExplicitChar36 = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains("Binary16 = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains("OptionalChar36 = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains("PrincipalId = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains("OptionalPrincipalId = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".HasDefaultValue(new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasDefaultValue(new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(
            ".HasDefaultValue(new Guid(\"00000000-0000-0000-0000-000000000000\"))",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".HasDefaultValue(new Guid(\"00000000-0000-0000-0000-000000000000\"))",
            generated.DesignerCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".HasDefaultValue(\"27caab1e-a588-4dcc-bace-fef7cf47e1fd\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".HasDefaultValue(\"27caab1e-a588-4dcc-bace-fef7cf47e1fd\")",
            generated.DesignerCode,
            StringComparison.Ordinal);
        AssertProviderGuidDefaultModelValues(sourceModel);
        AssertProviderGuidDefaultModelValues(generated.SnapshotModel);
        AssertProviderGuidDefaultModelValues(generated.DesignerModel);
        Assert.DoesNotContain(
            "Id = \"bf1da273-beed-4197-ab57-4cf8395244d4\"",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ExplicitChar36 = \"5153b4e4-0158-4641-b387-433631824557\"",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
        AssertGuidSeedDataOperations(sourceMigration);
        AssertGuidSeedDataOperations(snapshotMigration);
        AssertGuidSeedDataOperations(designerMigration);
    }

    /// <summary>
    /// A model containing only converted Char36 values still imports the model
    /// namespace required by the Guid literals introduced during generation.
    /// </summary>
    [Fact]
    public void Provider_owned_char36_seed_includes_its_model_namespace()
    {
        // Arrange
        using var context = new Char36SeedOnlyDesignContext(
            CreateOptions<Char36SeedOnlyDesignContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Char36));

        // Act
        var generated = GenerateAndCompile(context);
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);

        // Assert
        Assert.Contains("using System;", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("Id = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("using System;", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains("Id = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
    }

    /// <summary>
    /// Provider-owned conversions nested in complex properties contribute their
    /// model namespaces independently of the containing entity's scalar properties.
    /// </summary>
    [Fact]
    public void Provider_owned_complex_property_includes_its_model_namespace()
    {
        // Arrange
        using var context = new Char36ComplexOnlyDesignContext(
            CreateOptions<Char36ComplexOnlyDesignContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Char36));

        // Act
        var generated = GenerateAndCompile(context);
        var sourceModel = context.GetService<IDesignTimeModel>().Model;

        // Assert
        Assert.Contains("using System;", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasDefaultValue(new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<string>(\"ApplicationId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("using System;", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".HasDefaultValue(new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".Property<string>(\"ApplicationId\")", generated.DesignerCode, StringComparison.Ordinal);
        AssertComplexGuidMetadata(sourceModel);
        AssertComplexGuidMetadata(generated.SnapshotModel);
        AssertComplexGuidMetadata(generated.DesignerModel);
    }

    /// <summary>
    /// Application-owned Guid converters retain EF Core's provider-shaped snapshot
    /// contract instead of being rewritten as provider-owned Guid mappings.
    /// </summary>
    [Fact]
    public void Application_owned_guid_seed_converter_retains_provider_values()
    {
        // Arrange
        using var context = new ApplicationGuidSeedDesignContext(
            CreateOptions<ApplicationGuidSeedDesignContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Char36));

        // Act
        var generated = GenerateAndCompile(context);
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);

        // Assert
        Assert.Contains(".Property<string>(\"Id\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(
            "Id = \"89a78261-ea26-494e-a520-b518f51ed3d1\"",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Id = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<string>(\"Id\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(
            "Id = \"89a78261-ea26-494e-a520-b518f51ed3d1\"",
            generated.DesignerCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Id = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(
            ".HasDefaultValue(\"89a78261-ea26-494e-a520-b518f51ed3d1\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".HasDefaultValue(\"89a78261-ea26-494e-a520-b518f51ed3d1\")",
            generated.DesignerCode,
            StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
    }
}
