namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Sizes implicit variable-length key and index properties against every complete
/// physical index while retaining convention ownership of the resulting lengths.
/// </summary>
internal sealed class MySqlIndexLengthConvention(IRelationalTypeMappingSource typeMappingSource)
    : IModelFinalizingConvention
{
    private const int MaximumImplicitLength = 255;

    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context
    )
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        var constraints = new List<IndexConstraint>();
        var candidates = new Dictionary<IConventionProperty, VariableProperty>();
        var tableDefaults = MySqlIndexByteBudget.GetPhysicalTableDefaults(modelBuilder.Metadata);

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var key in entityType.GetKeys())
            {
                AddConstraint(entityType, key.Properties, null, constraints, candidates, tableDefaults);
            }

            foreach (var index in entityType.GetIndexes())
            {
                if (!index.GetMySqlFullTextIndex()
                    && !index.GetMySqlSpatialIndex())
                {
                    AddConstraint(
                        entityType,
                        index.Properties,
                        index.GetMySqlIndexPrefixLengths(),
                        constraints,
                        candidates,
                        tableDefaults);
                }
            }
        }

        MergeSharedColumnCandidates(modelBuilder.Metadata, candidates);
        MergeForeignKeyCandidates(modelBuilder.Metadata, candidates);

        // WHY: Each constraint is solved against the same initial facets. Publishing
        // a result while enumerating indexes makes later allocation depend on order.
        foreach (var constraint in constraints)
        {
            ApplySharedByteBudget(constraint, candidates);
        }

        foreach (var (property, variable) in candidates)
        {
            if (variable.FixedMapping is { } fixedMapping)
            {
                if (property.Builder.HasColumnType(fixedMapping.StoreType) is null
                    || !string.Equals(
                        property.GetColumnType(),
                        fixedMapping.StoreType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"The implicit index store type for '{property.DeclaringType.DisplayName()}.{property.Name}' "
                        + "could not be applied without replacing application metadata.");
                }
            }

            if (property.Builder.HasMaxLength(variable.Length) is null
                || property.GetMaxLength() != variable.Length)
            {
                throw new InvalidOperationException(
                    $"The implicit index length for '{property.DeclaringType.DisplayName()}.{property.Name}' "
                    + "could not be applied without replacing application metadata.");
            }
        }

        VerifyFinalMappings(constraints);
    }

    private void AddConstraint(
        IConventionEntityType entityType,
        IReadOnlyList<IConventionPropertyBase> indexProperties,
        IReadOnlyList<int>? prefixes,
        List<IndexConstraint> constraints,
        Dictionary<IConventionProperty, VariableProperty> candidates,
        Dictionary<StoreObjectIdentifier, MySqlIndexByteBudget.TableDefaults> tableDefaults
    )
    {
        // Invalid application prefixes are diagnosed by the final model validator.
        if (prefixes is not null
            && (prefixes.Count != indexProperties.Count || prefixes.Any(length => length < 0)))
        {
            return;
        }

        var properties = new List<IConventionProperty>();
        List<int>? scalarPrefixes = prefixes is null ? null : [];

        for (var index = 0; index < indexProperties.Count; index++)
        {
            var property = indexProperties[index];
            var prefix = prefixes?[index] ?? 0;

            if (property is IConventionComplexProperty && prefix > 0)
            {
                // WHY: A prefix on a complex value has no single physical column.
                // Leave the application error to the final model validator.
                return;
            }

            foreach (var scalar in GetScalarProperties(property))
            {
                properties.Add(scalar);
                scalarPrefixes?.Add(prefix);
            }
        }

        var mappings = new RelationalTypeMapping[properties.Count];
        var hasVariableProperty = false;

        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            var mapping = typeMappingSource.FindMapping((IProperty)property)
                ?? throw new InvalidOperationException(
                    $"No relational type mapping exists for '{entityType.DisplayName()}.{property.Name}'.");
            mappings[index] = mapping;

            if (!IsImplicitVariableLengthProperty(property, mapping))
            {
                continue;
            }

            hasVariableProperty = true;

            if (!candidates.TryGetValue(property, out var variable))
            {
                variable = new VariableProperty(mapping, property.GetMaxLength());
                candidates.Add(property, variable);
            }

            variable.MinimumLength = Math.Max(variable.MinimumLength, scalarPrefixes?[index] ?? 0);
        }

        if (!hasVariableProperty)
        {
            return;
        }

        foreach (var table in MySqlIndexByteBudget.GetPhysicalIndexTables(entityType, properties))
        {
            var widths = properties
                .Select(property => MySqlIndexByteBudget.GetBytesPerCharacter(
                    entityType,
                    property,
                    table,
                    tableDefaults[table]))
                .ToArray();

            constraints.Add(new IndexConstraint(entityType, properties, scalarPrefixes, mappings, widths));
        }
    }

    private void MergeSharedColumnCandidates(
        IConventionModel model,
        Dictionary<IConventionProperty, VariableProperty> candidates
    )
    {
        var columns = new Dictionary<(StoreObjectIdentifier Table, string Column), List<IConventionProperty>>();

        foreach (var entityType in model.GetEntityTypes())
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
                foreach (var property in GetScalarProperties(entityType))
                {
                    if (property.GetColumnName(table) is not { } column)
                    {
                        continue;
                    }

                    var identity = (table, column);

                    if (!columns.TryGetValue(identity, out var aliases))
                    {
                        aliases = [];
                        columns.Add(identity, aliases);
                    }

                    if (!aliases.Contains(property))
                    {
                        aliases.Add(property);
                    }
                }
            }
        }

        foreach (var aliases in columns.Values)
        {
            var variables = aliases
                .Where(candidates.ContainsKey)
                .Select(property => candidates[property])
                .Distinct()
                .ToArray();

            if (variables.Length == 0
                || aliases.Count == 1)
            {
                continue;
            }

            var fixedContracts = variables
                .Where(variable => variable.FixedMapping is not null)
                .Select(variable => (Mapping: variable.FixedMapping!, Length: variable.InitialLength,
                    variable.HasConceptualLength))
                .ToArray();

            var shared = variables[0];
            shared.FixedMapping = null;
            shared.HasConceptualLength = false;
            shared.InitialLength = variables.Min(variable => variable.InitialLength);
            shared.Length = shared.InitialLength;
            shared.MinimumLength = variables.Max(variable => variable.MinimumLength);

            foreach (var (mapping, length, hasConceptualLength) in fixedContracts)
            {
                ApplySharedFixedMapping(
                    shared,
                    mapping,
                    length,
                    hasConceptualLength);
            }

            foreach (var alias in aliases)
            {
                var mapping = typeMappingSource.FindMapping((IProperty)alias)!;

                if (!IsImplicitVariableLengthProperty(alias, mapping))
                {
                    var physicalLength = MySqlIndexByteBudget.GetStoreTypeLength(alias, mapping);

                    if (physicalLength is null)
                    {
                        throw new InvalidOperationException(
                            $"The shared indexed column '{alias.Name}' requires a bounded application mapping "
                            + "before an implicit alias can be allocated.");
                    }

                    // WHY: MaxLength is conceptual metadata. A byte[] length of 16
                    // maps physically to varbinary(17), but must remain length 16.
                    var conceptualLength = alias.GetMaxLength() ?? mapping.Converter?.MappingHints?.Size;
                    ApplySharedFixedMapping(
                        shared,
                        mapping,
                        conceptualLength ?? physicalLength.Value,
                        conceptualLength is not null);
                }
            }

            // A property may also participate in another physical mapping (TPC or
            // entity splitting). Retain the complete transitive alias group.
            var mergedProperties = candidates
                .Where(pair => variables.Contains(pair.Value))
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var property in mergedProperties)
            {
                candidates[property] = shared;
            }

            // WHY: Separate model properties can name one physical column. All of
            // their indexes constrain that same column, including an unindexed alias.
            foreach (var alias in aliases)
            {
                var mapping = typeMappingSource.FindMapping((IProperty)alias)!;

                if (IsImplicitVariableLengthProperty(alias, mapping))
                {
                    candidates[alias] = shared;
                }
            }
        }
    }

    /// <summary>
    /// Retains principal-derived implicit lengths across compatible FK columns
    /// while leaving deliberate application asymmetry unchanged.
    /// </summary>
    private void MergeForeignKeyCandidates(
        IConventionModel model,
        Dictionary<IConventionProperty, VariableProperty> candidates
    )
    {
        if (candidates.Count == 0)
        {
            return;
        }

        var relationships = model
            .GetEntityTypes()
            .SelectMany(entityType => entityType.GetDeclaredForeignKeys())
            .SelectMany(foreignKey => foreignKey.Properties
                .Select((dependent, index) => (Dependent: dependent, Principal: foreignKey.PrincipalKey.Properties[index])))
            .ToArray();

        foreach (var (dependent, principal) in relationships)
        {
            if (!candidates.TryGetValue(dependent, out var dependentVariable)
                || !candidates.TryGetValue(principal, out var principalVariable)
                || ReferenceEquals(dependentVariable, principalVariable)
                || dependentVariable.FixedMapping is not null
                || principalVariable.FixedMapping is not null
                || !HaveCompatibleForeignKeyMappings(
                    dependent,
                    principal,
                    dependentVariable.Mapping,
                    principalVariable.Mapping))
            {
                continue;
            }

            // WHY: EF normally infers an implicit FK's size from its principal.
            // Independent index caps must not replace that inference with two
            // different convention facets. This is not a native FK equality rule:
            // deliberate dependent facets and differing representations stay owned
            // by the application instead of being propagated back to the principal.
            principalVariable.InitialLength = Math.Min(
                principalVariable.InitialLength,
                dependentVariable.InitialLength);

            principalVariable.Length = principalVariable.InitialLength;
            principalVariable.MinimumLength = Math.Max(
                principalVariable.MinimumLength,
                dependentVariable.MinimumLength);

            var mergedProperties = candidates
                .Where(pair => ReferenceEquals(pair.Value, dependentVariable))
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var property in mergedProperties)
            {
                candidates[property] = principalVariable;
            }
        }

        foreach (var (dependent, principal) in relationships)
        {
            if (!candidates.TryGetValue(dependent, out var variable)
                || variable.FixedMapping is not null)
            {
                continue;
            }

            candidates.TryGetValue(principal, out var principalVariable);
            var sharedPrincipalMapping = principalVariable?.FixedMapping;

            if (principalVariable is not null
                && sharedPrincipalMapping is null)
            {
                continue;
            }

            var principalMapping = sharedPrincipalMapping ?? typeMappingSource.FindMapping((IProperty)principal)!;
            var dependentMapping = typeMappingSource.FindMapping((IProperty)dependent)!;

            if (!HaveCompatibleForeignKeyMappings(dependent, principal, dependentMapping, principalMapping)
                || (sharedPrincipalMapping is null
                    && !string.Equals(
                        dependentMapping.StoreType,
                        principalMapping.StoreType,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var conceptualLength = sharedPrincipalMapping is not null
                ? principalVariable!.HasConceptualLength ? principalVariable.InitialLength : null
                : principal.GetMaxLength() ?? principalMapping.Converter?.MappingHints?.Size;

            var length = sharedPrincipalMapping is not null
                ? principalVariable!.InitialLength
                : conceptualLength ?? MySqlIndexByteBudget.GetStoreTypeLength(principal, principalMapping);

            if (length is not null)
            {
                // WHY: Preserve a principal's deliberate contract only when EF has
                // inferred that representation or a shared physical alias establishes
                // it before publication. A dependent's explicit asymmetry never
                // anchors its principal, and no converter or collation is replaced.
                ApplySharedFixedMapping(variable, principalMapping, length.Value, conceptualLength is not null);
            }
        }
    }

    /// <summary>
    /// Restricts implicit length inference to the same mapped representation;
    /// converters and collations remain application-owned contracts.
    /// </summary>
    private static bool HaveCompatibleForeignKeyMappings(
        IConventionProperty dependent,
        IConventionProperty principal,
        RelationalTypeMapping dependentMapping,
        RelationalTypeMapping principalMapping
    ) => dependentMapping.ClrType == principalMapping.ClrType
        && dependentMapping.IsUnicode == principalMapping.IsUnicode
        && string.Equals(
            dependentMapping.StoreTypeNameBase,
            principalMapping.StoreTypeNameBase,
            StringComparison.OrdinalIgnoreCase)
        && ReferenceEquals(dependent.GetValueConverter(), principal.GetValueConverter())
        && string.Equals(dependent.GetCollation(), principal.GetCollation(), StringComparison.OrdinalIgnoreCase);

    private static void ApplySharedFixedMapping(
        VariableProperty variable,
        RelationalTypeMapping mapping,
        int length,
        bool hasConceptualLength
    )
    {
        if (variable.FixedMapping is not null
            && !string.Equals(variable.FixedMapping.StoreType, mapping.StoreType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A shared indexed column has conflicting explicit store mappings.");
        }

        if (variable.HasConceptualLength
            && hasConceptualLength
            && variable.InitialLength != length)
        {
            throw new InvalidOperationException("A shared indexed column has conflicting explicit length facets.");
        }

        variable.FixedMapping = mapping;

        if (variable.HasConceptualLength
            && !hasConceptualLength)
        {
            return;
        }

        variable.HasConceptualLength = hasConceptualLength;
        variable.InitialLength = length;
        variable.Length = length;
    }

    private void VerifyFinalMappings(
        List<IndexConstraint> constraints
    )
    {
        foreach (var constraint in constraints)
        {
            long bytes = 0;

            for (var index = 0; index < constraint.Properties.Count; index++)
            {
                var property = constraint.Properties[index];
                var mapping = typeMappingSource.FindMapping((IProperty)property)!;
                var length = MySqlIndexByteBudget.GetStoreTypeLength(property, mapping);
                var width = MySqlIndexByteBudget.GetIndexedBytes(
                    property,
                    constraint.Prefixes?[index] ?? 0,
                    length,
                    constraint.CharacterWidths[index],
                    useSafeUpperBound: true,
                    mapping);

                bytes = checked(bytes
                    + (width
                        ?? throw new InvalidOperationException(
                            $"The final physical width of indexed property '{property.Name}' cannot be bounded.")));
            }

            if (bytes > MySqlIndexByteBudget.MaximumBytes)
            {
                throw new InvalidOperationException(
                    $"The final physical index on '{constraint.EntityType.DisplayName()}' requires "
                    + $"{bytes} bytes after implicit allocation, exceeding InnoDB's "
                    + $"{MySqlIndexByteBudget.MaximumBytes}-byte index-key budget.");
            }
        }
    }

    private void ApplySharedByteBudget(
        IndexConstraint constraint,
        Dictionary<IConventionProperty, VariableProperty> candidates
    )
    {
        if (GetRequiredBytes(constraint, candidates, 1) > MySqlIndexByteBudget.MaximumBytes)
        {
            throw new InvalidOperationException(
                $"The key or index on entity type '{constraint.EntityType.DisplayName()}' cannot fit "
                + $"InnoDB's {MySqlIndexByteBudget.MaximumBytes}-byte index-key budget even at the "
                + "minimum implicit lengths. Reduce explicit lengths or configure a deliberate prefix; "
                + "Doka does not truncate application facets or invent index prefixes.");
        }

        var lower = 1;
        var upper = MaximumImplicitLength;

        while (lower < upper)
        {
            var middle = lower + ((upper - lower + 1) / 2);

            if (GetRequiredBytes(constraint, candidates, middle) <= MySqlIndexByteBudget.MaximumBytes)
            {
                lower = middle;
            }
            else
            {
                upper = middle - 1;
            }
        }

        for (var index = 0; index < constraint.Properties.Count; index++)
        {
            // WHY: A prefix has a fixed index cost regardless of the column's
            // full length. A separate complete index can still constrain it.
            if (constraint.Prefixes?[index] is > 0)
            {
                continue;
            }

            var property = constraint.Properties[index];

            if (candidates.TryGetValue(property, out var variable)
                && variable.FixedMapping is null)
            {
                variable.Length = Math.Min(variable.Length, Math.Max(variable.MinimumLength, lower));
            }
        }
    }

    private long GetRequiredBytes(
        IndexConstraint constraint,
        Dictionary<IConventionProperty, VariableProperty> candidates,
        int commonLength
    )
    {
        long bytes = 0;

        for (var index = 0; index < constraint.Properties.Count; index++)
        {
            var property = constraint.Properties[index];
            var mapping = constraint.Mappings[index];

            if (candidates.TryGetValue(property, out var variable)
                && variable.FixedMapping is not null)
            {
                mapping = variable.FixedMapping;
            }
            else if (variable is not null)
            {
                if (variable.MinimumLength > variable.InitialLength)
                {
                    throw new InvalidOperationException(
                        $"The implicit indexed property '{constraint.EntityType.DisplayName()}.{property.Name}' "
                        + $"requires prefix length {variable.MinimumLength}, exceeding its conventional "
                        + $"maximum length {variable.InitialLength}. Configure the column length explicitly.");
                }

                var length = Math.Max(variable.MinimumLength, Math.Min(variable.InitialLength, commonLength));
                var providerType = variable.Mapping.Converter?.ProviderClrType ?? variable.Mapping.ClrType;
                mapping = typeMappingSource.FindMapping(
                        providerType,
                        storeTypeName: null,
                        keyOrIndex: true,
                        unicode: variable.Mapping.IsUnicode,
                        size: length,
                        rowVersion: property is
                        {
                            IsConcurrencyToken: true, ValueGenerated: ValueGenerated.OnAddOrUpdate
                        },
                        fixedLength: property.IsFixedLength(),
                        precision: property.GetPrecision(),
                        scale: property.GetScale())
                    ?? throw new InvalidOperationException(
                        $"No bounded type mapping exists for '{constraint.EntityType.DisplayName()}.{property.Name}'.");
            }

            var fullLength = MySqlIndexByteBudget.GetStoreTypeLength(property, mapping);
            var propertyBytes = MySqlIndexByteBudget.GetIndexedBytes(
                property,
                constraint.Prefixes?[index] ?? 0,
                fullLength,
                constraint.CharacterWidths[index],
                useSafeUpperBound: true,
                mapping);

            if (propertyBytes is null)
            {
                throw new InvalidOperationException(
                    $"The physical byte width of indexed property "
                    + $"'{constraint.EntityType.DisplayName()}.{property.Name}' with store type "
                    + $"'{mapping.StoreType}' cannot be bounded for implicit index allocation. "
                    + "Configure all variable column lengths explicitly.");
            }

            bytes = checked(bytes + propertyBytes.Value);
        }

        return bytes;
    }

    private static bool IsImplicitVariableLengthProperty(
        IConventionProperty property,
        RelationalTypeMapping mapping
    )
    {
        var providerType = (mapping.Converter?.ProviderClrType ?? mapping.ClrType).UnwrapNullableType();

        return (providerType == typeof(string) || providerType == typeof(byte[]))
            && mapping.Converter?.MappingHints?.Size is null
            && property.GetMaxLengthConfigurationSource() is null or ConfigurationSource.Convention
            && property.FindAnnotation(RelationalAnnotationNames.ColumnType) is null;
    }

    private static IEnumerable<IConventionProperty> GetScalarProperties(
        IConventionPropertyBase property
    )
    {
        if (property is IConventionProperty scalarProperty)
        {
            yield return scalarProperty;
            yield break;
        }

        if (property is IConventionComplexProperty complexProperty)
        {
            foreach (var nestedProperty in GetScalarProperties(complexProperty.ComplexType))
            {
                yield return nestedProperty;
            }
        }
    }

    private static IEnumerable<IConventionProperty> GetScalarProperties(
        IConventionTypeBase typeBase
    )
    {
        foreach (var property in typeBase.GetProperties())
        {
            yield return property;
        }

        foreach (var complexProperty in typeBase.GetComplexProperties())
        {
            foreach (var property in GetScalarProperties(complexProperty.ComplexType))
            {
                yield return property;
            }
        }
    }

    private sealed record IndexConstraint(
        IConventionEntityType EntityType,
        IReadOnlyList<IConventionProperty> Properties,
        IReadOnlyList<int>? Prefixes,
        RelationalTypeMapping[] Mappings,
        int?[] CharacterWidths
    );

    /// <summary>
    /// Holds one transitive implicit-column group until all index constraints are solved.
    /// InitialLength is the pre-allocation cap; MinimumLength accumulates explicit
    /// prefix minima; Length only shrinks as complete indexes are evaluated.
    /// FixedMapping and HasConceptualLength anchor a shared-column or principal
    /// contract that must be published unchanged instead of allocated.
    /// </summary>
    private sealed class VariableProperty(
        RelationalTypeMapping mapping,
        int? existingLength
    )
    {
        internal RelationalTypeMapping Mapping { get; } = mapping;

        internal int InitialLength { get; set; } = Math.Min(
            MaximumImplicitLength,
            existingLength ?? MaximumImplicitLength);

        internal RelationalTypeMapping? FixedMapping { get; set; }

        internal bool HasConceptualLength { get; set; }

        internal int MinimumLength { get; set; } = 1;

        internal int Length { get; set; } = Math.Min(MaximumImplicitLength, existingLength ?? MaximumImplicitLength);
    }
}
