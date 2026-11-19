namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlSqlTranslatingExpressionVisitor
{
    /// <summary>Parses only the exact expression-only fluent family before ordinary receiver translation.</summary>
    private bool TryTranslateWindow(
        MethodCallExpression terminal,
        out SqlExpression? translation
    )
    {
        translation = null;

        if (terminal.Method.DeclaringType != typeof(MySqlWindowDefinition))
        {
            return false;
        }

        if (!Enum.TryParse<MySqlWindowFunction>(terminal.Method.Name, out var function))
        {
            throw new InvalidOperationException(
                "A window definition must end in a supported scalar window calculation.");
        }

        var definition = ParseWindowDefinition(terminal.Object
            ?? throw new InvalidOperationException("A window calculation requires its expression-only definition."));

        if (definition.Orderings.Count == 0
            && MySqlWindowParameterConstraints.RequiresOrdering(function, definition.Frame))
        {
            throw new InvalidOperationException("This window calculation requires an explicit window ordering.");
        }

        var arguments = new List<SqlExpression>(terminal.Arguments.Count);

        // WHY: Translate arguments directly into the pre-sized list in their original evaluation order.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var argument in terminal.Arguments)
        {
            arguments.Add(TranslateWindowLeaf(argument));
        }

        if (function is MySqlWindowFunction.Lag or MySqlWindowFunction.Lead
            && arguments.Count == 1)
        {
            // WHY: This implicit offset bypasses scalar visitation, which normally supplies its mapping.
            arguments.Add(_sqlExpressionFactory.Constant(1L, _typeMappingSource.FindMapping(typeof(long))));
        }

        var resultType = Nullable.GetUnderlyingType(terminal.Type) ?? terminal.Type;
        var argumentMapping = arguments.Count == 0
            ? null
            : arguments[0].TypeMapping;

        // WHY: EF's same-type SUM/AVG retains the input's converter and precision. An integer
        // AVG instead returns double and must not inherit an incompatible integer converter.
        var preservesAggregateMapping = function is MySqlWindowFunction.Sum or MySqlWindowFunction.Average
            && argumentMapping?.ClrType == resultType;

        var mapping = preservesAggregateMapping
            || function is MySqlWindowFunction.Min
                or MySqlWindowFunction.Max
                or MySqlWindowFunction.Lag
                or MySqlWindowFunction.Lead
                or MySqlWindowFunction.FirstValue
                or MySqlWindowFunction.LastValue
                or MySqlWindowFunction.NthValue
                ? argumentMapping
                : _typeMappingSource.FindMapping(resultType);

        if (mapping is null)
        {
            throw new InvalidOperationException("The window result has no qualified relational type mapping.");
        }

        if (function == MySqlWindowFunction.Average
            && arguments[0].Type != resultType)
        {
            // WHY: Integer AVG must use the same floating-point input conversion as EF's ordinary Average contract.
            arguments[0] = _sqlExpressionFactory.Convert(arguments[0], resultType, mapping);
        }

        if (function is MySqlWindowFunction.Lag or MySqlWindowFunction.Lead
            && arguments.Count == 3)
        {
            arguments[2] = arguments[2] switch
            {
                SqlConstantExpression constant => constant.ApplyTypeMapping(mapping),
                SqlParameterExpression parameter => parameter.ApplyTypeMapping(mapping),
                var value when Equals(value.TypeMapping, mapping) => value,
                _ => throw new InvalidOperationException(
                    "A window default must use a compatible mapped representation."),
            };
        }

        var nullable = function is not (MySqlWindowFunction.Count
            or MySqlWindowFunction.LongCount
            or MySqlWindowFunction.RowNumber
            or MySqlWindowFunction.Rank
            or MySqlWindowFunction.DenseRank
            or MySqlWindowFunction.Ntile
            or MySqlWindowFunction.PercentRank
            or MySqlWindowFunction.CumeDist);

        var window = new MySqlWindowExpression(
            function,
            arguments,
            definition.Partitions,
            definition.Orderings,
            definition.Frame,
            resultType,
            mapping,
            nullable);

        translation = function == MySqlWindowFunction.Sum
            ? _sqlExpressionFactory.Coalesce(window,
                _sqlExpressionFactory.Constant(Convert.ChangeType(0, resultType, CultureInfo.InvariantCulture),
                    mapping))
            : window;

        return true;
    }

    /// <summary>Retains the fluent chain as compilation-local SQL children, never as executed descriptor objects.</summary>
    private WindowDefinition ParseWindowDefinition(
        Expression expression
    )
    {
        var calls = new Stack<MethodCallExpression>();

        while (expression is MethodCallExpression { Method.DeclaringType: var declaringType } call
            && declaringType == typeof(MySqlWindowDefinition))
        {
            calls.Push(call);
            expression = call.Object
                ?? throw new InvalidOperationException("A window definition cannot escape its fluent expression.");
        }

        if (expression is not MethodCallExpression entry
            || entry.Method.DeclaringType != typeof(MySqlDbFunctionsExtensions)
            || entry.Method.Name != nameof(MySqlDbFunctionsExtensions.Window)
            || entry.Arguments.Count != 1
            || !IsWindowFunctionsReceiver(entry.Arguments[0]))
        {
            throw new InvalidOperationException(
                "A window definition must begin at EF.Functions.Window() in this query.");
        }

        var definition = new WindowDefinition();

        while (calls.TryPop(out var call))
        {
            switch (call.Method.Name)
            {
                case nameof(MySqlWindowDefinition.PartitionBy):
                    AppendWindowKeys(definition.Partitions, call.Arguments[0]);
                    break;

                case nameof(MySqlWindowDefinition.OrderBy):
                case nameof(MySqlWindowDefinition.OrderByDescending):
                    definition.Orderings.Clear();
                    definition.Orderings.Add(new OrderingExpression(
                        TranslateWindowLeaf(call.Arguments[0]),
                        call.Method.Name == nameof(MySqlWindowDefinition.OrderBy)));

                    break;

                case nameof(MySqlWindowDefinition.ThenBy):
                case nameof(MySqlWindowDefinition.ThenByDescending):
                    if (definition.Orderings.Count == 0)
                    {
                        throw new InvalidOperationException("ThenBy requires an earlier window OrderBy.");
                    }

                    definition.Orderings.Add(new OrderingExpression(
                        TranslateWindowLeaf(call.Arguments[0]),
                        call.Method.Name == nameof(MySqlWindowDefinition.ThenBy)));

                    break;

                case nameof(MySqlWindowDefinition.RowsBetween):
                case nameof(MySqlWindowDefinition.RangeBetween):
                    if (definition.Frame is not null)
                    {
                        throw new InvalidOperationException("A window can specify only one explicit frame.");
                    }

                    var start = ParseWindowBound(call.Arguments[0]);
                    var end = ParseWindowBound(call.Arguments[1]);
                    definition.Frame = new MySqlWindowFrame(
                        call.Method.Name == nameof(MySqlWindowDefinition.RowsBetween),
                        start.Kind,
                        start.Offset,
                        end.Kind,
                        end.Offset);

                    break;

                default:
                    throw new InvalidOperationException(
                        "A scalar window result cannot be reused as a fluent definition.");
            }
        }

        if (definition.Frame is { Rows: false } frame
            && (frame.StartOffset is not null || frame.EndOffset is not null)
            && (definition.Orderings.Count != 1 || !IsNumericWindowOrdering(definition.Orderings[0].Expression)))
        {
            throw new InvalidOperationException("An offset RANGE frame requires exactly one numeric window ordering.");
        }

        return definition;
    }

    private static bool IsWindowFunctionsReceiver(
        Expression expression
    ) => (expression is MemberExpression
            {
                Expression: null, Member.DeclaringType: var owner, Member.Name: nameof(EF.Functions),
            }
            && owner == typeof(EF))
        || expression is ConstantExpression { Value: DbFunctions };

    private void AppendWindowKeys(
        List<SqlExpression> keys,
        Expression expression
    )
    {
        if (expression is NewExpression composite)
        {
            foreach (var argument in composite.Arguments)
            {
                AppendWindowKeys(keys, argument);
            }

            return;
        }

        keys.Add(TranslateWindowLeaf(expression));
    }

    private SqlExpression TranslateWindowLeaf(
        Expression expression
    )
    {
        if (Translate(expression) is not { } sql)
        {
            throw new InvalidOperationException(
                "Every window key and argument must translate to a mapped scalar SQL value.");
        }

        var nested = new NestedWindowFinder();
        nested.Visit(sql);

        if (nested.Found)
        {
            throw new InvalidOperationException("Window calculations cannot be nested on the same SQL level.");
        }

        return sql;
    }

    private (MySqlWindowBoundKind Kind, SqlExpression? Offset) ParseWindowBound(
        Expression expression
    )
    {
        if (expression is ConstantExpression { Value: MySqlWindowFrameBound bound })
        {
            return (bound.Kind, bound.Kind is MySqlWindowBoundKind.Preceding or MySqlWindowBoundKind.Following
                ? _sqlExpressionFactory.Constant(bound.Offset)
                : null);
        }

        if (expression is NewExpression { Arguments.Count: 0 } empty
            && empty.Type == typeof(MySqlWindowFrameBound))
        {
            return (MySqlWindowBoundKind.CurrentRow, null);
        }

        if (expression is MemberExpression { Expression: null, Member.DeclaringType: var owner } member
            && owner == typeof(MySqlWindowFrameBound))
        {
            return member.Member.Name switch
            {
                nameof(MySqlWindowFrameBound.CurrentRow) => (MySqlWindowBoundKind.CurrentRow, null),
                nameof(MySqlWindowFrameBound.UnboundedPreceding) => (MySqlWindowBoundKind.UnboundedPreceding, null),
                nameof(MySqlWindowFrameBound.UnboundedFollowing) => (MySqlWindowBoundKind.UnboundedFollowing, null),
                _ => throw new InvalidOperationException("The window endpoint is not a supported typed marker."),
            };
        }

        if (expression is MethodCallExpression { Method.DeclaringType: var declaringType } call
            && declaringType == typeof(MySqlWindowFrameBound))
        {
            var kind = call.Method.Name switch
            {
                nameof(MySqlWindowFrameBound.Preceding) => MySqlWindowBoundKind.Preceding,
                nameof(MySqlWindowFrameBound.Following) => MySqlWindowBoundKind.Following,
                _ => throw new InvalidOperationException("The window endpoint is not a supported typed marker."),
            };

            return (kind, TranslateWindowLeaf(call.Arguments[0]));
        }

        throw new InvalidOperationException(
            "Window frame bounds must remain typed markers inside the query expression.");
    }

    private static bool IsNumericWindowOrdering(
        SqlExpression expression
    )
    {
        var type = expression.TypeMapping?.Converter?.ProviderClrType
            ?? expression.TypeMapping?.ClrType
            ?? expression.Type;

        return Type.GetTypeCode(Nullable.GetUnderlyingType(type) ?? type) is TypeCode.SByte
            or TypeCode.Byte
            or TypeCode.Int16
            or TypeCode.UInt16
            or TypeCode.Int32
            or TypeCode.UInt32
            or TypeCode.Int64
            or TypeCode.UInt64
            or TypeCode.Single
            or TypeCode.Double
            or TypeCode.Decimal;
    }

    private sealed class WindowDefinition
    {
        public List<SqlExpression> Partitions { get; } = [];
        public List<OrderingExpression> Orderings { get; } = [];
        public MySqlWindowFrame? Frame { get; set; }
    }

    private sealed class NestedWindowFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is MySqlWindowExpression or RowNumberExpression)
            {
                Found = true;

                return node;
            }

            // WHY: A separately projected scalar subquery is a different SQL window scope.
            return node is SelectExpression or ScalarSubqueryExpression
                ? node
                : base.VisitExtension(node);
        }
    }
}
