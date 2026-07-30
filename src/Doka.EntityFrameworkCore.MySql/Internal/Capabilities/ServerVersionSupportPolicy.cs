namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Single source of truth for classifying server release lines against the
/// advertised support matrix from decision D-014.
/// </summary>
internal static class ServerVersionSupportPolicy
{
    public const string SupportedMatrix = "MySQL 8.4; MariaDB 11.4 and 11.8";

    public static MySqlServerVersionSupportStatus Classify(
        EngineFamily family,
        Version version
    )
    {
        ArgumentNullException.ThrowIfNull(version);

        return family switch
        {
            EngineFamily.MySql => ClassifyMySql(version),
            EngineFamily.MariaDb => ClassifyMariaDb(version),
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
    }

    public static string CreateRejectionMessage(
        MySqlServerVersion serverVersion
    )
    {
        ArgumentNullException.ThrowIfNull(serverVersion);

        return $"{serverVersion} is classified as {serverVersion.SupportStatus} and is outside "
            + $"the supported matrix ({SupportedMatrix}). Pass "
            + $"{nameof(MySqlServerVersionCompatibilityMode)}."
            + $"{nameof(MySqlServerVersionCompatibilityMode.AllowUnsupported)} explicitly "
            + "if unsupported execution is intentional.";
    }

    private static MySqlServerVersionSupportStatus ClassifyMySql(
        Version version
    )
    {
        var lineComparison = CompareReleaseLine(version, major: 8, minor: 4);

        return lineComparison switch
        {
            0 => MySqlServerVersionSupportStatus.Supported,
            < 0 => MySqlServerVersionSupportStatus.Legacy,
            > 0 => MySqlServerVersionSupportStatus.Future,
        };
    }

    private static MySqlServerVersionSupportStatus ClassifyMariaDb(
        Version version
    )
    {
        if (IsReleaseLine(version, major: 11, minor: 4)
            || IsReleaseLine(version, major: 11, minor: 8))
        {
            return MySqlServerVersionSupportStatus.Supported;
        }

        if (CompareReleaseLine(version, major: 11, minor: 4) < 0)
        {
            return MySqlServerVersionSupportStatus.Legacy;
        }

        return CompareReleaseLine(version, major: 11, minor: 8) > 0
            ? MySqlServerVersionSupportStatus.Future
            : MySqlServerVersionSupportStatus.Unvalidated;
    }

    private static bool IsReleaseLine(
        Version version,
        int major,
        int minor
    ) => version.Major == major && version.Minor == minor;

    private static int CompareReleaseLine(
        Version version,
        int major,
        int minor
    )
    {
        var majorComparison = version.Major.CompareTo(major);
        return majorComparison != 0 ? majorComparison : version.Minor.CompareTo(minor);
    }
}
