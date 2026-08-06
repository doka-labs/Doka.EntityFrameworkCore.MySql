namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Carries one MariaDB application-time mutation range through query translation.
/// </summary>
internal sealed class MySqlApplicationTimeQueryRootExpression : EntityQueryRootExpression
{
    public MySqlApplicationTimeQueryRootExpression(
        IEntityType entityType,
        DateTime from,
        DateTime to
    ) : base(entityType)
    {
        From = from;
        To = to;
    }

    public MySqlApplicationTimeQueryRootExpression(
        IAsyncQueryProvider queryProvider,
        IEntityType entityType,
        DateTime from,
        DateTime to
    ) : base(queryProvider, entityType)
    {
        From = from;
        To = to;
    }

    public DateTime From { get; }

    public DateTime To { get; }

    public override Expression DetachQueryProvider() =>
        new MySqlApplicationTimeQueryRootExpression(EntityType, From, To);

    public override EntityQueryRootExpression UpdateEntityType(
        IEntityType entityType
    ) => entityType.ClrType != EntityType.ClrType || entityType.Name != EntityType.Name
        ? throw new InvalidOperationException(CoreStrings.QueryRootDifferentEntityType(entityType.DisplayName()))
        : new MySqlApplicationTimeQueryRootExpression(entityType, From, To);

    protected override void Print(
        ExpressionPrinter expressionPrinter
    )
    {
        base.Print(expressionPrinter);
        expressionPrinter.Append($".ForPortionOf({From}, {To})");
    }

    public override bool Equals(
        object? obj
    ) => ReferenceEquals(this, obj)
        || (obj is MySqlApplicationTimeQueryRootExpression other
            && base.Equals(other)
            && From == other.From
            && To == other.To);

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), From, To);
}
