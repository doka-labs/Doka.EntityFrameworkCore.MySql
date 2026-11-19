namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Validates cached window commands using only immutable parameter names and numeric constraints.</summary>
internal sealed class MySqlWindowParameterConstraints
{
    private readonly NumericConstraint[] _arguments;
    private readonly FrameConstraint[] _frames;

    private MySqlWindowParameterConstraints(
        NumericConstraint[] arguments,
        FrameConstraint[] frames
    )
    {
        _arguments = arguments;
        _frames = frames;
    }

    /// <summary>Checks the current invocation before I/O, including reuse of a cached command template.</summary>
    public void Validate(
        IReadOnlyDictionary<string, object?> parameterValues
    )
    {
        foreach (var argument in _arguments)
        {
            ValidateInteger(argument.Value.Resolve(parameterValues), argument.Positive);
        }

        foreach (var frame in _frames)
        {
            ValidateFrame(frame.Rows,
                frame.StartKind,
                frame.Start.Resolve(parameterValues),
                frame.EndKind,
                frame.End.Resolve(parameterValues));
        }
    }

    /// <summary>Validates constants and records only constraints that need current execution values.</summary>
    internal static void Collect(
        MySqlWindowExpression window,
        EngineProfile engine,
        ref Builder? builder
    )
    {
        if (!engine.Has(EngineCapability.WindowFunctions))
        {
            throw new InvalidOperationException("The configured server does not support window functions.");
        }

        var validArity = window.Function switch
        {
            MySqlWindowFunction.Count
                or MySqlWindowFunction.LongCount
                or MySqlWindowFunction.RowNumber
                or MySqlWindowFunction.Rank
                or MySqlWindowFunction.DenseRank
                or MySqlWindowFunction.PercentRank
                or MySqlWindowFunction.CumeDist => window.Arguments.Count == 0,
            MySqlWindowFunction.Lag or MySqlWindowFunction.Lead => window.Arguments.Count is 2 or 3,
            MySqlWindowFunction.NthValue => window.Arguments.Count == 2,
            _ => window.Arguments.Count == 1,
        };

        if (!validArity)
        {
            throw new InvalidOperationException("The window calculation has an invalid argument count.");
        }

        if (window.Orderings.Count == 0
            && RequiresOrdering(window.Function, window.Frame))
        {
            throw new InvalidOperationException("This window calculation requires an explicit window ordering.");
        }

        if (window.Function is MySqlWindowFunction.Lag
            or MySqlWindowFunction.Lead
            or MySqlWindowFunction.Ntile
            or MySqlWindowFunction.NthValue)
        {
            var index = window.Function == MySqlWindowFunction.Ntile
                ? 0
                : 1;

            var argument = NumericValue.FromSql(window.Arguments[index]);
            var positive = window.Function is MySqlWindowFunction.Ntile or MySqlWindowFunction.NthValue;

            if (argument.ParameterName is null)
            {
                ValidateInteger(argument.Constant, positive);
            }
            else
            {
                (builder ??= new Builder()).Add(new NumericConstraint(argument, positive));
            }
        }

        if (window.Frame is not { } frame)
        {
            return;
        }

        if (window.Function is MySqlWindowFunction.RowNumber
            or MySqlWindowFunction.Rank
            or MySqlWindowFunction.DenseRank
            or MySqlWindowFunction.Ntile
            or MySqlWindowFunction.PercentRank
            or MySqlWindowFunction.CumeDist
            or MySqlWindowFunction.Lag
            or MySqlWindowFunction.Lead)
        {
            throw new InvalidOperationException("This window function does not consume an explicit frame.");
        }

        var start = ReadBound(frame.StartKind, frame.StartOffset);
        var end = ReadBound(frame.EndKind, frame.EndOffset);

        if ((start.ParameterName is not null || end.ParameterName is not null)
            && !engine.Has(EngineCapability.WindowFrameParameterOffsets))
        {
            // WHY: MariaDB's frame grammar requires numeric literals even when the surrounding command is prepared.
            throw new InvalidOperationException("The configured server requires literal window-frame offsets.");
        }

        if (start.ParameterName is null
            && end.ParameterName is null)
        {
            ValidateFrame(frame.Rows, frame.StartKind, start.Constant, frame.EndKind, end.Constant);
        }
        else
        {
            // WHY: Store primitive names and bounds, never the SQL tree, model or a captured context.
            (builder ??= new Builder()).Add(new FrameConstraint(frame.Rows,
                frame.StartKind,
                start,
                frame.EndKind,
                end));
        }
    }

