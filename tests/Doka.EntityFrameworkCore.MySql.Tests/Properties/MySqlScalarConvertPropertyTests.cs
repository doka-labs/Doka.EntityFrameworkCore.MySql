using FsCheck.Xunit;

namespace Doka.EntityFrameworkCore.MySql.Tests.Properties;

/// <summary>
/// Property-style coverage for <see cref="MySqlScalarConvert.ToBoolean(object?)"/>.
/// The conversion is the single chokepoint every <c>GET_LOCK</c> / <c>RELEASE_LOCK</c>
/// scalar result routes through; its type-dispatch behavior across the full numeric
/// matrix plus the documented string vocabulary needs the FsCheck saturation that
/// per-type fixed tests cannot supply.
/// </summary>
public sealed class MySqlScalarConvertPropertyTests
{
    // The string dispatch in MySqlScalarConvert.ToBoolean is: the literal "1" returns true
    // (Ordinal compare); every other input routes through bool.TryParse, which is
    // case-insensitive and whitespace-tolerant. The property mirrors that contract so a
    // future change to the dispatch surfaces as a property failure with FsCheck's shrunk
    // counter-example, not a silent contract drift.
    private static bool IsRecognizedTrueString(
        string input
    ) => input.Equals("1", StringComparison.Ordinal) || (bool.TryParse(input, out var parsed) && parsed);

    [Property(MaxTest = 1000)]
    public bool ToBoolean_matches_non_zero_contract_for_int(
        int candidate
    ) => MySqlScalarConvert.ToBoolean(candidate) == (candidate != 0);

    [Property(MaxTest = 1000)]
    public bool ToBoolean_matches_non_zero_contract_for_long(
        long candidate
    ) => MySqlScalarConvert.ToBoolean(candidate) == (candidate != 0L);

    [Property(MaxTest = 1000)]
    public bool ToBoolean_matches_non_zero_contract_for_short(
        short candidate
    ) => MySqlScalarConvert.ToBoolean(candidate) == (candidate != 0);

    [Property(MaxTest = 1000)]
    public bool ToBoolean_matches_non_zero_contract_for_byte(
        byte candidate
    ) => MySqlScalarConvert.ToBoolean(candidate) == (candidate != 0);

    [Property(MaxTest = 1000)]
    public bool ToBoolean_matches_non_zero_contract_for_double(
        double candidate
    ) => MySqlScalarConvert.ToBoolean(candidate) == (candidate != 0.0);

    [Property(MaxTest = 1000)]
    public bool ToBoolean_matches_bool_input_exactly(
        bool candidate
    ) => MySqlScalarConvert.ToBoolean(candidate) == candidate;

    [Property(MaxTest = 1000)]
    public bool ToBoolean_recognizes_only_documented_true_strings(
        string? candidate
    )
    {
        if (candidate is null)
        {
            return true;
        }

        return MySqlScalarConvert.ToBoolean(candidate) == IsRecognizedTrueString(candidate);
    }

    [Fact]
    public void ToBoolean_returns_false_for_null()
    {
        Assert.False(MySqlScalarConvert.ToBoolean(null));
    }

    [Fact]
    public void ToBoolean_throws_for_unsupported_clr_type()
    {
        Assert.Throws<InvalidOperationException>(() => MySqlScalarConvert.ToBoolean(new object()));
    }
}
