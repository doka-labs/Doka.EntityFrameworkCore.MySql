namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Controls whether a server-version descriptor may be used outside the provider's
/// continuously tested support matrix.
/// </summary>
public enum MySqlServerVersionCompatibilityMode
{
    /// <summary>
    /// Reject legacy, unvalidated, and future release lines during provider-option
    /// validation.
    /// </summary>
    SupportedOnly,

    /// <summary>
    /// Explicitly allows an unsupported release line. The provider emits a
    /// structured warning and supplies no compatibility guarantee for that line.
    /// </summary>
    AllowUnsupported,
}
