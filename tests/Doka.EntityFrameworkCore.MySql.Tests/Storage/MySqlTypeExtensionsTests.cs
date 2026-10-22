namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies shared CLR type normalization.
/// </summary>
public sealed class MySqlTypeExtensionsTests
{
    /// <summary>
    /// Verifies that nullable value types are unwrapped while other CLR types
    /// remain unchanged.
    /// </summary>
    [Theory]
    [InlineData(typeof(int?), typeof(int))]
    [InlineData(typeof(string), typeof(string))]
    public void UnwrapNullableType_normalizes_nullable_value_types(
        Type type,
        Type expected
    )
    {
        // Arrange: The theory supplies type, expected.

        // Act
        var actual = type.UnwrapNullableType();

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies that a missing CLR type fails at the shared boundary.
    /// </summary>
    [Fact]
    public void UnwrapNullableType_rejects_null()
    {
        // Arrange
        Action act = () => MySqlTypeExtensions.UnwrapNullableType(null!);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentNullException>(recordedException);
    }
}
