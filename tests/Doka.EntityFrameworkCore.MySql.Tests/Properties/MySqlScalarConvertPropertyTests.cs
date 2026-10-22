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
    /// <summary>Computes the documented true-string vocabulary independently of provider conversion.</summary>
    // WHY: The string dispatch in MySqlScalarConvert.ToBoolean is: the literal "1" returns true
    // (Ordinal compare); every other input routes through bool.TryParse, which is
    // case-insensitive and whitespace-tolerant. The property mirrors that contract so a
    // future change to the dispatch surfaces as a property failure with FsCheck's shrunk
    // counter-example, not a silent contract drift.
    private static bool IsRecognizedTrueString(
        string input
    ) => input.Equals("1", StringComparison.Ordinal) || (bool.TryParse(input, out var parsed) && parsed);

    /// <summary>Verifies nonzero integer conversion across generated 32-bit operands.</summary>
    [Property(MaxTest = 1000)]
    public bool ToBoolean_matches_non_zero_contract_for_int(
        int candidate
    )
    {
        // Arrange
        var expected = candidate != 0;

        // Act
        var actual = MySqlScalarConvert.ToBoolean(candidate);

        // Assert
        return actual == expected;
    }

    /// <summary>Verifies nonzero integer conversion across generated 64-bit operands.</summary>
    [Property(MaxTest = 1000)]
    public bool ToBoolean_matches_non_zero_contract_for_long(
        long candidate
    )
    {
        // Arrange
        var expected = candidate != 0L;

        // Act
        var actual = MySqlScalarConvert.ToBoolean(candidate);

        // Assert
        return actual == expected;
    }

    /// <summary>Verifies nonzero integer conversion across generated 16-bit operands.</summary>
    [Property(MaxTest = 1000)]
    public bool ToBoolean_matches_non_zero_contract_for_short(
        short candidate
    )
    {
        // Arrange
        var expected = candidate != 0;

        // Act
        var actual = MySqlScalarConvert.ToBoolean(candidate);

        // Assert
        return actual == expected;
    }

    /// <summary>Verifies nonzero conversion across generated unsigned byte operands.</summary>
    [Property(MaxTest = 1000)]
    public bool ToBoolean_matches_non_zero_contract_for_byte(
        byte candidate
    )
    {
        // Arrange
        var expected = candidate != 0;

        // Act
        var actual = MySqlScalarConvert.ToBoolean(candidate);

        // Assert
        return actual == expected;
    }

    /// <summary>Verifies generated floating-point operands against the nonzero conversion contract.</summary>
    [Property(MaxTest = 1000)]
    public bool ToBoolean_matches_non_zero_contract_for_double(
        double candidate
    )
    {
        // Arrange
        var expected = candidate != 0.0;

        // Act
        var actual = MySqlScalarConvert.ToBoolean(candidate);

        // Assert
        return actual == expected;
    }

    /// <summary>Verifies that generated boolean operands preserve their original value.</summary>
    [Property(MaxTest = 1000)]
    public bool ToBoolean_matches_bool_input_exactly(
        bool candidate
    )
    {
        // Arrange
        var expected = candidate;

        // Act
        var actual = MySqlScalarConvert.ToBoolean(candidate);

        // Assert
        return actual == expected;
    }

    /// <summary>Verifies generated strings against the documented boolean vocabulary.</summary>
    [Property(MaxTest = 1000)]
    public bool ToBoolean_recognizes_only_documented_true_strings(
        string? candidate
    )
    {
        // Arrange
        if (candidate is null)
        {
            return true;
        }

        var expected = IsRecognizedTrueString(candidate);

        // Act
        var actual = MySqlScalarConvert.ToBoolean(candidate);

        // Assert
        return actual == expected;
    }

    /// <summary>
    /// Verifies toboolean returns false for null.
    /// </summary>
    [Fact]
    public void ToBoolean_returns_false_for_null()
    {
        // Arrange
        object? candidate = null;

        // Act
        var actual = MySqlScalarConvert.ToBoolean(candidate);

        // Assert
        Assert.False(actual);
    }

    /// <summary>
    /// Verifies toboolean throws for unsupported clr type.
    /// </summary>
    [Fact]
    public void ToBoolean_throws_for_unsupported_clr_type()
    {
        // Arrange
        Action act = () => MySqlScalarConvert.ToBoolean(new object());

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(recordedException);
    }
}
