namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Widening cast with an actual proven recursive bound and preserved explicit text semantics.</summary>
internal sealed class MySqlRecursiveTextCastExpression : SqlExpression
{
    private static readonly ConstructorInfo s_constructor = typeof(MySqlRecursiveTextCastExpression).GetConstructor(
    [
        typeof(SqlExpression),
        typeof(int),
        typeof(string),
        typeof(string),
        typeof(RelationalTypeMapping),
    ])!;

    /// <summary>Initializes a finite native cast after the recursive projection bound has been proved.</summary>
    public MySqlRecursiveTextCastExpression(
        SqlExpression operand,
        int capacity,
        string charSet,
        string collation,
        RelationalTypeMapping typeMapping
    ) : base(typeof(string), typeMapping)
    {
        ArgumentNullException.ThrowIfNull(operand);
        ArgumentNullException.ThrowIfNull(typeMapping);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        if (operand.Type != typeof(string)
            || typeMapping.ClrType != typeof(string)
            || typeMapping.Size != capacity)
        {
            throw new InvalidOperationException("A recursive text cast requires its exact bounded string mapping.");
        }

        Operand = operand;
        Capacity = capacity;
        CharSet = MySqlSqlTokenValidator.ValidateIdentifier(charSet, nameof(charSet));
        Collation = MySqlSqlTokenValidator.ValidateIdentifier(collation, nameof(collation));

        if (!collation.StartsWith(charSet + "_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "A recursive text cast must preserve one known character set and collation.");
        }
    }

    /// <summary>Gets the original scalar anchor expression.</summary>
    public SqlExpression Operand { get; }

    /// <summary>Gets the proved maximum character count after the selected recursive depth.</summary>
    public int Capacity { get; }

    /// <summary>Gets the preserved character set instead of a session-dependent CAST default.</summary>
    public string CharSet { get; }

    /// <summary>Gets the preserved explicit native collation.</summary>
    public string Collation { get; }

    /// <summary>Preserves identity when nullability or parameter processing leaves the scalar child unchanged.</summary>
    public MySqlRecursiveTextCastExpression Update(
        SqlExpression operand
    ) => ReferenceEquals(operand, Operand)
        ? this
        : new MySqlRecursiveTextCastExpression(operand, Capacity, CharSet, Collation, TypeMapping!);

    /// <inheritdoc />
    protected override Expression VisitChildren(
        ExpressionVisitor visitor
    ) => Update((SqlExpression)visitor.Visit(Operand));

    /// <inheritdoc />
    [Experimental("EF9100")]
    public override Expression Quote() => New(
        s_constructor,
        Operand.Quote(),
        Constant(Capacity),
        Constant(CharSet),
        Constant(Collation),
        RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    /// <inheritdoc />
    protected override void Print(
        ExpressionPrinter expressionPrinter
    )
    {
        expressionPrinter.Append("RecursiveTextCast(");
        expressionPrinter.Visit(Operand);
        expressionPrinter
            .Append(", ")
            .Append(Capacity.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(CharSet)
            .Append(", ")
            .Append(Collation)
            .Append(")");
    }

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => ReferenceEquals(this, obj)
        || (obj is MySqlRecursiveTextCastExpression other
            && base.Equals(other)
            && Capacity == other.Capacity
            && Operand.Equals(other.Operand)
            && string.Equals(CharSet, other.CharSet, StringComparison.Ordinal)
            && string.Equals(Collation, other.Collation, StringComparison.Ordinal));

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Operand, Capacity, CharSet, Collation);
}
