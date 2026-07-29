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
    [Fact]
    public void UnwrapNullableType_normalizes_nullable_value_types()
    {
        Assert.Equal(typeof(int), typeof(int?).UnwrapNullableType());
        Assert.Equal(typeof(string), typeof(string).UnwrapNullableType());
    }

    /// <summary>
    /// Verifies that a missing CLR type fails at the shared boundary.
    /// </summary>
    [Fact]
    public void UnwrapNullableType_rejects_null() =>
        Assert.Throws<ArgumentNullException>(() => MySqlTypeExtensions.UnwrapNullableType(null!));
}
