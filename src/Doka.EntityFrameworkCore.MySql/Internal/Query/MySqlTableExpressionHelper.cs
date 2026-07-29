namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides shared operations for relational table expressions used by query
/// rewriting visitors.
/// </summary>
internal static class MySqlTableExpressionHelper
{
    /// <summary>
    /// Returns the distinct aliases exposed by the supplied table sources.
    /// </summary>
    public static HashSet<string> CollectAliases(
        IReadOnlyList<TableExpressionBase> tables
    )
    {
        var aliases = new HashSet<string>(tables.Count, StringComparer.Ordinal);

        for (var index = 0; index < tables.Count; index++)
        {
            var table = tables[index];

            while (table is JoinExpressionBase join)
            {
                table = join.Table;
            }

            if (table.Alias is not null)
            {
                aliases.Add(table.Alias);
            }
        }

        return aliases;
    }

    /// <summary>
    /// Returns the derived select wrapped by a CROSS or OUTER APPLY expression,
    /// or <see langword="null"/> for any other table shape.
    /// </summary>
    public static SelectExpression? GetApplySelect(
        TableExpressionBase table
    ) => table switch
    {
        CrossApplyExpression { Table: SelectExpression select } => select,
        OuterApplyExpression { Table: SelectExpression select } => select,
        _ => null,
    };
}
