namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlIndexedSelectLowering
{
    /// <summary>Finds the existing grouping without dropping operators on the sequence of complete groups.</summary>
    private static MethodCallExpression? FindGrouping(
        Expression source,
        out IReadOnlyList<MethodCallExpression> sequence
    )
    {
        List<MethodCallExpression>? wrappers = null;

        while (source is MethodCallExpression { Method.IsGenericMethod: true } call)
        {
            var definition = call.Method.GetGenericMethodDefinition();

            if (definition == QueryableMethods.GroupByWithKeySelector)
            {
                wrappers?.Reverse();
                sequence = wrappers is null
                    ? []
                    : wrappers;

                return call;
            }

            if (!IsGroupSequenceOperator(definition))
            {
                break;
            }

            wrappers ??= [];
            wrappers.Add(call);
            source = call.Arguments[0];
        }

        sequence = [];

        return null;
    }

    /// <summary>Accepts only exact element-preserving LINQ operators whose group scope can be replayed unchanged.</summary>
    private static bool IsGroupSequenceOperator(
        MethodInfo definition
    ) => definition == QueryableMethods.OrderBy
        || definition == QueryableMethods.OrderByDescending
        || definition == QueryableMethods.ThenBy
        || definition == QueryableMethods.ThenByDescending
        || definition == QueryableMethods.Where
        || definition == QueryableMethods.Skip
        || definition == QueryableMethods.Take;

    /// <summary>Retains group ordering, filtering and paging after regrouping the original rows with their ordinals.</summary>
    private static Expression ReplayGroupSequence(
        IReadOnlyList<MethodCallExpression> sequence,
        Expression regrouped,
        Type groupType,
        ParameterExpression newGroup,
        Expression projectedGroup,
        FieldInfo? originalKeyMember
    )
    {
        var current = regrouped;

        foreach (var call in sequence)
        {
            var types = call.Method.GetGenericArguments();
            types[0] = groupType;
            var method = CloseGroupedMethod(call.Method.GetGenericMethodDefinition(), types);
            var arguments = call.Arguments.ToArray();
            arguments[0] = current;

            if (arguments[1] is UnaryExpression { NodeType: ExpressionType.Quote })
            {
                var selector = UnwrapLambda(arguments[1]);

                // WHY: A group key is a member of the closed IGrouping interface, so
                // replacing only its parameter would retain incompatible original-row metadata.
                var body = new GroupResultRewriter(selector.Parameters[0],
                    newGroup,
                    projectedGroup,
                    new Dictionary<MethodCallExpression, Expression>(ReferenceEqualityComparer.Instance),
                    originalKeyMember).Visit(selector.Body);

                arguments[1] = Expression.Quote(CreateSelector(method, body, newGroup));
            }

            current = Expression.Call(method, arguments);
        }

        return current;
    }
}
