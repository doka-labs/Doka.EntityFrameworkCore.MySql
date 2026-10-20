namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    /// <inheritdoc />
    protected override Expression VisitInnerJoin(
        InnerJoinExpression innerJoinExpression
    )
    {
        if (innerJoinExpression is not MySqlStraightJoinExpression)
        {
            return base.VisitInnerJoin(innerJoinExpression);
        }

        Sql.Append("STRAIGHT_JOIN ");
        Visit(innerJoinExpression.Table);
        Sql.Append(" ON ");
        Visit(innerJoinExpression.JoinPredicate);

        return innerJoinExpression;
    }
}
