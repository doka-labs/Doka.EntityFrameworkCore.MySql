namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Validates MySQL grammar tokens that must be embedded in generated SQL and
/// therefore cannot be represented by command parameters or quoted identifiers.
/// </summary>
internal static class MySqlSqlTokenValidator
{
    /// <summary>
    /// Returns <paramref name="value"/> when it consists only of ASCII letters,
    /// digits, or underscores; otherwise fails before SQL text is emitted.
    /// </summary>
    /// <param name="value">The grammar token to validate.</param>
    /// <param name="tokenName">The bounded provider name used in the failure message.</param>
    /// <returns>The validated token.</returns>
    public static string ValidateIdentifier(
        string value,
        string tokenName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenName);

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character)
                && character != '_')
            {
                // Do not include the rejected value. It can contain log-control
                // characters or other attacker-selected content that must not
                // cross an exception or telemetry boundary.
                throw new InvalidOperationException(
                    $"The value configured for '{tokenName}' contains invalid characters. "
                    + "MySQL grammar identifiers must use ASCII letters, digits, or underscores only.");
            }
        }

        return value;
    }
}
