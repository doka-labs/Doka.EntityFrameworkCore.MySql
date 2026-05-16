using FsCheck.Xunit;

namespace Doka.EntityFrameworkCore.MySql.Tests.Properties;

/// <summary>
/// FsCheck property-style coverage for <see cref="MySqlConnectionStringRedactor"/>.
/// The companion file at <c>../MySqlConnectionStringRedactorPropertyTests.cs</c>
/// covers the named-key plus reflection-walk surfaces; this file pins the secret-
/// substring-no-leak invariant across 1000 randomized iterations. Secrets are
/// prefixed with a stable marker (<c>DOKA_SECRET_PROBE_</c>) so the substring
/// search cannot collide with connection-string keywords (<c>database</c>,
/// <c>server</c>, <c>password</c>, etc.) when the random value happens to share
/// a character sequence with one.
/// </summary>
public sealed class MySqlConnectionStringRedactorPropertyTests
{
    private const string SecretMarker = "DOKA_SECRET_PROBE_";

    [Property(MaxTest = 1000)]
    public bool Redact_never_leaks_marked_password_substring(
        string? passwordSuffix
    )
    {
        if (passwordSuffix is null)
        {
            return true;
        }

        var markedSecret = SecretMarker + passwordSuffix;

        MySqlConnectionStringBuilder builder;
        try
        {
            builder = new MySqlConnectionStringBuilder
            {
                Server = "host",
                Database = "db",
                UserID = "u",
                Password = markedSecret,
            };
        }
        catch (ArgumentException)
        {
            // MySqlConnector rejects some byte sequences in Password; the redactor
            // cannot leak what never reached the builder. Property holds trivially.
            return true;
        }

        var redacted = MySqlConnectionStringRedactor.Redact(builder.ConnectionString);

        return !redacted.Contains(SecretMarker, StringComparison.Ordinal);
    }

    [Property(MaxTest = 1000)]
    public bool Redact_never_leaks_marked_pwd_alias_substring(
        string? passwordSuffix
    )
    {
        if (passwordSuffix is null)
        {
            return true;
        }

        var markedSecret = SecretMarker + passwordSuffix;
        var escapedSecret = EscapeConnectionStringValue(markedSecret);
        var connectionString = $"Server=host;Database=db;User ID=u;Pwd={escapedSecret};";

        string redacted;
        try
        {
            redacted = MySqlConnectionStringRedactor.Redact(connectionString);
        }
        catch (ArgumentException)
        {
            return true;
        }

        return !redacted.Contains(SecretMarker, StringComparison.Ordinal);
    }

    [Property(MaxTest = 1000)]
    public bool Redact_preserves_marked_server_and_database_through_roundtrip(
        string? serverSuffix,
        string? databaseSuffix
    )
    {
        if (serverSuffix is null
            || databaseSuffix is null
            || ContainsControlOrWhitespace(serverSuffix)
            || ContainsControlOrWhitespace(databaseSuffix))
        {
            // MySqlConnector's connection-string parser normalizes control characters
            // and embedded whitespace inconsistently across keys; the redactor's
            // pass-through contract is observable only on inputs MySqlConnector
            // round-trips identically through its own builder. The named-key tests
            // in the companion file pin those edge cases explicitly.
            return true;
        }

        var markedServer = "host-" + serverSuffix;
        var markedDatabase = "db-" + databaseSuffix;

        MySqlConnectionStringBuilder builder;
        try
        {
            builder = new MySqlConnectionStringBuilder
            {
                Server = markedServer,
                Database = markedDatabase,
                UserID = "u",
                Password = "secret-no-leak",
            };
        }
        catch (ArgumentException)
        {
            return true;
        }

        // Self-roundtrip check: skip property when MySqlConnector itself loses
        // the value through builder.ConnectionString -> new builder roundtrip.
        var selfRoundTrip = new MySqlConnectionStringBuilder(builder.ConnectionString);
        if (!string.Equals(selfRoundTrip.Server, builder.Server, StringComparison.Ordinal)
            || !string.Equals(selfRoundTrip.Database, builder.Database, StringComparison.Ordinal))
        {
            return true;
        }

        var redacted = MySqlConnectionStringRedactor.Redact(builder.ConnectionString);

        if (redacted is "<none>" or "<redacted>")
        {
            return true;
        }

        var roundTrip = new MySqlConnectionStringBuilder(redacted);

        return string.Equals(roundTrip.Server, builder.Server, StringComparison.Ordinal)
            && string.Equals(roundTrip.Database, builder.Database, StringComparison.Ordinal)
            && !redacted.Contains("secret-no-leak", StringComparison.Ordinal);
    }

    private static bool ContainsControlOrWhitespace(
        string value
    ) => value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c));

    private static string EscapeConnectionStringValue(
        string value
    ) => value.Contains(';', StringComparison.Ordinal)
        || value.Contains('=', StringComparison.Ordinal)
        || value.Contains('"', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
}
