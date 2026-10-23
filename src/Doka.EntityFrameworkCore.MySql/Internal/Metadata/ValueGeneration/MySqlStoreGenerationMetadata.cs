namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Resolves store generation from local annotations and actual shared-column mappings.</summary>
internal static class MySqlStoreGenerationMetadata
{
    /// <summary>Finds a SQL default without assuming that the entity's primary table contains the property.</summary>
    internal static string? GetDefaultValueSql(
        IReadOnlyProperty property
    )
    {
        if (property.GetDefaultValueSql() is { } localDefault)
        {
            return localDefault;
        }

        // WHY: EF's store-specific getters follow shared-column roots and throw
        // for unmapped stores. This API checks GetColumnName for primary,
        // secondary-fragment and derived mappings without building the relational model.
        foreach (var table in property.GetMappedStoreObjects(StoreObjectType.Table))
        {
            if (property.GetDefaultValueSql(table) is { } sharedDefault)
            {
                return sharedDefault;
            }
        }

        return null;
    }

    /// <summary>Includes computed expressions and defaults inherited from any actual shared column.</summary>
    internal static bool HasStoreGeneratedExpression(
        IReadOnlyProperty property
    )
    {
        if (property.GetDefaultValueSql() is not null
            || property.GetComputedColumnSql() is not null)
        {
            return true;
        }

        // WHY: A direct-annotation fast path keeps ordinary generated properties
        // cheap; shared defaults still require physical-store rather than local-only lookup.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var table in property.GetMappedStoreObjects(StoreObjectType.Table))
        {
            if (property.GetDefaultValueSql(table) is not null
                || property.GetComputedColumnSql(table) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Recognizes a literal default on the property or an actual shared-column root.</summary>
    internal static bool HasLiteralDefault(
        IReadOnlyProperty property
    )
    {
        if (property.TryGetDefaultValue(out _))
        {
            return true;
        }

        // WHY: Integer-key inference must use the same ownership rule as GUID
        // generation, including shared secondary fragments and literal defaults.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var table in property.GetMappedStoreObjects(StoreObjectType.Table))
        {
            if (property.TryGetDefaultValue(table, out _))
            {
                return true;
            }
        }

        return false;
    }
}
