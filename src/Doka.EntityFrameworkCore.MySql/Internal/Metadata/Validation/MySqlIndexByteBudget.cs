namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Measures physical key parts for convention allocation and final model validation.
/// </summary>
internal static class MySqlIndexByteBudget
{
    /// <summary>
    /// The largest InnoDB key length supported by the provider's current model contract.
    /// </summary>
    internal const int MaximumBytes = 3072;

    /// <summary>
    /// Measures a complete physical key part or its explicitly declared prefix.
    /// </summary>
    /// <param name="property">The property supplying application facets and collation.</param>
    /// <param name="prefixLength">The deliberate prefix length; zero means the complete column.</param>
    /// <param name="fullLength">The resolved complete store length, when bounded.</param>
    /// <param name="bytesPerCharacter">The captured physical character width, or null when unknown.</param>
    /// <param name="useSafeUpperBound">Use four bytes for an unknown charset when allocating;
    /// validation uses a one-byte lower bound to reject only proven overlength values.</param>
    /// <param name="mapping">An optional prospective mapping used before publishing convention lengths.</param>
    /// <returns>The byte width, or null when the physical width cannot be bounded.</returns>
    internal static long? GetIndexedBytes(
        IReadOnlyProperty property,
        int prefixLength,
        int? fullLength,
        int? bytesPerCharacter,
        bool useSafeUpperBound = false,
        RelationalTypeMapping? mapping = null
    )
    {
        var storeType = (mapping ?? property.GetRelationalTypeMapping()).StoreType;
        var storeTypeName = GetStoreTypeName(storeType);
        var indexedLength = prefixLength > 0 ? prefixLength : fullLength;

        if (storeTypeName is "char" or "varchar"
            || (prefixLength > 0 && storeTypeName is "tinytext" or "text" or "mediumtext" or "longtext"))
        {
            if (indexedLength is null)
            {
                return null;
            }

            if (HasUnresolvedCharacterModifiers(storeType))
            {
                bytesPerCharacter = null;
            }

            // WHY: Validation needs a proven rejection lower bound, while implicit
            // allocation needs the four-byte safe upper bound for an unknown charset.
            // Reusing a lower bound to allocate lengths can create invalid native DDL.
            return checked((long)indexedLength.Value * (bytesPerCharacter ?? (useSafeUpperBound ? 4 : 1)));
        }

        if (storeTypeName is "enum" or "set")
        {
            // WHY: Native ENUM and SET storage is bounded, but exact list cardinality
            // is not needed to allocate safely and does not justify a second SQL parser.
            return useSafeUpperBound ? storeTypeName == "enum" ? 2 : 8 : 1;
        }

        return storeTypeName is "binary" or "varbinary"
            || (prefixLength > 0 && storeTypeName is "tinyblob" or "blob" or "mediumblob" or "longblob")
                ? indexedLength
                : GetFixedStoreTypeBytes(storeTypeName, storeType);
    }

    /// <summary>
    /// Enumerates actual table mappings whose columns contain the complete key;
    /// inherited TPC copies and entity-splitting fragments participate separately.
    /// </summary>
    /// <param name="entityType">The entity supplying the physical mapping context.</param>
    /// <param name="properties">Every key or index property that must exist on the same table.</param>
    /// <returns>Only complete physical definitions; omitted cross-table indexes have no entry.</returns>
    internal static IEnumerable<StoreObjectIdentifier> GetPhysicalIndexTables(
        IReadOnlyEntityType entityType,
        IReadOnlyList<IReadOnlyProperty> properties
    )
    {
        var tables = entityType
            .GetMappingFragments(StoreObjectType.Table)
            .Select(fragment => fragment.StoreObject);

        if (StoreObjectIdentifier.Create(entityType, StoreObjectType.Table) is { } primaryTable)
        {
            tables = tables.Prepend(primaryTable);
        }

        foreach (var table in tables)
        {
            if (properties.All(property => property.GetColumnName(table) is not null))
            {
                yield return table;
            }
        }
    }

