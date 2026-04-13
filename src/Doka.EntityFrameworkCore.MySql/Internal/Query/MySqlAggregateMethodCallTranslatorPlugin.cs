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
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    private static readonly MethodInfo s_stringJoinMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Join),
        [
            typeof(string),
            typeof(IEnumerable<string>)
        ])!;

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

        if (method != s_stringJoinMethod
            || source.Selector is not SqlExpression selector)
        {
            return null;
        }

        // string.Join(separator, source) → GROUP_CONCAT(selector SEPARATOR separator)
        // Uses a sentinel name that MySqlQuerySqlGenerator intercepts to emit
        // the correct SEPARATOR keyword syntax instead of a comma-separated argument list.
        var separator = arguments[0];

        return _sqlExpressionFactory.Function(
            "__mysql_group_concat",
            new SqlExpression[]
            {
                selector,
                separator
            },
            nullable: true,
            argumentsPropagateNullability:
            [
                true,
                true
            ],
            typeof(string));
    }
}
