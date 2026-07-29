namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// MySQL-specific aggregate method call translator plugin.
/// Translates <c>string.Join(separator, group)</c> to <c>GROUP_CONCAT(expr SEPARATOR separator)</c>.
/// </summary>
internal sealed class MySqlAggregateMethodCallTranslatorPlugin : IAggregateMethodCallTranslatorPlugin
{
    public IEnumerable<IAggregateMethodCallTranslator> Translators { get; }

    public MySqlAggregateMethodCallTranslatorPlugin(
        ISqlExpressionFactory sqlExpressionFactory
    )
    {
        Translators = new IAggregateMethodCallTranslator[]
        {
            new MySqlStringAggregateTranslator(sqlExpressionFactory),
        };
    }
}

/// <summary>
/// Translates <c>string.Join(separator, source)</c> aggregate to MySQL <c>GROUP_CONCAT(expr SEPARATOR separator)</c>.
/// </summary>
internal sealed class MySqlStringAggregateTranslator : IAggregateMethodCallTranslator
{
    private static readonly MethodInfo s_stringConcatMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Concat),
        [typeof(IEnumerable<string>)])!;

    private static readonly MethodInfo s_stringJoinMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Join),
        [
            typeof(string),
            typeof(IEnumerable<string>)
        ])!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public MySqlStringAggregateTranslator(
        ISqlExpressionFactory sqlExpressionFactory
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory ?? throw new ArgumentNullException(nameof(sqlExpressionFactory));
    }

    public SqlExpression? Translate(
        MethodInfo method,
        EnumerableExpression source,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(arguments);

        if (source.Selector is not SqlExpression selector
            || (method != s_stringJoinMethod && method != s_stringConcatMethod))
        {
            return null;
        }

        // GROUP_CONCAT skips NULL values, while the CLR methods treat them as empty
        // strings. The predicate is applied after that normalization so filtered-out
        // rows still become SQL NULL and are omitted by the aggregate.
        selector = _sqlExpressionFactory.Coalesce(
            selector,
            _sqlExpressionFactory.Constant(string.Empty));

        if (source.Predicate is not null)
        {
            selector = _sqlExpressionFactory.Case(
                [
                    new CaseWhenClause(source.Predicate, selector),
                ],
                elseResult: null);
        }

        if (source.IsDistinct)
        {
            selector = new DistinctExpression(selector);
        }

        var separator = method == s_stringJoinMethod
            ? _sqlExpressionFactory.Coalesce(arguments[0], _sqlExpressionFactory.Constant(string.Empty))
            : _sqlExpressionFactory.Constant(string.Empty);

        var functionArguments = new List<SqlExpression>(source.Orderings.Count + 2)
        {
            selector,
            separator,
        };

        foreach (var ordering in source.Orderings)
        {
            functionArguments.Add(
                _sqlExpressionFactory.Function(
                    ordering.IsAscending ? "__mysql_order_ascending" : "__mysql_order_descending",
                    [ordering.Expression],
                    nullable: true,
                    argumentsPropagateNullability: [true],
                    ordering.Expression.Type,
                    ordering.Expression.TypeMapping));
        }

        var aggregate = _sqlExpressionFactory.Function(
            "__mysql_group_concat",
            functionArguments,
            nullable: true,
            argumentsPropagateNullability: new bool[functionArguments.Count],
            typeof(string),
            selector.TypeMapping);

        // GROUP_CONCAT returns NULL for an empty input; both CLR methods return "".
        return _sqlExpressionFactory.Coalesce(
            aggregate,
            _sqlExpressionFactory.Constant(string.Empty),
            selector.TypeMapping);
    }
}
