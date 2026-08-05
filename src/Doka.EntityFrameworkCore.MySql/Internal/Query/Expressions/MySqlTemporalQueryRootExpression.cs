namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Carries a temporal query operation through EF Core's provider translation pipeline.
/// </summary>
/// <remarks>
/// A single immutable root type keeps query-cache equality exhaustive while avoiding one
/// near-identical expression class per public temporal operator.
/// </remarks>
internal sealed class MySqlTemporalQueryRootExpression : EntityQueryRootExpression
{
    public MySqlTemporalQueryRootExpression(
        IEntityType entityType,
        MySqlTemporalQueryOperation operation,
        DateTime? pointInTime = null,
        DateTime? from = null,
        DateTime? to = null
    ) : base(entityType)
    {
        Operation = operation;
        PointInTime = pointInTime;
        From = from;
        To = to;
    }

    public MySqlTemporalQueryRootExpression(
        IAsyncQueryProvider queryProvider,
        IEntityType entityType,
        MySqlTemporalQueryOperation operation,
        DateTime? pointInTime = null,
        DateTime? from = null,
        DateTime? to = null
    ) : base(queryProvider, entityType)
    {
        Operation = operation;
        PointInTime = pointInTime;
        From = from;
        To = to;
    }

    public MySqlTemporalQueryOperation Operation { get; }

    public DateTime? PointInTime { get; }

    public DateTime? From { get; }

    public DateTime? To { get; }

    public override Expression DetachQueryProvider() =>
        new MySqlTemporalQueryRootExpression(EntityType, Operation, PointInTime, From, To);

    public override EntityQueryRootExpression UpdateEntityType(
        IEntityType entityType
    ) => entityType.ClrType != EntityType.ClrType || entityType.Name != EntityType.Name
        ? throw new InvalidOperationException(CoreStrings.QueryRootDifferentEntityType(entityType.DisplayName()))
        : new MySqlTemporalQueryRootExpression(entityType, Operation, PointInTime, From, To);

    protected override void Print(
        ExpressionPrinter expressionPrinter
    )
    {
        base.Print(expressionPrinter);

        expressionPrinter.Append(
            Operation switch
            {
                MySqlTemporalQueryOperation.AsOf => $".TemporalAsOf({PointInTime})",
                MySqlTemporalQueryOperation.FromTo => $".TemporalFromTo({From}, {To})",
                MySqlTemporalQueryOperation.Between => $".TemporalBetween({From}, {To})",
                MySqlTemporalQueryOperation.ContainedIn => $".TemporalContainedIn({From}, {To})",
                MySqlTemporalQueryOperation.All => ".TemporalAll()",
                _ => throw new InvalidOperationException($"Unknown temporal query operation '{Operation}'."),
            });
    }

    public override bool Equals(
        object? obj
    ) => ReferenceEquals(this, obj)
        || (obj is MySqlTemporalQueryRootExpression other
            && base.Equals(other)
            && Operation == other.Operation
            && PointInTime == other.PointInTime
            && From == other.From
            && To == other.To);

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Operation, PointInTime, From, To);
}
