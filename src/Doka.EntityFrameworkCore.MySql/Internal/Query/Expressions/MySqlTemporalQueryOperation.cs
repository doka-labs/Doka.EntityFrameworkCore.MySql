namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Identifies the temporal set selected by a provider query root.
/// </summary>
internal enum MySqlTemporalQueryOperation
{
    AsOf,
    FromTo,
    Between,
    ContainedIn,
    All,
}
