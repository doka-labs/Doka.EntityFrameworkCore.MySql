namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies generated provider models and migration round trips.
/// </summary>
public sealed partial class MySqlDesignTimeRoundTripTests
{
    /// <summary>
    /// Database character set and collation survive both generated model
    /// surfaces without creating a pending migration.
    /// </summary>
    [Fact]
    public void Database_options_snapshot_and_designer_roundtrip_without_pending_operations()
    {
        // Arrange
        using var context = new DatabaseCollationDesignContext(
            CreateOptions<DatabaseCollationDesignContext>(MySqlServerVersion.MySql(new Version(8, 4, 0))));

        // Act
        var generated = GenerateAndCompile(context);
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);

        // Assert
        Assert.Contains(".UseCollation(\"utf8mb4_unicode_ci\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasCharSet(\"utf8mb4\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".UseCollation(\"utf8mb4_unicode_ci\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".HasCharSet(\"utf8mb4\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Equal("utf8mb4_unicode_ci", generated.SnapshotModel.GetCollation());
        Assert.Equal("utf8mb4", generated.SnapshotModel.GetMySqlCharSet());
        Assert.Equal("utf8mb4_unicode_ci", generated.DesignerModel.GetCollation());
        Assert.Equal("utf8mb4", generated.DesignerModel.GetMySqlCharSet());
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
    }
}