    /// <summary>Identifies functions whose positions would otherwise have no explicit ordering contract.</summary>
    internal static bool RequiresOrdering(
        MySqlWindowFunction function,
        MySqlWindowFrame? frame
    ) => frame is not null
        || function is MySqlWindowFunction.RowNumber
            or MySqlWindowFunction.Rank
            or MySqlWindowFunction.DenseRank
            or MySqlWindowFunction.Ntile
            or MySqlWindowFunction.PercentRank
            or MySqlWindowFunction.CumeDist
            or MySqlWindowFunction.Lag
            or MySqlWindowFunction.Lead
            or MySqlWindowFunction.FirstValue
            or MySqlWindowFunction.LastValue
            or MySqlWindowFunction.NthValue;

    private static NumericValue ReadBound(
        MySqlWindowBoundKind kind,
        SqlExpression? offset
    )
    {
        if (!Enum.IsDefined(kind))
        {
            throw new InvalidOperationException("The window frame has an unknown endpoint.");
        }

        if (kind is MySqlWindowBoundKind.Preceding or MySqlWindowBoundKind.Following)
        {
            return offset is null
                ? throw new InvalidOperationException("A bounded window endpoint requires an offset.")
                : NumericValue.FromSql(offset);
        }

        if (offset is not null)
        {
            throw new InvalidOperationException("An unbounded or CURRENT ROW endpoint cannot carry an offset.");
        }

        return default;
    }

    /// <summary>Converts a validated ROWS value at the parameter binder, including offline command inspection.</summary>
    internal static long ConvertRowsOffset(
        decimal value
    )
    {
        ValidateInteger(value, positive: false);

        return checked((long)value);
    }

    private static void ValidateInteger(
        decimal value,
        bool positive
    )
    {
        if (value
            < (positive
                ? 1
                : 0)
            || value > long.MaxValue
            || decimal.Truncate(value) != value)
        {
            throw new ArgumentOutOfRangeException(nameof(value),
                "A window position must be a non-NULL representable integer in the permitted range.");
        }
    }

