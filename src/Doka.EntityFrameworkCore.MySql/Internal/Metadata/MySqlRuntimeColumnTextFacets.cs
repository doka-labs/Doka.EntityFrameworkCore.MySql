namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Retains only the column collation evidence needed to qualify recursive physical text copies.</summary>
internal static class MySqlRuntimeColumnTextFacets
{
    internal const string RuntimeCollation = MySqlAnnotationNames.Prefix + "RuntimeColumnCollation";

    /// <summary>Records original explicit collation or its known absence, without changing migration metadata.</summary>
    internal static void AddRuntimeAnnotations(
        IDictionary<string, object?> annotations,
        IReadOnlyAnnotatable source,
        IReadOnlyProperty property
    )
    {
        if (!IsText(property.GetRelationalTypeMapping()))
        {
            return;
        }

        // WHY: Empty means the original annotation was absent. A missing receipt in an older
        // supplied model is unknown, so it cannot silently be mistaken for inherited defaults.
        annotations[RuntimeCollation] = source.FindAnnotation(RelationalAnnotationNames.Collation)
                ?.Value as string
            ?? string.Empty;
    }

    /// <summary>Classifies native character storage, including provider-string converters and textual GUIDs.</summary>
    internal static bool IsText(
        RelationalTypeMapping mapping
    ) => mapping.StoreTypeNameBase.ToLowerInvariant() is "char"
        or "varchar"
        or "tinytext"
        or "text"
        or "mediumtext"
        or "longtext"
        or "enum"
        or "set";

    /// <summary>Proves physical column compatibility without querying the server or guessing omitted defaults.</summary>
    internal static bool AreCompatible(
        IColumn left,
        IColumn right
    )
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (!TryReadColumnCollation(left, out var leftCollation)
            || !TryReadColumnCollation(right, out var rightCollation))
        {
            return false;
        }

        // WHY: Distinct columns with the same proved override (including known absence)
        // on the same actual table share its charset/collation inheritance, even when the
        // inherited native default name was not explicitly configured in this EF model.
        if (ReferenceEquals(left.Table, right.Table)
            && string.Equals(leftCollation, rightCollation, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftFacets = Resolve(left, leftCollation);
        var rightFacets = Resolve(right, rightCollation);

        return leftFacets is { } first
            && rightFacets is { } second
            && string.Equals(first.CharSet, second.CharSet, StringComparison.OrdinalIgnoreCase)
            && string.Equals(first.Collation, second.Collation, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resolves the same explicit-column, table, model precedence as the provider's schema declarations.</summary>
    private static (string CharSet, string Collation)? Resolve(
        IColumn column,
        string? collation
    )
    {
        var table = column.Table;
        var model = MySqlTextDefaults.GetModelFacets(table.Model.Model);
        var tableCharSet = table.FindAnnotation(MySqlAnnotationNames.CharSet)
            ?.Value as string;

        collation ??= table.FindAnnotation(RelationalAnnotationNames.Collation)
            ?.Value as string;

        if (collation is null
            && (tableCharSet is null || string.Equals(tableCharSet, model.CharSet, StringComparison.OrdinalIgnoreCase)))
        {
            collation = model.Collation;
        }

        if (collation is null)
        {
            return null;
        }

        var separator = collation.IndexOf('_');
        var collationCharSet = separator > 0
            ? collation[..separator]
            : null;

        // WHY: MariaDB shared UCA names have no unique charset. A prefix is evidence only
        // for a known native charset; ambiguous names retain the actual table/model facet.
        var charSet = MySqlIndexByteBudget.GetBytesPerCharacter(collationCharSet) is not null
            ? collationCharSet
            : tableCharSet ?? model.CharSet;

        return charSet is null
            ? null
            : (charSet.Equals("utf8", StringComparison.OrdinalIgnoreCase)
                ? "utf8mb3"
                : charSet, collation);
    }

    /// <summary>Distinguishes proved inheritance from runtime metadata that never retained the original explicit facet.</summary>
    private static bool TryReadColumnCollation(
        IColumn column,
        out string? collation
    )
    {
        collation = null;
        var tableId = StoreObjectIdentifier.Table(column.Table.Name, column.Table.Schema);

        foreach (var mapping in column.PropertyMappings)
        {
            var property = mapping.Property;
            var overrides = property.FindOverrides(tableId);

            if (!TryRead((IReadOnlyAnnotatable?)overrides ?? property, out var explicitCollation)
                || (explicitCollation is null && overrides is not null && !TryRead(property, out explicitCollation)))
            {
                return false;
            }

            if (collation is not null
                && explicitCollation is not null
                && !string.Equals(collation, explicitCollation, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            collation ??= explicitCollation;
        }

        return column.PropertyMappings.Count != 0;
    }

    private static bool TryRead(
        IReadOnlyAnnotatable source,
        out string? collation
    )
    {
        if (source.FindAnnotation(RelationalAnnotationNames.Collation)
                ?.Value is string configured)
        {
            collation = configured;

            return true;
        }

        if (source.FindAnnotation(RuntimeCollation)
                ?.Value is string retained)
        {
            collation = retained.Length == 0
                ? null
                : retained;

            return true;
        }

        collation = null;

        return source is not RuntimeProperty and not RuntimeRelationalPropertyOverrides;
    }
}
