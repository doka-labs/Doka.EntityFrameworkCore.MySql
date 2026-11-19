namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Resolves a direct operation's physical target without inferring a store from its CLR type.</summary>
internal sealed class MySqlDirectWriteTarget
{
    private MySqlDirectWriteTarget(
        IEntityType entityType,
        ITable table
    )
    {
        EntityType = entityType;
        Table = table;
    }

    /// <summary>Gets the exact mapped entity identity.</summary>
    internal IEntityType EntityType { get; }

    /// <summary>Gets the single physical table consumed by the operation.</summary>
    internal ITable Table { get; }

    /// <summary>Requires one keyed physical table, preserving operation-specific mapping checks at its consumer.</summary>
    internal static MySqlDirectWriteTarget Resolve(
        IEntityType entityType
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        if (entityType.FindPrimaryKey() is null
            || entityType.ClrType.IsAbstract)
        {
            throw new InvalidOperationException("Direct writes require a concrete keyed physical table target.");
        }

        ITable? table = null;

        // WHY: TPH can expose multiple mappings to the same table. Physical identity, rather
        // than mapping count, also lets each operation apply its own shared-mapping contract.
        foreach (var mapping in entityType.GetTableMappings())
        {
            if (table is not null
                && !ReferenceEquals(table, mapping.Table))
            {
                throw new InvalidOperationException(
                    "Direct writes cannot target an entity mapped to multiple physical tables.");
            }

            table = mapping.Table;
        }

        if (table is null)
        {
            throw new InvalidOperationException("Direct writes require a physical table mapping.");
        }

        return new MySqlDirectWriteTarget(entityType, table);
    }

    /// <summary>Finds the property's actual column on this target, including field and shadow properties.</summary>
    internal IColumn RequireColumn(
        IPropertyBase property
    )
    {
        ArgumentNullException.ThrowIfNull(property);

        // WHY: EF11 resolves columns by IPropertyBase identity, including store-name
        // overrides and shadow/field mappings; CLR names cannot identify a physical column.
        return Table.FindColumn(property)
            ?? throw new InvalidOperationException("The selected property is not mapped to the direct-write target.");
    }
}
