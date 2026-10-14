namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlMariaDbUuidSupport
{
    public static void EnsureSupported(
        ProviderProfile profile
    )
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Supports(ProviderCapability.NativeUuidColumns))
        {
            return;
        }

        throw new NotSupportedException(
            "Native UUID columns require MariaDB 10.7 or later; "
            + $"the configured server is {profile.Engine.Family} {profile.Engine.Version}.");
    }
}
