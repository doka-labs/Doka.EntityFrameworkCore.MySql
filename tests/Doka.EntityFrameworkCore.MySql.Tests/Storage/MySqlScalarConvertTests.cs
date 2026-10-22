namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests all branches of <see cref="MySqlScalarConvert.ToBoolean"/>.
/// </summary>
public sealed class MySqlScalarConvertTests
{
    /// <summary>Null input returns false.</summary>
    [Fact]
    public void Null_returns_false()
    {
        // Arrange
        object? input = null;

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.False(actual);
    }

    /// <summary>Bool values pass through.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Bool_returns_value(
        bool input,
        bool expected
    )
    {
        // Arrange: The theory supplies input, expected.

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Sbyte zero is false, nonzero is true.</summary>
    [Theory]
    [InlineData((sbyte)0, false)]
    [InlineData((sbyte)1, true)]
    [InlineData((sbyte)-1, true)]
    public void Sbyte_converts_correctly(
        sbyte input,
        bool expected
    )
    {
        // Arrange: The theory supplies input, expected.

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Byte zero is false, nonzero is true.</summary>
    [Theory]
    [InlineData((byte)0, false)]
    [InlineData((byte)1, true)]
    [InlineData((byte)255, true)]
    public void Byte_converts_correctly(
        byte input,
        bool expected
    )
    {
        // Arrange: The theory supplies input, expected.

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Short zero is false, nonzero is true.</summary>
    [Theory]
    [InlineData((short)0, false)]
    [InlineData((short)1, true)]
    [InlineData((short)-100, true)]
    public void Short_converts_correctly(
        short input,
        bool expected
    )
    {
        // Arrange: The theory supplies input, expected.

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Ushort zero is false, nonzero is true.</summary>
    [Theory]
    [InlineData((ushort)0, false)]
    [InlineData((ushort)1, true)]
    public void Ushort_converts_correctly(
        ushort input,
        bool expected
    )
    {
        // Arrange: The theory supplies input, expected.

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Int zero is false, nonzero is true.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-42, true)]
    public void Int_converts_correctly(
        int input,
        bool expected
    )
    {
        // Arrange: The theory supplies input, expected.

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Uint zero is false, nonzero is true.</summary>
    [Theory]
    [InlineData(0u, false)]
    [InlineData(1u, true)]
    public void Uint_converts_correctly(
        uint input,
        bool expected
    )
    {
        // Arrange: The theory supplies input, expected.

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Long zero is false, nonzero is true.</summary>
    [Theory]
    [InlineData(0L, false)]
    [InlineData(1L, true)]
    [InlineData(-999L, true)]
    public void Long_converts_correctly(
        long input,
        bool expected
    )
    {
        // Arrange: The theory supplies input, expected.

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Ulong zero is false, nonzero is true.</summary>
    [Theory]
    [InlineData(0UL, false)]
    [InlineData(1UL, true)]
    public void Ulong_converts_correctly(
        ulong input,
        bool expected
    )
    {
        // Arrange: The theory supplies input, expected.

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Float zero is false, nonzero is true.</summary>
    [Theory]
    [InlineData(0f, false)]
    [InlineData(1f, true)]
    [InlineData(-0.5f, true)]
    public void Float_converts_correctly(
        float input,
        bool expected
    )
    {
        // Arrange: The theory supplies input, expected.

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Double zero is false, nonzero is true.</summary>
    [Theory]
    [InlineData(0d, false)]
    [InlineData(1d, true)]
    [InlineData(0.001d, true)]
    public void Double_converts_correctly(
        double input,
        bool expected
    )
    {
        // Arrange: The theory supplies input, expected.

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Decimal zero returns false.</summary>
    [Fact]
    public void Decimal_zero_returns_false()
    {
        // Arrange
        const decimal input = 0m;

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.False(actual);
    }

    /// <summary>Decimal nonzero returns true.</summary>
    [Fact]
    public void Decimal_nonzero_returns_true()
    {
        // Arrange
        const decimal input = 1.5m;

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.True(actual);
    }

    /// <summary>
    /// String dispatch contract: <c>"1"</c> compares ordinal-equal; every other input
    /// routes through <see cref="bool.TryParse(string, out bool)"/> which is
    /// case-insensitive and whitespace-tolerant. Inputs the parser does not recognize
    /// (numeric strings other than "1", yes / no, empty string, non-ASCII, etc.) return
    /// <see langword="false"/>.
    /// </summary>
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("trUe", true)]
    [InlineData("  true  ", true)]
    [InlineData("\ttrue\n", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("FALSE", false)]
    [InlineData("  false  ", false)]
    [InlineData("0", false)]
    [InlineData("2", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("yes", false)]
    [InlineData("no", false)]
    [InlineData("Y", false)]
    [InlineData("N", false)]
    [InlineData("verdadero", false)]
    [InlineData("wahr", false)]
    [InlineData("ja", false)]
    [InlineData("nein", false)]
    public void String_converts_correctly(
        string input,
        bool expected
    )
    {
        // Arrange: The theory supplies input, expected.

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Unsupported types throw InvalidOperationException.</summary>
    [Fact]
    public void Unsupported_type_throws_invalid_operation_exception()
    {
        // Arrange
        Action act = () => MySqlScalarConvert.ToBoolean(new object());

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Contains("System.Object", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>DateTime is not a supported type and should throw.</summary>
    [Fact]
    public void DateTime_throws_invalid_operation_exception()
    {
        // Arrange
        Action act = () => MySqlScalarConvert.ToBoolean(DateTime.Now);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(recordedException);
    }
}
