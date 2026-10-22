namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the backtick-escape behavior of <see cref="MySqlIdentifierEscaping"/>'s
/// <c>DelimitIdentifier</c> helper. The helper guards both the sequence-name and
/// the emulation-table-name interpolation paths inside
/// <see cref="MySqlSequenceValueGenerator"/>; a sequence name carrying a backtick
/// would otherwise terminate the surrounding backtick-delimited identifier and let
/// arbitrary SQL run past the boundary. The escape rule mirrors
/// <see cref="MySqlSqlGenerationHelper"/>: double every embedded backtick, then
/// wrap the result in backticks.
/// </summary>
public sealed class MySqlSequenceIdentifierEscapeTests
{
    /// <summary>
    /// Verifies sequence identifier escaping doubles embedded backticks and wraps the complete name.
    /// </summary>
    /// <param name="sequenceName">The unescaped sequence identifier.</param>
    /// <param name="expected">The expected delimited SQL identifier.</param>
    [Theory]
    [InlineData("OrderSeq", "`OrderSeq`")]
    [InlineData("orders_seq", "`orders_seq`")]
    [InlineData("foo`bar", "`foo``bar`")]
    [InlineData("`leading", "```leading`")]
    [InlineData("trailing`", "`trailing```")]
    [InlineData("`both`", "```both```")]
    [InlineData("`", "````")]
    [InlineData("``", "``````")]
    [InlineData("a`b`c", "`a``b``c`")]
    public void DelimitIdentifier_doubles_embedded_backticks_and_wraps(
        string sequenceName,
        string expected
    )
    {
        // Arrange
        var identifier = sequenceName;

        // Act
        var actual = MySqlIdentifierEscaping.DelimitIdentifier(identifier);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies a missing or blank sequence identifier is rejected before SQL interpolation.
    /// </summary>
    /// <param name="identifier">The missing or blank identifier.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DelimitIdentifier_rejects_null_or_whitespace(
        string? identifier
    )
    {
        // Arrange
        var sequenceName = identifier;

        // Act
        var recordedException = Record.Exception(() => MySqlIdentifierEscaping.DelimitIdentifier(sequenceName!));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(recordedException);
    }
}
