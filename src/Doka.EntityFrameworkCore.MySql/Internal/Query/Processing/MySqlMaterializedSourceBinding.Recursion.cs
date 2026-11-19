namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMaterializedSourceBinding
{
    /// <summary>Creates finite lexical anchor, frontier and output scopes before ordinary EF navigation expansion.</summary>
    private Expression BindRecursiveSource(
        MethodCallExpression node
    )
    {
        var anchor = Visit(node.Arguments[0]);
        var step = UnwrapLambda(node.Arguments[1]);
        var compose = UnwrapLambda(node.Arguments[2]);
        var maximumDepth = Visit(node.Arguments[3]);
        var bindingId = _nextBindingId++;
        var sourceType = node
            .Method
            .GetGenericArguments()[0];

        var origin = ResolveOrigin(anchor, new Stack<string>());

        if (origin is not null
            && origin.EntityType.ClrType != sourceType)
        {
            origin = RefineEntityType(origin, sourceType);
        }

        Expression frontier = origin is not null
            ? new MySqlMaterializedEntityQueryRootExpression(
                origin,
                bindingId,
                MySqlSourceBindingKind.Recursive,
                isRecursiveFrontier: true)
            : new MySqlMaterializedQueryRootExpression(
                sourceType,
                bindingId,
                MySqlSourceBindingKind.Recursive,
                isRecursiveFrontier: true);

        var stepBody = ReplacingExpressionVisitor.Replace(step.Parameters[0], frontier, step.Body);
        RecursiveStepBindingValidator.Validate(stepBody, bindingId);

        // WHY: Only the finite frontier leaf appears in the step. Embedding the definition in its
        // own source would create an object cycle in EF visitors, cloning and C# quotation.
        _definitions.Add(bindingId, new MySqlRecursiveSourceQueryExpression(anchor, stepBody, maximumDepth, bindingId));
        Expression reference = origin is not null
            ? new MySqlMaterializedEntityQueryRootExpression(origin, bindingId, MySqlSourceBindingKind.Recursive)
            : new MySqlMaterializedQueryRootExpression(sourceType, bindingId, MySqlSourceBindingKind.Recursive);

        return Visit(ReplacingExpressionVisitor.Replace(compose.Parameters[0], reference, compose.Body));
    }

    /// <summary>Rejects unqualified recursive operators before EF can erase their shape while normalizing a UNION.</summary>
    private sealed class RecursiveStepBindingValidator : ExpressionVisitor
    {
        private readonly int _bindingId;

        private int _frontierCount;

        /// <summary>Initializes the immutable dependencies for this operation.</summary>
        public RecursiveStepBindingValidator(
            int bindingId
        )
        {
            _bindingId = bindingId;
        }

        /// <summary>Requires exactly one lexical previous-iteration occurrence and no mutually recursive binder.</summary>
        public static void Validate(
            Expression step,
            int bindingId
        )
        {
            var visitor = new RecursiveStepBindingValidator(bindingId);
            visitor.Visit(step);

            if (visitor._frontierCount != 1)
            {
                throw new InvalidOperationException(
                    "A recursive CTE step must consume exactly one frontier reference.");
            }
        }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(
            MethodCallExpression node
        )
        {
            if (node.Method.DeclaringType == typeof(MySqlQueryableExtensions)
                && node.Method.IsGenericMethod)
            {
                var method = node.Method.GetGenericMethodDefinition();

                if (method == MySqlQueryableExtensions.WithCteMethod
                    || method == MySqlQueryableExtensions.WithRecursiveCteMethod
                    || method == MySqlQueryableExtensions.WithMaterializedSourceMethod)
                {
                    throw new InvalidOperationException(
                        "A recursive step cannot introduce another statement-local source binding.");
                }
            }

            if (node.Method.DeclaringType == typeof(Queryable)
                && node.Method.Name is nameof(Queryable.OrderBy)
                    or nameof(Queryable.OrderByDescending)
                    or nameof(Queryable.ThenBy)
                    or nameof(Queryable.ThenByDescending)
                    or nameof(Queryable.Skip)
                    or nameof(Queryable.Take)
                    or nameof(Queryable.Distinct)
                    or nameof(Queryable.GroupBy)
                    or nameof(Queryable.Concat)
                    or nameof(Queryable.Union)
                    or nameof(Queryable.Intersect)
                    or nameof(Queryable.Except))
            {
                throw new InvalidOperationException(
                    "Recursive CTE steps do not support sorting, paging, grouping, distinct or nested set operations.");
            }

            return base.VisitMethodCall(node);
        }

        /// <inheritdoc />
        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is IMySqlMaterializedQueryRoot reference)
            {
                if (reference.IsRecursiveFrontier
                    && reference.BindingId == _bindingId)
                {
                    _frontierCount++;
                }

                return node;
            }

            return base.VisitExtension(node);
        }
    }
}
