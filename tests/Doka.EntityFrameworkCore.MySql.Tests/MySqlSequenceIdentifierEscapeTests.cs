namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the backtick-escape behavior of <see cref="MySqlSequenceValueGenerator"/>'s
/// internal <c>DelimitIdentifier</c> helper. The helper guards both the sequence-name
/// and the emulation-table-name interpolation paths inside <c>GetNextValue</c> /
/// <c>GetNextValueAsync</c>; a sequence name carrying a backtick would otherwise
/// terminate the surrounding backtick-delimited identifier and let arbitrary SQL
/// run past the boundary. The escape rule mirrors
/// <see cref="MySqlSqlGenerationHelper"/>: double every embedded backtick, then
/// wrap the result in backticks.
/// </summary>
public sealed class MySqlSequenceIdentifierEscapeTests
{
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
    ) => Assert.Equal(expected, MySqlSequenceValueGenerator.DelimitIdentifier(sequenceName));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DelimitIdentifier_rejects_null_or_whitespace(
        string? identifier
    ) => Assert.ThrowsAny<ArgumentException>(() => MySqlSequenceValueGenerator.DelimitIdentifier(identifier!));
}
