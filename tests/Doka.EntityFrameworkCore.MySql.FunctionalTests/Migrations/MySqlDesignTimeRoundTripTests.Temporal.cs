namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlDesignTimeRoundTripTests
{
    /// <summary>
    /// System-time metadata, including convention-inherited owned mapping, survives
    /// both generated design-time model surfaces without creating a pending migration.
    /// </summary>
    [Fact]
    public void System_time_snapshot_and_designer_roundtrip_without_pending_operations()
    {
        // Arrange
        using var context = new SystemTimeDesignContext(
            CreateOptions<SystemTimeDesignContext>(MySqlServerVersion.MySql(new Version(8, 4, 0))));

        // Act
        var generated = GenerateAndCompile(context);
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);
        var operations = CaptureTableCreation(
            context,
            context.GetService<IDesignTimeModel>().Model,
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        // Assert
        Assert.Contains(".IsTemporal(temporalTableBuilder =>", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(
            ".UseHistoryTable(\"TemporalRecordsHistory\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(".HasPeriodStart(\"ValidFrom\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasColumnName(\"valid_from\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".IsTemporal(temporalTableBuilder =>", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.IsTemporal, generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.IsTemporal, generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
        AssertSinglePhysicalTemporalTableCreation(operations);
    }

    /// <summary>
    /// Application-time and system-time metadata share one generated table closure and
    /// both survive snapshot and designer compilation for a bitemporal MariaDB model.
    /// </summary>
    [Fact]
    public void Bitemporal_snapshot_and_designer_roundtrip_without_pending_operations()
    {
        // Arrange
        using var context = new BitemporalDesignContext(
            CreateOptions<BitemporalDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        // Act
        var generated = GenerateAndCompile(context);
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);
        var operations = CaptureTableCreation(
            context,
            context.GetService<IDesignTimeModel>().Model,
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)));

        // Assert
        Assert.Contains(".IsTemporal(temporalTableBuilder =>", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(
            ".HasApplicationTimePeriod(applicationTimeTableBuilder =>",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(".HasPeriodName(\"BusinessValidity\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".UseWithoutOverlaps()", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(
            ".HasApplicationTimePeriod(applicationTimeTableBuilder =>",
            generated.DesignerCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.IsApplicationTime, generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.IsApplicationTime, generated.DesignerCode, StringComparison.Ordinal);
        AssertExactPropertySet(
            context.GetService<IDesignTimeModel>().Model,
            typeof(BitemporalDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id",
            "RecordedFrom",
            "RecordedTo");
        AssertExactPropertySet(
            generated.SnapshotModel,
            typeof(BitemporalDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id",
            "RecordedFrom",
            "RecordedTo");
        AssertExactPropertySet(
            generated.DesignerModel,
            typeof(BitemporalDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id",
            "RecordedFrom",
            "RecordedTo");
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
        AssertSinglePhysicalBitemporalTableCreation(operations);
    }

    /// <summary>
    /// Typed application-time endpoints remain the complete property set through
    /// both generated design-time surfaces and the initial migration.
    /// </summary>
    [Fact]
    public void Application_time_snapshot_and_designer_roundtrip_without_default_properties()
    {
        // Arrange
        using var context = new ApplicationTimeDesignContext(
            CreateOptions<ApplicationTimeDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        // Act
        var generated = GenerateAndCompile(context);
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);
        var operations = CaptureTableCreation(
            context,
            context.GetService<IDesignTimeModel>().Model,
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)));

        // Assert
        Assert.Contains(
            ".HasApplicationTimePeriod(applicationTimeTableBuilder =>",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(".HasPeriodStart(\"BusinessFrom\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasPeriodEnd(\"BusinessTo\")", generated.SnapshotCode, StringComparison.Ordinal);
        AssertExactPropertySet(
            context.GetService<IDesignTimeModel>().Model,
            typeof(ApplicationTimeDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id");
        AssertExactPropertySet(
            generated.SnapshotModel,
            typeof(ApplicationTimeDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id");
        AssertExactPropertySet(
            generated.DesignerModel,
            typeof(ApplicationTimeDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id");
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
        AssertSinglePhysicalApplicationTimeTableCreation(operations);
    }
}
