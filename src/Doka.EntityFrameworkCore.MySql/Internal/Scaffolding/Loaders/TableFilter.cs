namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Per-scaffolding-call set of table names to include. <see cref="MatchAll"/> matches
/// every table in the active database (filter inactive); <see cref="For"/> with a
/// non-empty list applies a server-side <c>WHERE TABLE_NAME IN (...)</c> SQL filter
/// plus a client-side belt-and-suspenders check inside every loader. Empty input
/// collapses to <see cref="MatchAll"/> so the caller does not have to special-case it.
/// </summary>
internal readonly record struct TableFilter(HashSet<string>? Tables)
{
    public static TableFilter MatchAll => new(null);

    public static TableFilter For(
        IEnumerable<string> tables
    )
    {
        ArgumentNullException.ThrowIfNull(tables);

        var set = new HashSet<string>(tables, StringComparer.Ordinal);

        return set.Count == 0 ? MatchAll : new TableFilter(set);
    }

    public bool Matches(
        string tableName
    ) => Tables is null || Tables.Contains(tableName);
}
