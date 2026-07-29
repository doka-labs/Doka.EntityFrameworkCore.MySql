using System.Reflection;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Property-style coverage for <see cref="MySqlConnectionStringRedactor"/>.
/// Two complementary surfaces are pinned:
/// (1) The twelve named sensitive keys the operability review flagged stay
///     out of the redacted output regardless of their value.
/// (2) A reflection-walk over every settable string-typed property on
///     <see cref="MySqlConnectionStringBuilder"/> proves the allowlist is
///     closed: if a future MySqlConnector release adds a new string-typed
///     connection-string key, the sentinel-leak check forces a deliberate
///     decision to add it to the allowlist (or leave it redacted by default).
/// The allowlisted property names (Server, Database, UserID) are excluded
/// from the reflection-walk because their values are explicitly intended to
/// pass through.
/// </summary>
public sealed class MySqlConnectionStringRedactorPropertyTests
{
    private const string Sentinel = "SECRET_PROBE_VALUE_42";

    private static readonly HashSet<string> s_allowlistedPropertyNames =
        new(StringComparer.Ordinal)
        {
            "Server",
            "Database",
            "UserID",
        };

    [Theory]
    [InlineData("Password")]
    [InlineData("Pwd")]
    [InlineData("CertificateFile")]
    [InlineData("CertificatePassword")]
    [InlineData("SslCa")]
    [InlineData("SslCert")]
    [InlineData("SslKey")]
    [InlineData("Password1")]
    [InlineData("Password2")]
    [InlineData("Password3")]
    [InlineData("AuthenticationPlugin")]
    [InlineData("ApplicationName")]
    public void Redact_never_leaks_sentinel_for_named_sensitive_key(
        string key
    )
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = "host",
            Database = "db",
            UserID = "u",
        };

        try
        {
            builder[key] = Sentinel;
        }
        catch (ArgumentException)
        {
            // The driver version under test does not recognize this key; skip
            // so the assertion list stays current as MySqlConnector evolves.
            return;
        }

        var redacted = MySqlConnectionStringRedactor.Redact(builder.ConnectionString);

        Assert.DoesNotContain(Sentinel, redacted, StringComparison.Ordinal);
        Assert.Contains("server=host", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("database=db", redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redact_never_leaks_sentinel_for_any_non_allowlisted_string_property()
    {
        var properties = typeof(MySqlConnectionStringBuilder)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite
                && property.PropertyType == typeof(string)
                && !s_allowlistedPropertyNames.Contains(property.Name));

        var probed = 0;

        foreach (var property in properties)
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = "host",
                Database = "db",
                UserID = "u",
            };

            try
            {
                property.SetValue(builder, Sentinel);
            }
            catch (ArgumentException)
            {
                // Setter rejects this raw value; the property still cannot
                // leak the sentinel because it never lands in the connection
                // string. Skip without counting it as probed.
                continue;
            }
            catch (TargetInvocationException invocationException)
                when (invocationException.InnerException is ArgumentException)
            {
                continue;
            }

            probed++;
            var redacted = MySqlConnectionStringRedactor.Redact(builder.ConnectionString);

            Assert.DoesNotContain(
                Sentinel,
                redacted,
                StringComparison.Ordinal);
        }

        // Guard rail: the reflection-walk must actually probe at least the
        // canonical Password / Pwd / CertificateFile surface. If the
        // MySqlConnector property surface shrinks the test would silently
        // pass with zero probes; this assertion forces the regression-signal.
        Assert.True(
            probed >= 5,
            $"Reflection-walk probed only {probed} non-allowlisted string properties; the connection-string surface should expose more.");
    }

    [Theory]
    [InlineData("Server", "Server=host")]
    [InlineData("Database", "Database=db")]
    [InlineData("User ID", "User ID=u")]
    public void Redact_passes_through_allowlisted_keys_unchanged(
        string passThroughKey,
        string expectedFragment
    )
    {
        var connectionString = $"{passThroughKey}={GetSampleValue(passThroughKey)};Password=do-not-leak;";

        var redacted = MySqlConnectionStringRedactor.Redact(connectionString);

        Assert.Contains(expectedFragment, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Password=***", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("do-not-leak", redacted, StringComparison.Ordinal);
    }

    private static string GetSampleValue(
        string key
    ) => key switch
    {
        "Server" => "host",
        "Database" => "db",
        "User ID" => "u",
        _ => "value",
    };
}