    private static bool HasUnresolvedCharacterModifiers(
        string storeType
    )
    {
        var facetEnd = storeType.IndexOf(')');
        var modifierStart = facetEnd >= 0 ? facetEnd + 1 : storeType.IndexOf(' ');

        if (modifierStart < 0)
        {
            return false;
        }

        var modifiers = storeType
            .AsSpan(modifierStart)
            .Trim();

        // WHY: Inline CHARSET/COLLATE clauses are native SQL outside EF's charset
        // annotations. Preserve them and use safe bounds without inventing a parser.
        // A sole BINARY modifier changes comparison semantics, not the table charset.
        return !modifiers.IsEmpty && !modifiers.Equals("BINARY", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Captures physical table defaults before repeated index-cap probes. Every
    /// mapped entity can supply the table annotation used by migration DDL.
    /// </summary>
    /// <param name="model">The current model, without requiring its runtime relational model.</param>
    /// <returns>One resolved default set per primary table or mapping fragment.</returns>
    internal static Dictionary<StoreObjectIdentifier, TableDefaults> GetPhysicalTableDefaults(
        IReadOnlyModel model
    )
    {
        var mappings = new Dictionary<StoreObjectIdentifier, List<PhysicalTableMapping>>();

        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.GetTableName() is null)
            {
                continue;
            }

            var strategy = entityType.GetMappingStrategy();
            bool? includesDerivedTypes = entityType
                .GetDirectlyDerivedTypes()
                .Any()
                ? strategy != RelationalAnnotationNames.TpcMappingStrategy
                : null;

            for (var mappedType = (IReadOnlyEntityType?)entityType;
                 mappedType is not null;
                 mappedType = mappedType.BaseType)
            {
                var fragments = mappedType
                    .GetMappingFragments(StoreObjectType.Table)
                    .ToArray();

                var primary = StoreObjectIdentifier.Create(mappedType, StoreObjectType.Table);

                if (primary is { } table)
                {
                    AddPhysicalMapping(
                        mappings,
                        table,
                        new PhysicalTableMapping(entityType, includesDerivedTypes, fragments.Length > 0 ? true : null));

                    foreach (var fragment in fragments)
                    {
                        AddPhysicalMapping(
                            mappings,
                            fragment.StoreObject,
                            new PhysicalTableMapping(entityType, includesDerivedTypes, false));
                    }
                }

                // WHY: EF maps the original derived type onto each TPT base table;
                // TPH and TPC create only the current type's table mappings.
                if (strategy is RelationalAnnotationNames.TpcMappingStrategy
                    or RelationalAnnotationNames.TphMappingStrategy)
                {
                    break;
                }
            }
        }

        var defaults = new Dictionary<StoreObjectIdentifier, TableDefaults>(mappings.Count);

        foreach (var (table, tableMappings) in mappings)
        {
            // WHY: Migration annotations take the first mapped entity's value.
            // Match EF's public-metadata mapping priorities before its relational
            // model exists; selecting an arbitrary entity can undercount bytes or
            // reject a valid first-wins table. Regression tests compare emitted DDL.
            var ordered = tableMappings
                .OrderByDescending(mapping => mapping.IncludesDerivedTypes)
                .ThenByDescending(mapping => mapping.SplitPrincipal)
                .ThenBy(mapping => mapping.EntityType.Name, StringComparer.Ordinal)
                .ToArray();

            var mappedEntities = tableMappings
                .Select(mapping => mapping.EntityType)
                .ToHashSet();
            IReadOnlyEntityType? principal = null;

            foreach (var mapping in ordered)
            {
                var entityType = mapping.EntityType;

                if (!IsPhysicalRowDependent(entityType, mappedEntities)
                    && (principal is null || entityType.IsAssignableFrom(principal)))
                {
                    principal = entityType;
                }
            }

            string? charSet = null;
            string? collation = null;

            foreach (var mapping in
                     ordered.OrderByDescending(mapping => ReferenceEquals(mapping.EntityType, principal)))
            {
                charSet ??= mapping.EntityType.FindAnnotation(MySqlAnnotationNames.CharSet)?.Value as string;
                collation ??= mapping.EntityType.FindAnnotation(RelationalAnnotationNames.Collation)?.Value as string;
            }

            defaults.Add(table, new TableDefaults(charSet, collation));
        }

        return defaults;
    }

    private static void AddPhysicalMapping(
        Dictionary<StoreObjectIdentifier, List<PhysicalTableMapping>> mappings,
        StoreObjectIdentifier table,
        PhysicalTableMapping mapping
    )
    {
        if (!mappings.TryGetValue(table, out var tableMappings))
        {
            tableMappings = [];
            mappings.Add(table, tableMappings);
        }

        tableMappings.Add(mapping);
    }

