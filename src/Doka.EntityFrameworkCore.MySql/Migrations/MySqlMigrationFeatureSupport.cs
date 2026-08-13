namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Describes how the configured provider profile supplies a migration feature.
/// </summary>
public enum MySqlMigrationFeatureSupport
{
    /// <summary>The configured engine cannot provide the feature.</summary>
    Unsupported,

    /// <summary>The configured engine provides the feature natively.</summary>
    Native,

    /// <summary>The provider supplies the feature through a portable emulation.</summary>
    Emulated,
}
