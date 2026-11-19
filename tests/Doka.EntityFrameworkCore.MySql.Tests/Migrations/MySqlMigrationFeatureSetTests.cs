namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the complete public migration-capability projection for every active
/// LTS target.
/// </summary>
public sealed class MySqlMigrationFeatureSetTests
{
    /// <summary>Identifies each native profile with serializable family and version values.</summary>
    public static TheoryData<bool, string> ActiveProfiles
    {
        get
        {
            // WHY: Rebuild provider objects in Arrange so xUnit 2 can serialize every discovered row.
            var data = new TheoryData<bool, string>
            {
                { false, "8.4.11" },
                { false, "9.7.2" },
                { true, "10.11.18" },
                { true, "11.4.12" },
                { true, "11.8.8" },
                { true, "12.3.2" },
            };

            return data;
        }
    }

    /// <summary>
    /// Verifies active lts profile exposes every feature exactly once.
    /// </summary>
    /// <param name="isMariaDb">Whether the profile belongs to the MariaDB engine family.</param>
    /// <param name="version">The server version reconstructed for this profile.</param>
    [Theory]
    [MemberData(nameof(ActiveProfiles))]
    public void Active_lts_profile_exposes_every_feature_exactly_once(
        bool isMariaDb,
        string version
    )
    {
        // Arrange
        var parsedVersion = Version.Parse(version);
        var serverVersion = isMariaDb
            ? MySqlServerVersion.MariaDb(parsedVersion)
            : MySqlServerVersion.MySql(parsedVersion);

        var expected = isMariaDb
            ? MariaDbExpected()
            : MySqlExpected();

        var features = new MySqlMigrationFeatureSet(serverVersion.Profile);

        // Act
        var actual = Enum
            .GetValues<MySqlMigrationFeature>()
            .ToDictionary(feature => feature, features.GetSupport);

        // Assert
        Assert.Equal(
            Enum.GetValues<MySqlMigrationFeature>()
                .Length,
            expected.Count);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies undefined feature fails closed.
    /// </summary>
    [Fact]
    public void Undefined_feature_fails_closed()
    {
        // Arrange
        var features = new MySqlMigrationFeatureSet(
            MySqlServerVersion.MySql(new Version(8, 4, 11))
                .Profile);

        Action act = () => features.GetSupport((MySqlMigrationFeature)int.MaxValue);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(recordedException);
    }

    private static Dictionary<MySqlMigrationFeature, MySqlMigrationFeatureSupport> MySqlExpected() => Expected(
        MySqlMigrationFeatureSupport.Native,
        MySqlMigrationFeatureSupport.Emulated,
        MySqlMigrationFeatureSupport.Unsupported);

    private static Dictionary<MySqlMigrationFeature, MySqlMigrationFeatureSupport> MariaDbExpected() => new()
    {
        [MySqlMigrationFeature.SchemaOperations] = MySqlMigrationFeatureSupport.Unsupported,
        [MySqlMigrationFeature.JsonColumns] = MySqlMigrationFeatureSupport.Emulated,
        [MySqlMigrationFeature.CheckConstraints] = MySqlMigrationFeatureSupport.Native,
        [MySqlMigrationFeature.DescendingIndexes] = MySqlMigrationFeatureSupport.Native,
        [MySqlMigrationFeature.FilteredIndexes] = MySqlMigrationFeatureSupport.Unsupported,
        [MySqlMigrationFeature.FunctionalIndexes] = MySqlMigrationFeatureSupport.Unsupported,
        [MySqlMigrationFeature.IndexPrefixLengths] = MySqlMigrationFeatureSupport.Native,
        [MySqlMigrationFeature.RenameColumn] = MySqlMigrationFeatureSupport.Native,
        [MySqlMigrationFeature.RenameIndex] = MySqlMigrationFeatureSupport.Native,
        [MySqlMigrationFeature.GeneratedColumnNullabilityClause] = MySqlMigrationFeatureSupport.Unsupported,
        [MySqlMigrationFeature.VirtualGeneratedColumns] = MySqlMigrationFeatureSupport.Native,
        [MySqlMigrationFeature.StoredGeneratedColumns] = MySqlMigrationFeatureSupport.Native,
        [MySqlMigrationFeature.SpatialColumnSridAttribute] = MySqlMigrationFeatureSupport.Emulated,
        [MySqlMigrationFeature.ExpressionDefaults] = MySqlMigrationFeatureSupport.Native,
        [MySqlMigrationFeature.TemporalTables] = MySqlMigrationFeatureSupport.Native,
        [MySqlMigrationFeature.ApplicationTimePeriods] = MySqlMigrationFeatureSupport.Native,
        [MySqlMigrationFeature.BitemporalTables] = MySqlMigrationFeatureSupport.Native,
        [MySqlMigrationFeature.Sequences] = MySqlMigrationFeatureSupport.Native,
        [MySqlMigrationFeature.PreparedDdl] = MySqlMigrationFeatureSupport.Native,
        [MySqlMigrationFeature.AtomicDdl] = MySqlMigrationFeatureSupport.Native,
        [MySqlMigrationFeature.TransactionalDdl] = MySqlMigrationFeatureSupport.Unsupported,
    };

    private static Dictionary<MySqlMigrationFeature, MySqlMigrationFeatureSupport> Expected(
        MySqlMigrationFeatureSupport native,
        MySqlMigrationFeatureSupport emulated,
        MySqlMigrationFeatureSupport unsupported
    ) => new()
    {
        [MySqlMigrationFeature.SchemaOperations] = unsupported,
        [MySqlMigrationFeature.JsonColumns] = native,
        [MySqlMigrationFeature.CheckConstraints] = native,
        [MySqlMigrationFeature.DescendingIndexes] = native,
        [MySqlMigrationFeature.FilteredIndexes] = unsupported,
        [MySqlMigrationFeature.FunctionalIndexes] = native,
        [MySqlMigrationFeature.IndexPrefixLengths] = native,
        [MySqlMigrationFeature.RenameColumn] = native,
        [MySqlMigrationFeature.RenameIndex] = native,
        [MySqlMigrationFeature.GeneratedColumnNullabilityClause] = native,
        [MySqlMigrationFeature.VirtualGeneratedColumns] = native,
        [MySqlMigrationFeature.StoredGeneratedColumns] = native,
        [MySqlMigrationFeature.SpatialColumnSridAttribute] = native,
        [MySqlMigrationFeature.ExpressionDefaults] = native,
        [MySqlMigrationFeature.TemporalTables] = emulated,
        [MySqlMigrationFeature.ApplicationTimePeriods] = unsupported,
        [MySqlMigrationFeature.BitemporalTables] = unsupported,
        [MySqlMigrationFeature.Sequences] = emulated,
        [MySqlMigrationFeature.PreparedDdl] = native,
        [MySqlMigrationFeature.AtomicDdl] = native,
        [MySqlMigrationFeature.TransactionalDdl] = unsupported,
    };
}
