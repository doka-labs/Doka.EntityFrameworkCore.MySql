namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Resolves type mappings that are mandatory for MySQL query translation.
/// </summary>
internal static class MySqlTranslationTypeMapping
{
    /// <summary>
    /// Returns the relational mapping for a CLR type or fails during translator
    /// construction before an incomplete SQL tree can be produced.
    /// </summary>
    public static RelationalTypeMapping GetRequired(
        IRelationalTypeMappingSource typeMappingSource,
        Type clrType
    )
    {
        ArgumentNullException.ThrowIfNull(typeMappingSource);
        ArgumentNullException.ThrowIfNull(clrType);

        return typeMappingSource.FindMapping(clrType)
            ?? throw new InvalidOperationException($"MySQL query translation requires a mapping for '{clrType.Name}'.");
    }
}
