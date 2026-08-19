namespace Doka.EntityFrameworkCore.MySql;

internal static partial class MariaDbSpatialSridCheckConstraintParser
{
    private const string ColumnGroupName = "column";
    private const string SridGroupName = "srid";

    [GeneratedRegex(
        @"\A\s*(?:st_)?srid\s*\(\s*`(?<column>(?:``|[^`])+)`\s*\)\s*=\s*(?<srid>0|[1-9][0-9]*)\s*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex ProviderCheckPattern();

    public static bool TryParse(
        string sql,
        [NotNullWhen(true)] out string? columnName,
        out int spatialReferenceSystemId
    )
    {
        ArgumentNullException.ThrowIfNull(sql);

        var match = ProviderCheckPattern()
            .Match(sql);

        if (!match.Success
            || !int.TryParse(
                match.Groups[SridGroupName].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out spatialReferenceSystemId))
        {
            columnName = null;
            spatialReferenceSystemId = default;
            return false;
        }

        columnName = match
            .Groups[ColumnGroupName]
            .Value
            .Replace("``", "`", StringComparison.Ordinal);

        return true;
    }
}