    private static bool IsPhysicalRowDependent(
        IReadOnlyEntityType entityType,
        HashSet<IReadOnlyEntityType> mappedEntities
    )
    {
        var key = entityType.FindPrimaryKey();

        if (key is null)
        {
            return false;
        }

        var json = entityType.IsMappedToJson();
        var foreignKeys = json
            ? entityType.FindOwnership() is { } ownership ? [ownership] : []
            : entityType
                .GetForeignKeys()
                .Where(foreignKey => foreignKey.Properties.SequenceEqual(key.Properties));

        return foreignKeys.Any(foreignKey => (foreignKey.IsUnique || json)
            && foreignKey.PrincipalKey.IsPrimaryKey()
            && !foreignKey.DeclaringEntityType.IsAssignableFrom(foreignKey.PrincipalEntityType)
            && !foreignKey.PrincipalEntityType.IsAssignableFrom(foreignKey.DeclaringEntityType)
            && mappedEntities.Contains(foreignKey.PrincipalEntityType));
    }

    private readonly record struct PhysicalTableMapping(
        IReadOnlyEntityType EntityType,
        bool? IncludesDerivedTypes,
        bool? SplitPrincipal
    );

    /// <summary>
    /// Resolves a property's physical charset once, keeping explicit property
    /// collation ahead of shared-table defaults and model-wide fallbacks.
    /// </summary>
    /// <param name="entityType">The entity supplying the model default.</param>
    /// <param name="property">The indexed property, including shared-column collation fallback.</param>
    /// <param name="table">The actual table containing this index.</param>
    /// <param name="defaults">The captured defaults of all mappings of that physical table.</param>
    /// <returns>The native maximum encoded width, or null for an unknown charset.</returns>
    internal static int? GetBytesPerCharacter(
        IReadOnlyEntityType entityType,
        IReadOnlyProperty property,
        StoreObjectIdentifier table,
        TableDefaults defaults
    )
    {
        var charSet = GetCharSetFromCollation(property.GetCollation(table))
            // WHY: A shared-table default can live only on another mapped
            // entity (usually its owner), ahead of the model-wide charset.
            ?? GetCharSetFromCollation(defaults.Collation)
            ?? defaults.CharSet
            ?? GetCharSetFromCollation(entityType.Model.GetCollation()) ?? entityType.Model.GetMySqlCharSet();

        return charSet?.Trim().ToLowerInvariant() switch
        {
            "utf8mb4" or "utf16" or "utf16le" or "utf32" or "gb18030" => 4,
            "utf8" or "utf8mb3" or "eucjpms" or "ujis" => 3,
            "big5" or "cp932" or "euckr" or "gb2312" or "gbk" or "sjis" or "ucs2" => 2,
            "armscii8"
                or "ascii"
                or "binary"
                or "cp1250"
                or "cp1251"
                or "cp1256"
                or "cp1257"
                or "cp850"
                or "cp852"
                or "cp866"
                or "dec8"
                or "geostd8"
                or "greek"
                or "hebrew"
                or "hp8"
                or "keybcs2"
                or "koi8r"
                or "koi8u"
                or "latin1"
                or "latin2"
                or "latin5"
                or "latin7"
                or "macce"
                or "macroman"
                or "swe7"
                or "tis620" => 1,
            _ => null,
        };
    }

    /// <summary>
    /// Stores only the table-level annotations needed by index-width resolution.
    /// </summary>
    /// <param name="CharSet">The explicit charset of the physical table, if present.</param>
    /// <param name="Collation">The explicit collation of the physical table, if present.</param>
    internal readonly record struct TableDefaults(
        string? CharSet,
        string? Collation
    );

    private static string? GetCharSetFromCollation(
        string? collation
    )
    {
        if (string.IsNullOrWhiteSpace(collation))
        {
            return null;
        }

        var separator = collation.IndexOf('_');

        return separator > 0 ? collation[..separator] : null;
    }

    /// <summary>
    /// Reads the physical store facet before mapping Size or the model length;
    /// this includes the provider's byte-array length-16 escape to varbinary(17).
    /// </summary>
    /// <param name="property">The model fallback for mappings without a size facet.</param>
    /// <param name="mapping">The actual or prospective relational mapping.</param>
    /// <returns>The physical length, or null for an unbounded representation.</returns>
    internal static int? GetStoreTypeLength(
        IReadOnlyProperty property,
        RelationalTypeMapping? mapping = null
    )
    {
        mapping ??= property.GetRelationalTypeMapping();
        var storeType = mapping.StoreType;
        var facetStart = storeType.IndexOf('(');
        if (facetStart >= 0)
        {
            var facetEnd = storeType.IndexOf(')', facetStart + 1);
            if (facetEnd > facetStart + 1)
            {
                var facet = storeType.AsSpan(facetStart + 1, facetEnd - facetStart - 1);
                var separator = facet.IndexOf(',');
                var lengthFacet = separator >= 0 ? facet[..separator] : facet;

                if (int.TryParse(lengthFacet.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var length))
                {
                    return length;
                }
            }
        }

        return mapping.Size ?? property.GetMaxLength();
    }

