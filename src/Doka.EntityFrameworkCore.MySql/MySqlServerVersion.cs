namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Represents the target MySQL-compatible server family and version for provider configuration.
/// </summary>
public sealed record MySqlServerVersion
{
    /// <summary>
    /// Gets the parsed server version.
    /// </summary>
    public Version Version { get; }

    /// <summary>
    /// Gets a value indicating whether the configured engine is MariaDB.
    /// </summary>
    public bool IsMariaDb { get; }

    internal EngineProfile Profile { get; }

    private MySqlServerVersion(
        Version version,
        bool isMariaDb
    )
    {
        ArgumentNullException.ThrowIfNull(version);

        Version = version;
        IsMariaDb = isMariaDb;
        Profile = EngineProfileTable.Resolve(
            isMariaDb ? EngineFamily.MariaDb : EngineFamily.MySql,
            version);
    }

    /// <summary>
    /// Creates a MySQL server-version descriptor.
    /// </summary>
    /// <param name="version">The server version.</param>
    /// <returns>A configured <see cref="MySqlServerVersion"/> instance.</returns>
    public static MySqlServerVersion MySql(
        Version version
    ) => new(version, false);

    /// <summary>
    /// Creates a MariaDB server-version descriptor.
    /// </summary>
    /// <param name="version">The server version.</param>
    /// <returns>A configured <see cref="MySqlServerVersion"/> instance.</returns>
    public static MySqlServerVersion MariaDb(
        Version version
    ) => new(version, true);

    /// <summary>
    /// Parses a server-version string into a provider server-version descriptor.
    /// </summary>
    /// <param name="serverVersion">The raw server-version string.</param>
    /// <returns>A configured <see cref="MySqlServerVersion"/> instance.</returns>
    public static MySqlServerVersion AutoDetect(
        string serverVersion
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverVersion);

        var version = ParseVersion(serverVersion);
        var isMariaDb = serverVersion.Contains("mariadb", StringComparison.OrdinalIgnoreCase);

        return new MySqlServerVersion(version, isMariaDb);
    }

    /// <summary>
    /// Reads the server-version string from an existing database connection and parses it.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <returns>A configured <see cref="MySqlServerVersion"/> instance.</returns>
    public static MySqlServerVersion AutoDetect(
        DbConnection connection
    )
    {
        ArgumentNullException.ThrowIfNull(connection);

        var serverVersion = connection.ServerVersion;

        return !string.IsNullOrWhiteSpace(serverVersion)
            ? AutoDetect(serverVersion)
            : throw new InvalidOperationException("The supplied connection did not expose a server version.");
    }

    /// <inheritdoc />
    public override string ToString() => FormattableString.Invariant($"{(IsMariaDb ? "MariaDB" : "MySQL")} {Version}");

    private static Version ParseVersion(
        string serverVersion
    )
    {
        var startIndex = -1;
        var endIndex = serverVersion.Length;

        for (var index = 0; index < serverVersion.Length; index++)
        {
            var character = serverVersion[index];

            if (char.IsDigit(character))
            {
                startIndex = index;
                break;
            }
        }

        if (startIndex < 0)
        {
            throw new ArgumentException(
                $"Unable to parse a server version from '{serverVersion}'.",
                nameof(serverVersion));
        }

        for (var index = startIndex; index < serverVersion.Length; index++)
        {
            var character = serverVersion[index];

            if (!char.IsDigit(character)
                && character != '.')
            {
                endIndex = index;
                break;
            }
        }

        var versionToken = serverVersion[startIndex..endIndex]
            .TrimEnd('.');

        // Two-component versions (e.g., "8.4") are intentionally supported and produce
        // Version(major, minor). The dot-check rejects single-component strings like "8".
        if (!versionToken.Contains('.', StringComparison.Ordinal)
            || !Version.TryParse(versionToken, out var version))
        {
            throw new ArgumentException(
                $"Unable to parse a server version from '{serverVersion}'.",
                nameof(serverVersion));
        }

        return version;
    }
}
