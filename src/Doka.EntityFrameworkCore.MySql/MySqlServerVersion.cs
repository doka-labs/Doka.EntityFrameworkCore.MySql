namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Represents the target MySQL-compatible server family and version for provider configuration.
/// </summary>
public sealed record MySqlServerVersion
{
    /// <summary>
    /// Gets the parsed server version.
    /// </summary>
    public Version Version => Profile.Engine.Version;

    /// <summary>
    /// Gets a value indicating whether the configured engine is MariaDB.
    /// </summary>
    public bool IsMariaDb => Profile.Engine.Family == EngineFamily.MariaDb;

    /// <summary>
    /// Gets the release-line classification against the provider's continuously
    /// tested support matrix.
    /// </summary>
    public MySqlServerVersionSupportStatus SupportStatus { get; }

    /// <summary>
    /// Gets the compatibility mode selected for this descriptor.
    /// </summary>
    public MySqlServerVersionCompatibilityMode CompatibilityMode { get; }

    internal ProviderProfile Profile { get; }

    private MySqlServerVersion(
        Version version,
        bool isMariaDb,
        MySqlServerVersionCompatibilityMode compatibilityMode
    )
    {
        ArgumentNullException.ThrowIfNull(version);

        if (!Enum.IsDefined(compatibilityMode))
        {
            throw new ArgumentOutOfRangeException(nameof(compatibilityMode));
        }

        Profile = new ProviderProfile(EngineProfileTable.Resolve(isMariaDb
            ? EngineFamily.MariaDb
            : EngineFamily.MySql, version));
        SupportStatus = ServerVersionSupportPolicy.Classify(Profile.Engine.Family, version);
        CompatibilityMode = compatibilityMode;
    }

    /// <summary>
    /// Creates a MySQL server-version descriptor that permits only supported
    /// release lines during provider-option validation.
    /// </summary>
    /// <param name="version">The server version.</param>
    /// <returns>A configured <see cref="MySqlServerVersion"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    public static MySqlServerVersion MySql(
        Version version
    ) => MySql(version, MySqlServerVersionCompatibilityMode.SupportedOnly);

    /// <summary>
    /// Creates a MySQL server-version descriptor with an explicit compatibility
    /// mode.
    /// </summary>
    /// <param name="version">The server version.</param>
    /// <param name="compatibilityMode">
    /// The compatibility mode controlling unsupported release lines.
    /// </param>
    /// <returns>A configured <see cref="MySqlServerVersion"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="compatibilityMode"/> is not a defined value.
    /// </exception>
    public static MySqlServerVersion MySql(
        Version version,
        MySqlServerVersionCompatibilityMode compatibilityMode
    ) => new(version, false, compatibilityMode);

    /// <summary>
    /// Creates a MariaDB server-version descriptor that permits only supported
    /// release lines during provider-option validation.
    /// </summary>
    /// <param name="version">The server version.</param>
    /// <returns>A configured <see cref="MySqlServerVersion"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    public static MySqlServerVersion MariaDb(
        Version version
    ) => MariaDb(version, MySqlServerVersionCompatibilityMode.SupportedOnly);

    /// <summary>
    /// Creates a MariaDB server-version descriptor with an explicit compatibility
    /// mode.
    /// </summary>
    /// <param name="version">The server version.</param>
    /// <param name="compatibilityMode">
    /// The compatibility mode controlling unsupported release lines.
    /// </param>
    /// <returns>A configured <see cref="MySqlServerVersion"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="compatibilityMode"/> is not a defined value.
    /// </exception>
    public static MySqlServerVersion MariaDb(
        Version version,
        MySqlServerVersionCompatibilityMode compatibilityMode
    ) => new(version, true, compatibilityMode);

    /// <summary>
    /// Parses a server-version string into a descriptor that permits only supported
    /// release lines during provider-option validation.
    /// </summary>
    /// <param name="serverVersion">The raw server-version string.</param>
    /// <returns>A configured <see cref="MySqlServerVersion"/> instance.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="serverVersion"/> is empty, whitespace, or does not contain a parseable version.
    /// </exception>
    public static MySqlServerVersion Parse(
        string serverVersion
    ) => Parse(serverVersion, MySqlServerVersionCompatibilityMode.SupportedOnly);

    /// <summary>
    /// Parses a server-version string into a descriptor with an explicit
    /// compatibility mode.
    /// </summary>
    /// <param name="serverVersion">The raw server-version string.</param>
    /// <param name="compatibilityMode">
    /// The compatibility mode controlling unsupported release lines.
    /// </param>
    /// <returns>A configured <see cref="MySqlServerVersion"/> instance.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="serverVersion"/> is empty, whitespace, or does not contain a parseable version.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="compatibilityMode"/> is not a defined value.
    /// </exception>
    public static MySqlServerVersion Parse(
        string serverVersion,
        MySqlServerVersionCompatibilityMode compatibilityMode
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverVersion);

        var version = ParseVersion(serverVersion);
        var isMariaDb = serverVersion.Contains("mariadb", StringComparison.OrdinalIgnoreCase);

        return new MySqlServerVersion(version, isMariaDb, compatibilityMode);
    }

    /// <summary>
    /// Reads and parses the server version from a connection into a descriptor that
    /// permits only supported release lines during provider-option validation.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <returns>A configured <see cref="MySqlServerVersion"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The connection does not expose a server version.</exception>
    public static MySqlServerVersion AutoDetect(
        DbConnection connection
    ) => AutoDetect(connection, MySqlServerVersionCompatibilityMode.SupportedOnly);

    /// <summary>
    /// Reads and parses the server version from an existing database connection
    /// with an explicit compatibility mode.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="compatibilityMode">
    /// The compatibility mode controlling unsupported release lines.
    /// </param>
    /// <returns>A configured <see cref="MySqlServerVersion"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="compatibilityMode"/> is not a defined value.
    /// </exception>
    /// <exception cref="InvalidOperationException">The connection does not expose a server version.</exception>
    public static MySqlServerVersion AutoDetect(
        DbConnection connection,
        MySqlServerVersionCompatibilityMode compatibilityMode
    )
    {
        ArgumentNullException.ThrowIfNull(connection);

        var serverVersion = connection.ServerVersion;

        return !string.IsNullOrWhiteSpace(serverVersion)
            ? Parse(serverVersion, compatibilityMode)
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
