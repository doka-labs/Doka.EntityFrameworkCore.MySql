namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Redacts MySQL connection strings for safe inclusion in logs, diagnostics, and
/// error messages. The redactor follows an explicit allowlist: only the
/// connectivity surface a reviewer needs to recognize the target endpoint
/// (server, port, transport security, timeout, and pooling toggle)
/// passes through unchanged. Every other key keeps its name but receives the
/// sentinel <c>***</c> value, so reviewers can still see which option was
/// configured without leaking the secret. The whitelist is intentionally narrow:
/// MySqlConnector's connection-string surface includes thirty-plus keys, several
/// of which (password fields, certificate paths and passwords, SSL key material,
/// authentication-plugin handshake parameters, ApplicationName free-text) carry
/// secrets or PII whose accidental log-trail leak would be hard to recover from.
/// </summary>
internal static class MySqlConnectionStringRedactor
{
    private const string RedactedSentinel = "***";
    private const string NoneSentinel = "<none>";
    private const string MalformedSentinel = "<redacted>";

    private static readonly FrozenSet<string> s_passThroughKeys = new[]
    {
        "Server",
        "Port",
        "SSL Mode",
        "Connection Timeout",
        "Pooling",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a redacted representation of the supplied MySQL connection string.
    /// Null / empty / whitespace inputs yield <see cref="NoneSentinel"/>; inputs
    /// MySqlConnectionStringBuilder cannot parse yield <see cref="MalformedSentinel"/>.
    /// Valid inputs are walked key-by-key: allowlisted keys pass through with their
    /// original value, every other key's value is replaced with <see cref="RedactedSentinel"/>.
    /// </summary>
    public static string Redact(
        string? connectionString
    )
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return NoneSentinel;
        }

        MySqlConnectionStringBuilder builder;
        try
        {
            builder = new MySqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            return MalformedSentinel;
        }

        var passThrough = new MySqlConnectionStringBuilder();
        var redactedKeys = new List<string>();

        foreach (var key in builder.Keys.OfType<string>())
        {
            var rawValue = builder[key];
            if (rawValue is null)
            {
                continue;
            }

            if (s_passThroughKeys.Contains(key))
            {
                passThrough[key] = rawValue;
            }
            else
            {
                // MySqlConnectionStringBuilder rejects a string sentinel for
                // typed options such as booleans, numbers, and enums.
                redactedKeys.Add($"{key}={RedactedSentinel}");
            }
        }

        var result = new StringBuilder(passThrough.ConnectionString);

        if (result.Length > 0
            && result[^1] != ';'
            && redactedKeys.Count > 0)
        {
            result.Append(';');
        }

        foreach (var redactedKey in redactedKeys)
        {
            result
                .Append(redactedKey)
                .Append(';');
        }

        return result.ToString();
    }
}
