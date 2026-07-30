namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    protected override void GenerateLimitOffset(
        SelectExpression selectExpression
    )
    {
        ArgumentNullException.ThrowIfNull(selectExpression);

        if (selectExpression.Limit is null
            && selectExpression.Offset is null)
        {
            return;
        }

        Sql.AppendLine();
        Sql.Append("LIMIT ");

        if (selectExpression.Offset is null)
        {
            Visit(selectExpression.Limit);

            return;
        }

        if (selectExpression.Limit is null)
        {
            Sql.Append(OffsetWithoutLimitSentinel);
        }
        else
        {
            Visit(selectExpression.Limit);
        }

        Sql.Append(" OFFSET ");
        Visit(selectExpression.Offset);
    }
}
