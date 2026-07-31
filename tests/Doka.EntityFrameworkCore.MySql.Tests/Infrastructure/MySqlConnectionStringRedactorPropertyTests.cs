using System.Reflection;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Property-style coverage for <see cref="MySqlConnectionStringRedactor"/>.
/// Two complementary surfaces are pinned:
/// (1) The named sensitive keys the operability review flagged stay
///     out of the redacted output regardless of their value.
/// (2) A reflection-walk over every settable string-typed property on
///     <see cref="MySqlConnectionStringBuilder"/> proves the allowlist is
///     closed: if a future MySqlConnector release adds a new string-typed
///     connection-string key, the sentinel-leak check forces a deliberate
///     decision to add it to the allowlist (or leave it redacted by default).
/// The allowlisted Server property is excluded from the reflection-walk
/// because its value is explicitly intended to pass through.
/// </summary>
public sealed class MySqlConnectionStringRedactorPropertyTests
{
    private const string Sentinel = "SECRET_PROBE_VALUE_42";

    private static readonly HashSet<string> s_allowlistedPropertyNames =
        new(StringComparer.Ordinal)
        {
            "Server",
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
    [InlineData("Database")]
    [InlineData("User ID")]
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
    public void Redact_passes_through_allowlisted_keys_unchanged(
        string passThroughKey,
        string expectedFragment
    )
    {
        var connectionString = $"{passThroughKey}=host;Password=do-not-leak;";

        var redacted = MySqlConnectionStringRedactor.Redact(connectionString);

        Assert.Contains(expectedFragment, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Password=***", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("do-not-leak", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that typed pool, TLS, and failover values cannot make diagnostic
    /// redaction throw while their non-allowlisted values remain hidden.
    /// </summary>
    [Fact]
    public void Redact_handles_typed_enterprise_connection_options()
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = "primary,secondary",
            Port = 3307,
            Database = "doka",
            UserID = "provider",
            Password = Sentinel,
            SslMode = MySqlSslMode.VerifyFull,
            LoadBalance = MySqlLoadBalance.FailOver,
            Pooling = true,
            MinimumPoolSize = 1,
            MaximumPoolSize = 8,
            ConnectionReset = true,
            AllowUserVariables = true,
        };

        var redacted = MySqlConnectionStringRedactor.Redact(builder.ConnectionString);

        Assert.DoesNotContain(Sentinel, redacted, StringComparison.Ordinal);
        Assert.Contains("Server=primary,secondary", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Database=***", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("User ID=***", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SSL Mode=VerifyFull", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Load Balance=***", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Maximum Pool Size=***", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Connection Reset=***", redacted, StringComparison.OrdinalIgnoreCase);
    }

}
