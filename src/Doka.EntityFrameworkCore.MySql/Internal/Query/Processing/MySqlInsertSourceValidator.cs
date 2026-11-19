namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Preserves opt-in source boundaries and native INSERT-specific self-reference limits.</summary>
internal sealed class MySqlInsertSourceValidator : ExpressionVisitor
{
    private readonly ITable _target;

    private int _selectDepth;

    /// <summary>Initializes the immutable dependencies for this operation.</summary>
    public MySqlInsertSourceValidator(
        ITable target
    )
    {
        _target = target;
    }

    /// <summary>Allows a direct same-table SELECT but rejects a target hidden inside a nested source.</summary>
    internal static void Validate(
        SelectExpression source,
        ITable target
    ) => new MySqlInsertSourceValidator(target).Visit(source);

    /// <inheritdoc />
    protected override Expression VisitExtension(
        Expression node
    )
    {
        if (node is MySqlMaterializedSourceExpression
            {
                Kind: not (MySqlSourceBindingKind.Ordinary or MySqlSourceBindingKind.Recursive),
            })
        {
            // WHY: Native ordinary relations and the independently guarded recursive
            // INSERT envelope are qualified sources. A once-bound sharing fence remains
            // read-only; accepting recursive reads must never remove that boundary.
            throw new InvalidOperationException("INSERT SELECT cannot consume a read-only materialized source.");
        }

        if (node is SelectExpression select)
        {
            if (MySqlLockingReadMetadata.GetMode(select) != MySqlLockingReadMode.None)
            {
                throw new InvalidOperationException("INSERT SELECT cannot consume a locking-read source.");
            }

            _selectDepth++;

            try
            {
                return base.VisitExtension(node);
            }
            finally
            {
                _selectDepth--;
            }
        }

        if (_selectDepth > 1
            && node is TableExpression table
            && table.Name == _target.Name
            && table.Schema == _target.Schema)
        {
            throw new InvalidOperationException(
                "INSERT SELECT cannot read its target through an unqualified nested self-subquery.");
        }

        return base.VisitExtension(node);
    }
}
