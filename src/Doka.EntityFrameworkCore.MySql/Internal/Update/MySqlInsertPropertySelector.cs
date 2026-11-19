namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Resolves typed, shadow and field-only target selectors through the actual EF metadata.</summary>
internal static class MySqlInsertPropertySelector
{
    /// <summary>Requires one scalar mapped access rooted in the target parameter.</summary>
    internal static IProperty Resolve(
        IEntityType target,
        LambdaExpression selector
    )
    {
        var members = new Stack<string>();
        var expression = selector.Body;

        while (true)
        {
            switch (expression)
            {
                case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion:
                    expression = conversion.Operand;
                    continue;

                case MemberExpression { Expression: { } owner } member:
                    members.Push(member.Member.Name);
                    expression = owner;
                    continue;

                case MethodCallExpression call when call.Method.DeclaringType == typeof(EF)
                    && call.Method.Name == nameof(EF.Property)
                    && call.Arguments[1] is ConstantExpression { Value: string name }:
                    members.Push(name);
                    expression = call.Arguments[0];
                    continue;
            }

            break;
        }

        if (!ReferenceEquals(expression, selector.Parameters[0])
            || members.Count == 0)
        {
            throw new InvalidOperationException("An INSERT SELECT target selector must be one mapped property access.");
        }

        ITypeBase container = target;

        while (members.Count > 1)
        {
            var name = members.Pop();

            if (container.FindComplexProperty(name) is { } complex)
            {
                container = complex.ComplexType;
                continue;
            }

            // WHY: An owned scalar can share the root's physical table. Resolving ownership
            // here permits complete one-table assignments; the target resolver still rejects
            // values whose actual mapped column belongs to a different physical table.
            if (container is IEntityType entity
                && entity.FindNavigation(name) is { IsCollection: false, ForeignKey.IsOwnership: true } navigation)
            {
                container = navigation.TargetEntityType;
                continue;
            }

            throw new InvalidOperationException(
                $"INSERT SELECT cannot resolve target member '{name}' as a mapped scalar container.");
        }

        var propertyName = members.Pop();

        return container.FindProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"INSERT SELECT target property '{propertyName}' is not a mapped scalar property.");
    }
}