    private static string GetStoreTypeName(
        string storeType
    )
    {
        var facetStart = storeType.IndexOf('(');
        var name = facetStart >= 0 ? storeType[..facetStart] : storeType;
        var modifierStart = name.IndexOf(' ');

        return (modifierStart >= 0 ? name[..modifierStart] : name)
            .Trim()
            .ToLowerInvariant();
    }

    private static int? GetFixedStoreTypeBytes(
        string storeTypeName,
        string storeType
    ) => storeTypeName switch
    {
        "bit" => GetBitBytes(storeType),
        "bool" or "boolean" or "tinyint" or "year" => 1,
        "smallint" => 2,
        "mediumint" or "date" => 3,
        "int" or "integer" => 4,
        "float" => GetFloatBytes(storeType),
        "bigint" or "double" or "real" => 8,
        "uuid" => 16,
        "decimal" or "numeric" => GetDecimalBytes(storeType),
        "time" => 3 + GetFractionalSecondsBytes(storeType),
        "datetime" => 5 + GetFractionalSecondsBytes(storeType),
        "timestamp" => 4 + GetFractionalSecondsBytes(storeType),
        _ => null,
    };

    private static int GetFloatBytes(
        string storeType
    )
    {
        var facets = GetStoreTypeFacets(storeType);

        // WHY: FLOAT(p) uses DOUBLE storage above 24 bits; FLOAT(M,D) is a
        // separate, deprecated syntax whose storage remains single precision.
        return !storeType.Contains(',', StringComparison.Ordinal) && facets?.First >= 25 ? 8 : 4;
    }

    private static int GetBitBytes(
        string storeType
    )
    {
        var bits = GetFirstStoreTypeFacet(storeType) ?? 1;

        return checked((bits + 7) / 8);
    }

    private static int? GetDecimalBytes(
        string storeType
    )
    {
        var facets = GetStoreTypeFacets(storeType);
        if (facets is null)
        {
            return null;
        }

        var precision = facets.Value.First;
        var scale = facets.Value.Second;

        if (precision < 1
            || scale < 0
            || scale > precision)
        {
            return null;
        }

        return GetDecimalDigitsBytes(precision - scale) + GetDecimalDigitsBytes(scale);
    }

    private static int GetDecimalDigitsBytes(
        int digits
    )
    {
        ReadOnlySpan<int> remainderBytes = [0, 1, 1, 2, 2, 3, 3, 4, 4];

        return ((digits / 9) * 4) + remainderBytes[digits % 9];
    }

    private static int GetFractionalSecondsBytes(
        string storeType
    )
    {
        var precision = GetFirstStoreTypeFacet(storeType) ?? 0;

        return precision switch
        {
            0 => 0,
            <= 2 => 1,
            <= 4 => 2,
            _ => 3,
        };
    }

    private static int? GetFirstStoreTypeFacet(
        string storeType
    ) => GetStoreTypeFacets(storeType)
        ?.First;

    private static (int First, int Second)? GetStoreTypeFacets(
        string storeType
    )
    {
        var facetStart = storeType.IndexOf('(');
        var facetEnd = facetStart < 0 ? -1 : storeType.IndexOf(')', facetStart + 1);
        if (facetStart < 0
            || facetEnd <= facetStart + 1)
        {
            return null;
        }

        var facets = storeType.AsSpan(facetStart + 1, facetEnd - facetStart - 1);
        var separator = facets.IndexOf(',');
        var firstFacet = separator >= 0 ? facets[..separator] : facets;
        var secondFacet = separator >= 0 ? facets[(separator + 1)..] : "0";

        return int.TryParse(firstFacet.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var first)
            && int.TryParse(secondFacet.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var second)
                ? (first, second)
                : null;
    }

    /// <summary>
    /// Identifies native large-object families that require a deliberate prefix
    /// to participate in an ordinary index.
    /// </summary>
    /// <param name="property">The property with its final relational mapping.</param>
    /// <returns>Whether a complete key part has no bounded native length.</returns>
    internal static bool HasUnboundedStoreType(
        IReadOnlyProperty property
    )
    {
        // Size is not populated by every bounded mapping. Guid converters, for
        // example, can expose binary(16) through a GuidTypeMapping whose Size is
        // null. The normalized store-type family is therefore the authoritative
        // signal for whether the engine can build a complete key or index.
        var storeType = property.GetRelationalTypeMapping().StoreType;

        return GetStoreTypeName(storeType) is "tinytext"
            or "text"
            or "mediumtext"
            or "longtext"
            or "tinyblob"
            or "blob"
            or "mediumblob"
            or "longblob";
    }
}
