namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlIndexedSelectLowering
{
    /// <summary>Retains grouping NULL membership without changing ordinary LINQ join equality.</summary>
    private static (Expression Body, FieldInfo? OriginalKeyMember) PreserveNullableGroupKey(
        Expression key
    )
    {
        var leaf = key;

        while (leaf is NewExpression { Arguments.Count: 1 } single)
        {
            leaf = single.Arguments[0];
        }

        if (leaf is NewExpression or MemberInitExpression
            || (leaf.Type.IsValueType && Nullable.GetUnderlyingType(leaf.Type) is null))
        {
            return (key, null);
        }

        // WHY: EF's single-column collection join uses ordinary LINQ Join NULL exclusion,
        // although a grouping contains its NULL-key rows. An explicit NULL class makes that
        // correlation composite and preserves grouping equality without a colliding sentinel.
        // Only indexed groups gain this internal bit; their original partition keys and public
        // Key stay unchanged. A required reference key may carry an always-false bit.
        var keyType = TransparentIdentifierFactory.Create(key.Type, typeof(bool));
        var original = keyType
            .GetTypeInfo()
            .GetDeclaredField("Outer")!;

        var nullClass = keyType
            .GetTypeInfo()
            .GetDeclaredField("Inner")!;

        var isNull = Expression.Equal(Box(leaf), Expression.Constant(null));
        var body = CreateCarrier(keyType
                .GetConstructors()[0],
            key,
            isNull,
            original,
            nullClass);

        return (body, original);
    }
}
