namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed class MySqlParameterSizeCommandInterceptorTests
{
    private static readonly byte[] s_shortBytes = [1, 2];
    private static readonly char[] s_shortCharacters = ['a', 'b'];
    private static readonly byte[] s_oversizedBytes = [1, 2, 3, 4];
    private static readonly byte[] s_truncatedBytes = [1, 2];
    private static readonly char[] s_oversizedCharacters = ['a', 'b', 'c'];
    private static readonly char[] s_truncatedCharacters = ['a', 'b'];

    [Theory]
    [MemberData(nameof(NoOpValues))]
    public void Noop_inputs_do_not_call_the_value_setter(
        object? value,
        int size,
        ParameterDirection direction
    )
    {
        var parameter = new TrackingDbParameter(value, size, direction);

        MySqlParameterSizeCommandInterceptor.TruncateInputValue(parameter);

        Assert.Equal(0, parameter.ValueSetterCount);
        Assert.Same(value, parameter.Value);
    }

    [Theory]
    [MemberData(nameof(OversizedValues))]
    public void Oversized_supported_inputs_are_truncated_with_one_setter(
        object value,
        int size,
        object expected
    )
    {
        var parameter = new TrackingDbParameter(value, size, ParameterDirection.InputOutput);

        MySqlParameterSizeCommandInterceptor.TruncateInputValue(parameter);

        Assert.Equal(1, parameter.ValueSetterCount);
        Assert.Equal(expected, parameter.Value);
        Assert.NotSame(value, parameter.Value);
    }

    public static TheoryData<object?, int, ParameterDirection> NoOpValues =>
        new()
        {
            { "short", 5, ParameterDirection.Input },
            { "short", 10, ParameterDirection.InputOutput },
            { s_shortBytes, 2, ParameterDirection.Input },
            { s_shortCharacters, 4, ParameterDirection.Input },
            { 42, 1, ParameterDirection.Input },
            { null, 10, ParameterDirection.Input },
            { DBNull.Value, 10, ParameterDirection.Input },
            { "long", 0, ParameterDirection.Input },
            { "long", 2, ParameterDirection.Output },
            { "long", 2, ParameterDirection.ReturnValue },
        };

    public static TheoryData<object, int, object> OversizedValues =>
        new()
        {
            { "abcdef", 3, "abc" },
            { s_oversizedBytes, 2, s_truncatedBytes },
            { s_oversizedCharacters, 2, s_truncatedCharacters },
        };

    private sealed class TrackingDbParameter : DbParameter
    {
        private object? _value;

        public TrackingDbParameter(
            object? value,
            int size,
            ParameterDirection direction
        )
        {
            _value = value;
            Size = size;
            Direction = direction;
        }

        public int ValueSetterCount { get; private set; }

        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; }

        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        public override int Size { get; set; }

        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;

        public override bool SourceColumnNullMapping { get; set; }

        public override object? Value
        {
            get => _value;
            set
            {
                _value = value;
                ValueSetterCount++;
            }
        }

        public override void ResetDbType() { }
    }
}
