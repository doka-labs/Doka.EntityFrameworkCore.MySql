namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Configures provider-specific reverse-engineering behavior for design-time scaffolding.
/// </summary>
public sealed class MySqlReverseEngineeringOptionsBuilder
{
    private readonly MySqlReverseEngineeringOptions _options;

    internal MySqlReverseEngineeringOptionsBuilder(
        MySqlReverseEngineeringOptions options
    )
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Treats explicit textual GUID columns such as <c>char(36)</c> and <c>varchar(36)</c>
    /// as canonical GUID properties during reverse engineering.
    /// </summary>
    /// <returns>The same <see cref="MySqlReverseEngineeringOptionsBuilder"/> instance.</returns>
    public MySqlReverseEngineeringOptionsBuilder ScaffoldTextGuidsAsGuids()
    {
        _options.ScaffoldTextGuidsAsGuids = true;

        return this;
    }
}
