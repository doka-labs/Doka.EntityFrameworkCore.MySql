namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Centralizes the naming convention for the MySQL sequence-emulation table
/// (<c>__efsequence_{sequenceName}</c>) so the runtime sequence reader and the
/// migrations DDL generator share one source of truth. Changing the prefix here
/// stays atomic across the runtime + migration surfaces; previously the literal
/// string lived in six callsites and a prefix change would silently desynchronize
/// migrations from runtime sequence reads.
/// </summary>
internal static class MySqlSequenceNaming
{
    /// <summary>
    /// The fixed prefix the provider stamps onto every sequence-emulation table.
    /// Visible as a constant so callers that need a backtick-stripped name (for
    /// example identifier escaping) can branch on it without re-deriving the
    /// substring.
    /// </summary>
    public const string EmulationTablePrefix = "__efsequence_";

    /// <summary>
    /// Returns the fully-qualified emulation-table name for <paramref name="sequenceName"/>:
    /// <c>__efsequence_{sequenceName}</c>. The result is the un-delimited identifier;
    /// callers wrap it through <see cref="MySqlIdentifierEscaping"/> /
    /// <see cref="ISqlGenerationHelper"/> when emitting SQL.
    /// </summary>
    public static string EmulationTableName(
        string sequenceName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceName);

        return EmulationTablePrefix + sequenceName;
    }
}
