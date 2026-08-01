namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the shared security boundary for non-parameterizable MySQL grammar
/// tokens.
/// </summary>
public sealed class MySqlSqlTokenValidatorTests
{
    [Theory]
    [InlineData("utf8mb4")]
    [InlineData("InnoDB")]
    [InlineData("utf8mb4_0900_ai_ci")]
    public void Valid_ascii_identifier_tokens_round_trip(
        string token
    ) => Assert.Equal(token, MySqlSqlTokenValidator.ValidateIdentifier(token, MySqlAnnotationNames.Collation));

    [Theory]
    [InlineData("utf8mb4 bin")]
    [InlineData("utf8mb4-bin")]
    [InlineData("utf8mb4;SELECT")]
    [InlineData("utf8mb4\u00e9")]
    public void Invalid_identifier_tokens_fail_without_echoing_input(
        string token
    )
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MySqlSqlTokenValidator.ValidateIdentifier(token, MySqlAnnotationNames.Collation));

        Assert.Contains(MySqlAnnotationNames.Collation, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(token, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_token_or_token_name_fails_before_validation()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MySqlSqlTokenValidator.ValidateIdentifier(null!, MySqlAnnotationNames.Collation));
        Assert.Throws<ArgumentException>(() =>
            MySqlSqlTokenValidator.ValidateIdentifier(" ", MySqlAnnotationNames.Collation));
        Assert.Throws<ArgumentNullException>(() => MySqlSqlTokenValidator.ValidateIdentifier("utf8mb4", null!));
        Assert.Throws<ArgumentException>(() => MySqlSqlTokenValidator.ValidateIdentifier("utf8mb4", " "));
    }
}
