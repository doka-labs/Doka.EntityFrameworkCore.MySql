namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsModelDiffer
{
    private static void ApplyIndexAnnotations(
        List<MigrationOperation> operations,
        IRelationalModel target
    )
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(target);

        var indexMetadata = GetProviderIndexMetadata(target);

        foreach (var createIndexOperation in operations.OfType<CreateIndexOperation>())
        {
            createIndexOperation.RemoveAnnotation(MySqlAnnotationNames.SpatialIndex);
            createIndexOperation.RemoveAnnotation(MySqlAnnotationNames.FullTextIndex);
            createIndexOperation.RemoveAnnotation(MySqlAnnotationNames.IndexPrefixLength);

            if (!indexMetadata.TryGetValue(
                    new ProviderIndexIdentity(
                        createIndexOperation.Schema,
                        createIndexOperation.Table,
                        createIndexOperation.Name),
                    out var metadata))
            {
                continue;
            }

            if (metadata.Spatial)
            {
                createIndexOperation.SetAnnotation(MySqlAnnotationNames.SpatialIndex, true);
            }

            if (metadata.FullText)
            {
                createIndexOperation.SetAnnotation(MySqlAnnotationNames.FullTextIndex, true);
            }

            if (metadata.PrefixLengths is not null)
            {
                createIndexOperation.SetAnnotation(MySqlAnnotationNames.IndexPrefixLength, metadata.PrefixLengths);
            }
        }
    }

    private static bool HasProviderIndexMetadataDifferences(
        IRelationalModel source,
        IRelationalModel target
    )
    {
        var sourceMetadata = GetProviderIndexMetadata(source);
        var targetMetadata = GetProviderIndexMetadata(target);

        if (sourceMetadata.Count != targetMetadata.Count)
        {
            return true;
        }

        foreach (var (identity, metadata) in sourceMetadata)
        {
            if (!targetMetadata.TryGetValue(identity, out var targetValue)
                || !ProviderIndexMetadataAreEquivalent(metadata, targetValue))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureProviderIndexMetadataTransitions(
        List<MigrationOperation> operations,
        IRelationalModel? source,
        IRelationalModel target
    )
    {
        if (source is null)
        {
            return;
        }

        var sourceIndexes = GetProviderIndexDescriptors(source);
        var targetIndexes = GetProviderIndexDescriptors(target);
        var drops = new List<DropIndexOperation>();
        var creates = new List<CreateIndexOperation>();

        foreach (var sourceIndex in sourceIndexes
                     .Values
                     .OrderBy(index => index.Identity.Schema, StringComparer.Ordinal)
                     .ThenBy(index => index.Identity.Table, StringComparer.Ordinal)
                     .ThenBy(index => index.Identity.IndexName, StringComparer.Ordinal))
        {
            var targetIndex = ResolveTargetIndex(sourceIndex, targetIndexes);
            if (targetIndex is null
                || ProviderIndexMetadataAreEquivalent(sourceIndex.Metadata, targetIndex.Metadata))
            {
                continue;
            }

            if (!operations
                    .OfType<DropIndexOperation>()
                    .Any(operation => MatchesIndex(
                        operation.Schema,
                        operation.Table,
                        operation.Name,
                        sourceIndex.Identity)))
            {
                drops.Add(
                    new DropIndexOperation
                    {
                        Schema = sourceIndex.Identity.Schema,
                        Table = sourceIndex.Identity.Table,
                        Name = sourceIndex.Identity.IndexName,
                    });
            }

            operations.RemoveAll(operation => operation is RenameIndexOperation renameIndex
                && string.Equals(renameIndex.Name, sourceIndex.Identity.IndexName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    renameIndex.NewName,
                    targetIndex.Identity.IndexName,
                    StringComparison.OrdinalIgnoreCase)
                && (string.Equals(renameIndex.Table, sourceIndex.Identity.Table, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        renameIndex.Table,
                        targetIndex.Identity.Table,
                        StringComparison.OrdinalIgnoreCase)));

            if (operations
                .OfType<CreateIndexOperation>()
                .Any(operation => MatchesIndex(
                    operation.Schema,
                    operation.Table,
                    operation.Name,
                    targetIndex.Identity)))
            {
                continue;
            }

            creates.Add(CreateIndexOperation.CreateFrom(targetIndex.RelationalIndex));
        }

        operations.InsertRange(0, drops);
        operations.AddRange(creates);
    }

    private static ProviderIndexDescriptor? ResolveTargetIndex(
        ProviderIndexDescriptor sourceIndex,
        IReadOnlyDictionary<ProviderIndexIdentity, ProviderIndexDescriptor> targetIndexes
    )
    {
        if (targetIndexes.TryGetValue(sourceIndex.Identity, out var exactMatch))
        {
            return exactMatch;
        }

        ProviderIndexDescriptor? logicalMatch = null;

        foreach (var candidate in targetIndexes.Values)
        {
            if (!candidate.ModelIdentities.Overlaps(sourceIndex.ModelIdentities))
            {
                continue;
            }

            if (logicalMatch is not null)
            {
                throw new InvalidOperationException(
                    $"Provider index '{sourceIndex.Identity.Table}.{sourceIndex.Identity.IndexName}' "
                    + "maps to more than one target index identity.");
            }

            logicalMatch = candidate;
        }

        return logicalMatch;
    }

    private static Dictionary<ProviderIndexIdentity, ProviderIndexMetadata> GetProviderIndexMetadata(
        IRelationalModel model
    ) => GetProviderIndexDescriptors(model)
        .ToDictionary(pair => pair.Key, pair => pair.Value.Metadata, ProviderIndexIdentityComparer.Instance);

    private static Dictionary<ProviderIndexIdentity, ProviderIndexDescriptor> GetProviderIndexDescriptors(
        IRelationalModel model
    )
    {
        // The relational model owns physical index identity, including TPC expansion.
        // Every mapped model index must agree on the provider metadata because one
        // physical index cannot implement conflicting contracts.
        var indexDescriptors = new Dictionary<ProviderIndexIdentity, ProviderIndexDescriptor>(
            ProviderIndexIdentityComparer.Instance);

        foreach (var table in model.Tables)
        {
            var tableMappingSignature = string.Concat(
                table
                    .EntityTypeMappings
                    .Select(mapping => mapping.TypeBase.Name)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .Select(name => $"{name.Length}:{name};"));

            foreach (var relationalIndex in table.Indexes)
            {
                ProviderIndexMetadata? metadata = null;
                var modelIdentities = new HashSet<ProviderModelIndexIdentity>();

                foreach (var index in relationalIndex.MappedIndexes)
                {
                    var mappedMetadata = new ProviderIndexMetadata(
                        Spatial: index.GetMySqlSpatialIndex(),
                        FullText: index.GetMySqlFullTextIndex(),
                        PrefixLengths: index
                            .GetMySqlIndexPrefixLengths()
                            ?.ToArray());

                    if (metadata is not null
                        && !ProviderIndexMetadataAreEquivalent(metadata.Value, mappedMetadata))
                    {
                        throw new InvalidOperationException(
                            $"Mapped indexes for '{table.Name}.{relationalIndex.Name}' "
                            + "have conflicting MySQL metadata.");
                    }

                    metadata = mappedMetadata;
                    modelIdentities.Add(
                        new ProviderModelIndexIdentity(
                            index.DeclaringEntityType.Name,
                            string.Concat(
                                index.Properties.Select(property => $"{property.Name.Length}:{property.Name};")),
                            tableMappingSignature));
                }

                if (metadata is null)
                {
                    throw new InvalidOperationException(
                        $"Relational index '{table.Name}.{relationalIndex.Name}' has no mapped model index.");
                }

                var key = new ProviderIndexIdentity(table.Schema, table.Name, relationalIndex.Name);

                indexDescriptors[key] = new ProviderIndexDescriptor(
                    key,
                    metadata.Value,
                    relationalIndex,
                    modelIdentities);
            }
        }

        return indexDescriptors;
    }

    private static bool ProviderIndexMetadataAreEquivalent(
        ProviderIndexMetadata left,
        ProviderIndexMetadata right
    ) => left.Spatial == right.Spatial
        && left.FullText == right.FullText
        && ValuesAreEquivalent(left.PrefixLengths, right.PrefixLengths);

    private static bool MatchesIndex(
        string? schema,
        string? table,
        string indexName,
        ProviderIndexIdentity identity
    ) => string.Equals(schema, identity.Schema, StringComparison.OrdinalIgnoreCase)
        && string.Equals(table, identity.Table, StringComparison.OrdinalIgnoreCase)
        && string.Equals(indexName, identity.IndexName, StringComparison.OrdinalIgnoreCase);

    private readonly record struct ProviderIndexIdentity(
        string? Schema,
        string Table,
        string IndexName
    );

    private readonly record struct ProviderIndexMetadata(
        bool Spatial,
        bool FullText,
        int[]? PrefixLengths
    );

    private readonly record struct ProviderModelIndexIdentity(
        string EntityTypeName,
        string PropertySignature,
        string TableMappingSignature
    );

    private sealed record ProviderIndexDescriptor(
        ProviderIndexIdentity Identity,
        ProviderIndexMetadata Metadata,
        ITableIndex RelationalIndex,
        HashSet<ProviderModelIndexIdentity> ModelIdentities
    );

    private sealed class ProviderIndexIdentityComparer : IEqualityComparer<ProviderIndexIdentity>
    {
        public static ProviderIndexIdentityComparer Instance { get; } = new();

        public bool Equals(
            ProviderIndexIdentity left,
            ProviderIndexIdentity right
        ) => string.Equals(left.Schema, right.Schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Table, right.Table, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.IndexName, right.IndexName, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(
            ProviderIndexIdentity identity
        ) => HashCode.Combine(
            identity.Schema is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(identity.Schema),
            StringComparer.OrdinalIgnoreCase.GetHashCode(identity.Table),
            StringComparer.OrdinalIgnoreCase.GetHashCode(identity.IndexName));
    }
}