    private static void ValidateFrame(
        bool rows,
        MySqlWindowBoundKind startKind,
        decimal start,
        MySqlWindowBoundKind endKind,
        decimal end
    )
    {
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "The window frame has a negative start offset.");
        }

        if (end < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "The window frame has a negative end offset.");
        }

        if (startKind == MySqlWindowBoundKind.UnboundedFollowing)
        {
            throw new ArgumentOutOfRangeException(nameof(startKind),
                "A window frame cannot begin at UNBOUNDED FOLLOWING.");
        }

        if (endKind == MySqlWindowBoundKind.UnboundedPreceding)
        {
            throw new ArgumentOutOfRangeException(nameof(endKind), "A window frame cannot end at UNBOUNDED PRECEDING.");
        }

        if (rows)
        {
            ValidateInteger(start, positive: false);
            ValidateInteger(end, positive: false);
        }

        var first = Position(startKind, start);
        var last = Position(endKind, end);

        if (first > last)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "The window frame starts after its ending bound.");
        }
    }

    private static decimal Position(
        MySqlWindowBoundKind kind,
        decimal offset
    ) => kind switch
    {
        MySqlWindowBoundKind.UnboundedPreceding => decimal.MinValue,
        MySqlWindowBoundKind.Preceding => -offset,
        MySqlWindowBoundKind.CurrentRow => 0,
        MySqlWindowBoundKind.Following => offset,
        MySqlWindowBoundKind.UnboundedFollowing => decimal.MaxValue,
        _ => throw new InvalidOperationException("The window frame has an unknown endpoint."),
    };

    internal readonly record struct NumericValue(
        string? ParameterName,
        decimal Constant
    )
    {
        public static NumericValue FromSql(
            SqlExpression expression
        )
        {
            while (expression is SqlUnaryExpression { OperatorType: ExpressionType.Convert } conversion)
            {
                var source = NumericRange(conversion.Operand.Type);
                var target = NumericRange(conversion.Type);

                if (source.Minimum < target.Minimum
                    || source.Maximum > target.Maximum)
                {
                    throw new InvalidOperationException(
                        "Window parameter conversions must preserve the complete input range.");
                }

                expression = conversion.Operand;
            }

            return expression switch
            {
                SqlParameterExpression parameter => new NumericValue(parameter.Name, 0),
                SqlConstantExpression constant => new NumericValue(null, ReadNumber(constant.Value)),
                _ => throw new InvalidOperationException(
                    "Window positions and offsets require a numeric constant or parameter."),
            };
        }

        private static (decimal Minimum, decimal Maximum) NumericRange(
            Type type
        ) => Type.GetTypeCode(Nullable.GetUnderlyingType(type) ?? type) switch
        {
            TypeCode.SByte => (sbyte.MinValue, sbyte.MaxValue),
            TypeCode.Byte => (byte.MinValue, byte.MaxValue),
            TypeCode.Int16 => (short.MinValue, short.MaxValue),
            TypeCode.UInt16 => (ushort.MinValue, ushort.MaxValue),
            TypeCode.Int32 => (int.MinValue, int.MaxValue),
            TypeCode.UInt32 => (uint.MinValue, uint.MaxValue),
            TypeCode.Int64 => (long.MinValue, long.MaxValue),
            TypeCode.UInt64 => (ulong.MinValue, ulong.MaxValue),
            TypeCode.Decimal => (decimal.MinValue, decimal.MaxValue),
            _ => throw new InvalidOperationException("Window positions require exact integral or decimal arguments."),
        };

        public decimal Resolve(
            IReadOnlyDictionary<string, object?> parameters
        )
        {
            if (ParameterName is null)
            {
                return Constant;
            }

            return parameters.TryGetValue(ParameterName, out var value)
                ? ReadNumber(value)
                : throw new InvalidOperationException("A required window parameter was not supplied.");
        }

        private static decimal ReadNumber(
            object? value
        ) => value switch
        {
            sbyte number => number,
            byte number => number,
            short number => number,
            ushort number => number,
            int number => number,
            uint number => number,
            long number => number,
            ulong number => number,
            decimal number => number,

            // WHY: NULL and arbitrary IConvertible objects must not acquire native engine-specific coercion semantics.
            _ => throw new ArgumentOutOfRangeException(nameof(value),
                "Window positions and offsets require a non-NULL numeric value."),
        };
    }

    internal readonly record struct NumericConstraint(
        NumericValue Value,
        bool Positive
    );

    internal readonly record struct FrameConstraint(
        bool Rows,
        MySqlWindowBoundKind StartKind,
        NumericValue Start,
        MySqlWindowBoundKind EndKind,
        NumericValue End
    );

    /// <summary>Collects constraints during SQL generation, then freezes a command-owned primitive policy.</summary>
    internal sealed class Builder
    {
        private List<NumericConstraint>? _arguments;
        private List<FrameConstraint>? _frames;

        internal void Add(
            NumericConstraint constraint
        ) => (_arguments ??= []).Add(constraint);

        internal void Add(
            FrameConstraint constraint
        ) => (_frames ??= []).Add(constraint);

        /// <summary>Builds the immutable execution policy once for a cached command template.</summary>
        public MySqlWindowParameterConstraints Build() => new(_arguments?.ToArray() ?? [], _frames?.ToArray() ?? []);
    }
}
