namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Marks only the finite recursive frontier's columns nullable before translating its later-iteration predicates.</summary>
internal sealed class MySqlRecursiveFrontierNullabilityExpressionVisitor : ExpressionVisitor
{
    private readonly string _frontierAlias;

    /// <summary>Initializes the immutable dependencies for this operation.</summary>
    public MySqlRecursiveFrontierNullabilityExpressionVisitor(
        string frontierAlias
    )
    {
        _frontierAlias = frontierAlias;
    }

    /// <inheritdoc />
    protected override Expression VisitExtension(
        Expression node
    ) => node is ColumnExpression column && column.TableAlias == _frontierAlias
        ? column.MakeNullable()
        : base.VisitExtension(node);
}
