using System.Text;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the infrastructure responsibility for MySqlMigrationDdlCoverageTests.</summary>
public sealed partial class MySqlMigrationDdlCoverageTests
{
    private static AlterColumnOperation CreateNullableTimestampRepair() => new()
    {
        Table = "Entries",
        Name = "OccurredAt",
        ClrType = typeof(DateTime),
        ColumnType = "timestamp(6)",
        IsNullable = false,
        OldColumn =
        {
            ClrType = typeof(DateTime),
            ColumnType = "timestamp(6)",
            IsNullable = true,
        },
    };

    // -- Helpers --

    private static string JoinSql(
        IReadOnlyList<MigrationCommand> commands
    ) => string.Join("\n", commands.Select(c => c.CommandText));

    private static string GenerateExpectedHexLiteral(
        string value
    ) => $"_utf8mb4 X'{Convert.ToHexString(Encoding.UTF8.GetBytes(value))}'";

    private static string GenerateExpectedDdlComment(
        string value
    ) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static void ConfigureInvalidIndex(
        CreateIndexOperation operation,
        string scenario
    )
    {
        switch (scenario)
        {
            case "prefix-count":
                operation.SetAnnotation(MySqlAnnotationNames.IndexPrefixLength, s_singlePrefixLength);
                break;
            case "negative-prefix":
                operation.SetAnnotation(MySqlAnnotationNames.IndexPrefixLength, s_invalidPrefixLengths);
                break;
            case "unique-full-text":
                operation.IsUnique = true;
                operation.SetAnnotation(MySqlAnnotationNames.FullTextIndex, true);
                break;
            case "prefixed-full-text":
                operation.SetAnnotation(MySqlAnnotationNames.FullTextIndex, true);
                operation.SetAnnotation(MySqlAnnotationNames.IndexPrefixLength, s_fullTextPrefixLengths);
                break;
            case "spatial-full-text":
                operation.Columns = ["Location"];
                operation.SetAnnotation(MySqlAnnotationNames.SpatialIndex, true);
                operation.SetAnnotation(MySqlAnnotationNames.FullTextIndex, true);
                break;
            case "prefixed-spatial":
                operation.Columns = ["Location"];
                operation.SetAnnotation(MySqlAnnotationNames.SpatialIndex, true);
                operation.SetAnnotation(MySqlAnnotationNames.IndexPrefixLength, s_singlePrefixLength);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown test scenario.");
        }
    }

    private static DdlCoverageContext CreateMySqlContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<DdlCoverageContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return new DdlCoverageContext(builder.Options);
    }

    private static DdlCoverageContext CreateMariaDbContext(
        Version? version = null
    )
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<DdlCoverageContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MariaDb(version ?? new Version(11, 8, 0)));

        return new DdlCoverageContext(builder.Options);
    }

    private static JsonFuncContext CreateJsonContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<JsonFuncContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return new JsonFuncContext(builder.Options);
    }

    private static TemporalContext CreateTemporalContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TemporalContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return new TemporalContext(builder.Options);
    }

    private static TimestampMappingContext CreateTimestampMappingContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TimestampMappingContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return new TimestampMappingContext(builder.Options);
    }
}
