namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Resolves one native write shape and binds only a bounded batch plus one pending candidate.</summary>
internal sealed partial class MySqlUpsertCommandBuilder
{
    internal const int MaximumRows = 1000;
    internal const int MaximumParameters = 65535;
    internal const long MaximumPayloadBytes = 4L * 1024 * 1024;

    private readonly Column[] _columns;
    private readonly long _fixedPayloadBytes;
    private readonly string _header;
    private readonly ReadOnlyGuard[] _readOnlyGuards;
    private readonly string _suffix;

#pragma warning disable EF1001 // Nonmutating entry lookup preserves temporary values without Attach or DetectChanges; qualified by UPSERT tests.
    private IStateManager? _stateManager;
#pragma warning restore EF1001

    private MySqlUpsertCommandBuilder(
        MySqlDirectWriteTarget target,
        Column[] columns,
        ReadOnlyGuard[] readOnlyGuards,
        string header,
        string suffix
    )
    {
        Target = target;
        _columns = columns;
        _readOnlyGuards = readOnlyGuards;
        _header = header;
        _suffix = suffix;

        // WHY: This is a conservative provider budget, not a discovered server max_allowed_packet.
        // It includes SQL and protocol allowance; arbitrary interceptor rewrites remain caller-owned.
        _fixedPayloadBytes = checked(Encoding.UTF8.GetByteCount(header)
            + Encoding.UTF8.GetByteCount(suffix)
            + 64L);
    }

    /// <summary>Gets the invocation's immutable mapped target.</summary>
    internal MySqlDirectWriteTarget Target { get; }

    /// <summary>Validates the selected model and resolves its complete physical insert/update shape once.</summary>
    internal static MySqlUpsertCommandBuilder Create(
        IEntityType entityType,
        IReadOnlyList<string> updatePropertyNames,
        EngineProfile engine,
        ISqlGenerationHelper sql
    )
    {
        ArgumentNullException.ThrowIfNull(updatePropertyNames);

        var target = MySqlDirectWriteTarget.Resolve(entityType);
        ValidateModel(target);

        if (updatePropertyNames.Count == 0)
        {
            throw new ArgumentException("UPSERT requires a nonempty explicit update selection.",
                nameof(updatePropertyNames));
        }

        var updateProperties = new HashSet<IProperty>();

        // WHY: Index the caller's read-only list without an interface enumerator;
        // validation still stops at the first invalid property name.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < updatePropertyNames.Count; index++)
        {
            var name = updatePropertyNames[index];
            var property = name is null
                ? null
                : entityType.FindProperty(name);

            if (property is null
                || !updateProperties.Add(property))
            {
                throw new ArgumentException("UPSERT update properties must be known, unique EF property names.",
                    nameof(updatePropertyNames));
            }

            var column = target.RequireColumn(property);

            if (property.IsPrimaryKey()
                || column.ComputedColumnSql is not null
                || property.GetAfterSaveBehavior() != PropertySaveBehavior.Save)
            {
                throw new ArgumentException("UPSERT cannot update keys, computed, or read-only properties.",
                    nameof(updatePropertyNames));
            }
        }

        var columns = new List<Column>();
        var readOnlyGuards = new List<ReadOnlyGuard>();
        var physicalColumns = new HashSet<IColumn>();

        foreach (var property in entityType.GetProperties())
        {
            var column = target.RequireColumn(property);

            if (!physicalColumns.Add(column))
            {
                throw new InvalidOperationException(
                    "UPSERT cannot assign multiple input properties to one physical column.");
            }

            if (column.ComputedColumnSql is not null)
            {
                if (property.GetBeforeSaveBehavior() == PropertySaveBehavior.Throw)
                {
                    readOnlyGuards.Add(new ReadOnlyGuard(property,
                        property.IsShadowProperty()
                            ? null
                            : property.GetGetter(),
                        property.GetValueComparer(),
                        property.Sentinel));
                }

                continue;
            }

            var selectedForUpdate = updateProperties.Contains(property);

            if (property.GetBeforeSaveBehavior() == PropertySaveBehavior.Ignore
                && !selectedForUpdate)
            {
                continue;
            }

            var generatedOnAdd = (property.ValueGenerated & ValueGenerated.OnAdd) != 0;
            var hasServerDefault = column.DefaultValueSql is not null
                || column.TryGetDefaultValue(out _)
                || property.GetMySqlValueGenerationStrategy() == MySqlValueGenerationStrategy.AutoIncrement;

            if (property.IsShadowProperty()
                && !(generatedOnAdd && hasServerDefault))
            {
                throw new InvalidOperationException(
                    "UPSERT requires an explicit CLR value or a qualified generated server default for every input property.");
            }

            var mapping = column.FindColumnMapping(entityType)
                ?? throw new InvalidOperationException("UPSERT requires an exact physical column mapping.");

            columns.Add(new Column(
                property,
                column,
                mapping.TypeMapping,
                property.IsShadowProperty()
                    ? null
                    : property.GetGetter(),
                property.GetValueComparer(),
                property.Sentinel,
                property.GetBeforeSaveBehavior(),
                property.ValueGenerated,
                generatedOnAdd && hasServerDefault,
                selectedForUpdate));
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException("UPSERT has no writable mapped input columns.");
        }

        var values = columns.ToArray();
        var header = new StringBuilder("INSERT INTO ")
            .Append(sql.DelimitIdentifier(target.Table.Name, target.Table.Schema))
            .Append(" (");

        for (var index = 0; index < values.Length; index++)
        {
            if (index != 0)
            {
                header.Append(", ");
            }

            header.Append(sql.DelimitIdentifier(values[index].Store.Name));
        }

        header.Append(") VALUES\n");
        var suffix = new StringBuilder();
        var rowAlias = string.Equals(target.Table.Name, "__doka_incoming", StringComparison.OrdinalIgnoreCase)
            ? "__doka_incoming_1"
            : "__doka_incoming";

        if (engine.Has(EngineCapability.InsertRowAlias))
        {
            suffix
                .Append(" AS ")
                .Append(sql.DelimitIdentifier(rowAlias));
        }
        else if (!engine.Has(EngineCapability.OnDuplicateKeyValuesFunction))
        {
            throw new InvalidOperationException(
                "The configured engine does not support the qualified UPSERT input-reference syntax.");
        }

        suffix.Append("\nON DUPLICATE KEY UPDATE ");
        var firstUpdate = true;

        foreach (var column in values)
        {
            if (!column.Update)
            {
                continue;
            }

            if (!firstUpdate)
            {
                suffix.Append(", ");
            }

            firstUpdate = false;
            var identifier = sql.DelimitIdentifier(column.Store.Name);
            suffix
                .Append(identifier)
                .Append(" = ");

            if (engine.Has(EngineCapability.InsertRowAlias))
            {
                suffix
                    .Append(sql.DelimitIdentifier(rowAlias))
                    .Append('.')
                    .Append(identifier);
            }
            else
            {
                suffix
                    .Append("VALUES(")
                    .Append(identifier)
                    .Append(')');
            }
        }

        suffix.Append(sql.StatementTerminator);

        return new MySqlUpsertCommandBuilder(target,
            values,
            // WHY: Freeze the computed-property guard array when the command descriptor is created.
            // ReSharper disable once UseCollectionExpression
            readOnlyGuards.ToArray(),
            header.ToString(),
            suffix.ToString());
    }

