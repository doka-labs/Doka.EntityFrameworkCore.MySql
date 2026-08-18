namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the complete public migration-capability projection for every active
/// LTS target.
/// </summary>
public sealed class MySqlMigrationFeatureSetTests
{
    [Theory]
    [MemberData(nameof(ActiveProfiles))]
    public void Active_lts_profile_exposes_every_feature_exactly_once(
        MySqlServerVersion serverVersion,
        IReadOnlyDictionary<MySqlMigrationFeature, MySqlMigrationFeatureSupport> expected
    )
    {
        var features = new MySqlMigrationFeatureSet(serverVersion.Profile);
        var actual = Enum
            .GetValues<MySqlMigrationFeature>()
            .ToDictionary(feature => feature, features.GetSupport);

        Assert.Equal(
            Enum.GetValues<MySqlMigrationFeature>()
                .Length,
            expected.Count);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Undefined_feature_fails_closed()
    {
        var features = new MySqlMigrationFeatureSet(
            MySqlServerVersion.MySql(new Version(8, 4, 11))
                .Profile);

        Assert.Throws<ArgumentOutOfRangeException>(() => features.GetSupport((MySqlMigrationFeature)int.MaxValue));
    }

    public static TheoryData<
        MySqlServerVersion, IReadOnlyDictionary<MySqlMigrationFeature, MySqlMigrationFeatureSupport>> ActiveProfiles
    {
        get
        {
            var data =
                new
                    TheoryData<MySqlServerVersion,
                        IReadOnlyDictionary<MySqlMigrationFeature, MySqlMigrationFeatureSupport>>
                    {
                        { MySqlServerVersion.MySql(new Version(8, 4, 11)), MySqlExpected() },
                        { MySqlServerVersion.MySql(new Version(9, 7, 2)), MySqlExpected() },
                        { MySqlServerVersion.MariaDb(new Version(10, 11, 18)), MariaDbExpected() },
                        { MySqlServerVersion.MariaDb(new Version(11, 4, 12)), MariaDbExpected() },
                        { MySqlServerVersion.MariaDb(new Version(11, 8, 8)), MariaDbExpected() },
                        { MySqlServerVersion.MariaDb(new Version(12, 3, 2)), MariaDbExpected() },
                    };

            return data;
        }
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
