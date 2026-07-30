namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Classifies a database release line against the provider's continuously tested
/// support matrix.
/// </summary>
public enum MySqlServerVersionSupportStatus
{
    /// <summary>
    /// The release line is continuously tested and supported by the provider.
    /// </summary>
    Supported,

    /// <summary>
    /// The release line predates the oldest supported line for its engine family.
    /// </summary>
    Legacy,

    /// <summary>
    /// The release line falls between supported lines but is not itself part of
    /// the continuously tested matrix.
    /// </summary>
    Unvalidated,

    /// <summary>
    /// The release line is newer than the latest supported line for its engine
    /// family.
    /// </summary>
    Future,
}
