namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the shared security boundary for non-parameterizable MySQL grammar
/// tokens.
/// </summary>
public sealed class MySqlSqlTokenValidatorTests
{
    /// <summary>
    /// Verifies valid ascii identifier tokens round trip.
    /// </summary>
    [Theory]
    [InlineData("utf8mb4")]
    [InlineData("InnoDB")]
    [InlineData("utf8mb4_0900_ai_ci")]
    public void Valid_ascii_identifier_tokens_round_trip(
        string token
    )
    {
        // Arrange: The theory supplies token.

        // Act
        var actual = MySqlSqlTokenValidator.ValidateIdentifier(token, MySqlAnnotationNames.Collation);

        // Assert
        Assert.Equal(token, actual);
    }

    /// <summary>
    /// Verifies invalid identifier tokens fail without echoing input.
    /// </summary>
    [Theory]
    [InlineData("utf8mb4 bin")]
    [InlineData("utf8mb4-bin")]
    [InlineData("utf8mb4;SELECT")]
    [InlineData("utf8mb4\u00e9")]
    public void Invalid_identifier_tokens_fail_without_echoing_input(
        string token
    )
    {
        // Arrange
        Action act = () =>
            MySqlSqlTokenValidator.ValidateIdentifier(token, MySqlAnnotationNames.Collation);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Contains(MySqlAnnotationNames.Collation, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(token, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies missing token or token name fails before validation.
    /// </summary>
    [Theory]
    [InlineData(null, MySqlAnnotationNames.Collation, typeof(ArgumentNullException))]
    [InlineData(" ", MySqlAnnotationNames.Collation, typeof(ArgumentException))]
    [InlineData("utf8mb4", null, typeof(ArgumentNullException))]
    [InlineData("utf8mb4", " ", typeof(ArgumentException))]
    public void Missing_token_or_token_name_fails_before_validation(
        string? token,
        string? tokenName,
        Type exceptionType
    )
    {
        // Arrange
        Action act = () => MySqlSqlTokenValidator.ValidateIdentifier(token!, tokenName!);

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.IsType(exceptionType, exception);
    }
}