    /// <summary>Rejects mappings whose write contract cannot be represented by one direct UPSERT target.</summary>
    private static void ValidateModel(
        MySqlDirectWriteTarget target
    )
    {
        var entity = target.EntityType;

        if (entity.BaseType is not null
            || entity
                .GetDerivedTypes()
                .Any()
            || entity.IsOwned()
            || entity
                .GetComplexProperties()
                .Any()
            || entity
                .GetNavigations()
                .Any(navigation => navigation.ForeignKey.IsOwnership && !navigation.IsOnDependent)
            || entity
                .GetProperties()
                .Any(property => property.IsConcurrencyToken))
        {
            throw new InvalidOperationException(
                "UPSERT does not support inheritance, owned/complex aggregates, or optimistic-concurrency models.");
        }

        // WHY: Reject the first unsupported shared-table or stored-procedure mapping before command construction.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var mapping in target.Table.EntityTypeMappings)
        {
            if (!ReferenceEquals(mapping.TypeBase, entity)
                || mapping.InsertStoredProcedureMapping is not null
                || mapping.UpdateStoredProcedureMapping is not null
                || mapping.DeleteStoredProcedureMapping is not null)
            {
                throw new InvalidOperationException(
                    "UPSERT does not support shared-table or stored-procedure write mappings.");
            }
        }

        var storageEngine = target.Table.FindAnnotation(MySqlAnnotationNames.StorageEngine)
                ?.Value as string
            ?? entity.FindAnnotation(MySqlAnnotationNames.StorageEngine)
                ?.Value as string;

        if (storageEngine is not null
            && !string.Equals(storageEngine, "InnoDB", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("UPSERT requires a transactional InnoDB target.");
        }
    }

    /// <summary>Retains the resolved getter, sentinel comparer and physical mapping for one input column.</summary>
    private sealed record Column(
        IProperty Property,
        IColumn Store,
        RelationalTypeMapping Mapping,
        IClrPropertyGetter? Getter,
        ValueComparer Comparer,
        object? Sentinel,
        PropertySaveBehavior BeforeSave,
        ValueGenerated Generation,
        bool ServerDefault,
        bool Update
    );

    /// <summary>Checks an explicitly immutable generated property without adding it to the insert column list.</summary>
    private sealed record ReadOnlyGuard(
        IProperty Property,
        IClrPropertyGetter? Getter,
        ValueComparer Comparer,
        object? Sentinel
    );
}
