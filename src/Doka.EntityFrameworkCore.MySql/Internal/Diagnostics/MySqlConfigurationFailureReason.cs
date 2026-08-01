namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Defines the bounded reason vocabulary emitted for invalid provider
/// configuration. Detailed validation messages remain in the thrown exception.
/// </summary>
internal enum MySqlConfigurationFailureReason
{
    MissingServerVersion,
    UnsupportedServerVersion,
    MissingConnectionPath,
    ConflictingConnectionPaths,
    ServerVersionChanged,
    RetrySettingsChanged,
    GuidFormatChanged,
    ConnectionPathChanged,
    ForeignKeyNameTooLong,
    IndexNameTooLong,
}
