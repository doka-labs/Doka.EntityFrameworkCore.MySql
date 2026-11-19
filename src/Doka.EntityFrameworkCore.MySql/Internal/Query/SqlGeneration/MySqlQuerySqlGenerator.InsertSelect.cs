namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    /// <summary>Emits the physical INSERT prefix before the existing SELECT-side CTE header.</summary>
    private void GenerateInsertSelect(
        MySqlInsertSelectExpression insert
    )
    {
        if (insert.Columns.Count == 0
            || insert.Columns.Count != insert.Source.Projection.Count)
        {
            throw new InvalidOperationException("INSERT SELECT target columns and finalized source values must match.");
        }

        MySqlInsertSourceValidator.Validate(insert.Source, (ITable)insert.Table.Table);

        foreach (var tag in insert.Tags)
        {
            Sql.AppendLine(Dependencies.SqlGenerationHelper.GenerateComment(tag));
        }

        Sql
            .Append("INSERT INTO ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(insert.Table.Name, insert.Table.Schema));

        Sql.Append(" (");

        // WHY: The physical column order is also the SELECT value order. Values and
        // identifiers never share an interpolation path; only metadata identifiers are quoted.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < insert.Columns.Count; index++)
        {
            if (index > 0)
            {
                Sql.Append(", ");
            }

            Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(insert.Columns[index]));
        }

        Sql.AppendLine(")");

        // WHY: Both native families accept INSERT INTO target (...) WITH ... SELECT.
        // The existing root generator owns lexical names and definition ordering exactly once.
        GenerateRootCommand(insert.Source);
    }
}
