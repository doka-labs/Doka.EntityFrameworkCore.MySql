using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// MySQL / MariaDB queryable-method translator. Overrides the two hook points that turn a JSON
/// source into a relational rowset (<see cref="TranslatePrimitiveCollection"/> for raw / column
/// primitive collections, <see cref="TransformJsonQueryToTable"/> for <c>ToJson()</c>-owned
/// entity collections) so the cross-engine JSON_TABLE grammar gets emitted in place of the
/// "no LINQ translation" fallback the base provider would otherwise hit.
/// </summary>
internal sealed class
    MySqlQueryableMethodTranslatingExpressionVisitor : RelationalQueryableMethodTranslatingExpressionVisitor
{
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly RelationalQueryCompilationContext _queryCompilationContext;

    public MySqlQueryableMethodTranslatingExpressionVisitor(
        QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
        RelationalQueryableMethodTranslatingExpressionVisitorDependencies relationalDependencies,
        RelationalQueryCompilationContext queryCompilationContext
    ) : base(dependencies, relationalDependencies, queryCompilationContext)
    {
        _typeMappingSource = relationalDependencies.TypeMappingSource;
        _sqlExpressionFactory = relationalDependencies.SqlExpressionFactory;
        _queryCompilationContext = queryCompilationContext;
    }

    private MySqlQueryableMethodTranslatingExpressionVisitor(
        MySqlQueryableMethodTranslatingExpressionVisitor parentVisitor
    ) : base(parentVisitor)
    {
        _typeMappingSource = parentVisitor._typeMappingSource;
        _sqlExpressionFactory = parentVisitor._sqlExpressionFactory;
        _queryCompilationContext = parentVisitor._queryCompilationContext;
    }

    protected override QueryableMethodTranslatingExpressionVisitor CreateSubqueryVisitor() =>
        new MySqlQueryableMethodTranslatingExpressionVisitor(this);

    /// <summary>
    /// Tells EF Core's translator that a <see cref="SelectExpression"/> whose row source is a
    /// <see cref="MySqlJsonTableExpression"/> ordered by its synthetic <c>key</c> ordinality
    /// column is "naturally ordered" -- the ordering reflects the inherent row order JSON_TABLE
    /// produces (1, 2, 3, ...), not an explicit user OrderBy. Without this hook EF Core fires
    /// <c>DistinctAfterOrderByWithoutRowLimitingOperatorWarning</c> on every Distinct over a
    /// JSON_TABLE-derived collection because it cannot tell the ordering apart from a user
    /// `.OrderBy()` whose meaning would be lost across the Distinct. Mirrors the SqlServer
    /// <see href="https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.SqlServer/Query/Internal/SqlServerQueryableMethodTranslatingExpressionVisitor.cs">
    /// OPENJSON</see> recognition shape; differs only in not requiring a <c>Convert</c> wrapper
    /// around the key column (our key is already typed <see cref="int"/>; SqlServer's OPENJSON
    /// key is string and gets converted to int for ordering).
    /// </summary>
    protected override bool IsNaturallyOrdered(
        SelectExpression selectExpression
    )
    {
        if (selectExpression.Tables is not [MySqlJsonTableExpression jsonTable, ..])
        {
            return false;
        }

        if (selectExpression.Orderings is not [{ IsAscending: true, Expression: ColumnExpression { Name: "key" } orderingColumn }])
        {
            return false;
        }

        return orderingColumn.TableAlias == jsonTable.Alias;
    }

    /// <summary>
    /// Translates a SQL expression that holds a primitive collection (an <c>int[]</c> column,
    /// an inline array constant, etc.) into a JSON_TABLE call exposing two columns: <c>value</c>
    /// (the element value) and <c>key</c> (its 1-based ordinality position).
    /// </summary>
    protected override ShapedQueryExpression? TranslatePrimitiveCollection(
        SqlExpression sqlExpression,
        IProperty? property,
        string tableAlias
    )
    {
        var elementTypeMapping = (RelationalTypeMapping?)sqlExpression.TypeMapping?.ElementTypeMapping;

        var columns = new List<MySqlJsonTableExpression.ColumnInfo>(2);

        if (elementTypeMapping is not null)
        {
            columns.Add(
                new MySqlJsonTableExpression.ColumnInfo(
                    Name: "value",
                    TypeMapping: elementTypeMapping,
                    Path: [],
                    AsJson: false,
                    ForOrdinality: false));
        }

        // Key ordinality column matches SqlServer OPENJSON's "key" column semantics. Type mapping
        // is integer because JSON_TABLE FOR ORDINALITY always returns BIGINT-shaped values.
        var keyTypeMapping = _typeMappingSource.FindMapping(typeof(int))!;
        columns.Add(
            new MySqlJsonTableExpression.ColumnInfo(
                Name: "key",
                TypeMapping: keyTypeMapping,
                Path: null,
                AsJson: false,
                ForOrdinality: true));

        var jsonTableExpression = new MySqlJsonTableExpression(
            alias: tableAlias,
            jsonExpression: sqlExpression,
            path: null,
            columnInfos: columns);

        var sequenceType = TryGetSequenceType(sqlExpression.Type)
            ?? throw new InvalidOperationException(
                $"Primitive-collection translation requires a sequence type; '{sqlExpression.Type}' is not enumerable.");
        var isNullable = property?.GetElementType()?.IsNullable
            ?? IsNullableType(sequenceType);

        var unwrapped = UnwrapNullableType(sequenceType);
        var valueColumn = new ColumnExpression(
            name: "value",
            tableAlias: tableAlias,
            type: unwrapped,
            typeMapping: elementTypeMapping ?? _typeMappingSource.FindMapping(unwrapped)!,
            nullable: isNullable);

        var keyColumn = new ColumnExpression(
            name: "key",
            tableAlias: tableAlias,
            type: typeof(int),
            typeMapping: keyTypeMapping,
            nullable: false);

        var identifier = new List<(ColumnExpression Column, ValueComparer Comparer)>(1)
        {
            (keyColumn, keyTypeMapping.Comparer),
        };

        var tables = new List<TableExpressionBase>(1) { jsonTableExpression };

#pragma warning disable EF1001 // SelectExpression's table-list ctor is EF Core internal; the LINQ -> JSON_TABLE translation needs it.
        var select = new SelectExpression(
            tables: tables,
            projection: valueColumn,
            identifier: identifier,
            sqlAliasManager: _queryCompilationContext.SqlAliasManager);
#pragma warning restore EF1001

        select.AppendOrdering(new OrderingExpression(keyColumn, ascending: true));

        Expression shaper = new ProjectionBindingExpression(select, new ProjectionMember(), MakeNullable(sequenceType));

        if (shaper.Type != sequenceType)
        {
            shaper = Expression.Convert(shaper, sequenceType);
        }

        return new ShapedQueryExpression(select, shaper);
    }

    /// <summary>
    /// Turns a <see cref="JsonQueryExpression"/> (an owned-JSON entity collection) into a
    /// JSON_TABLE rowset. One typed column per primitive property (<c>PATH '$.PropertyName'</c>),
    /// one <c>JSON</c> column per nested complex/owned navigation (<c>JSON PATH '$.Nav'</c>),
    /// plus a <c>key FOR ORDINALITY</c> column for stable ordering. The base provider falls back
    /// to <c>JsonQueryLinqOperatorsNotSupported</c>; this override is what lets owned-JSON
    /// collections survive LINQ composition (`.Where`, `.Select`, `.OrderBy`, `.Count`, ...).
    /// </summary>
    protected override ShapedQueryExpression? TransformJsonQueryToTable(
        JsonQueryExpression jsonQueryExpression
    )
    {
        // JSON_TABLE's row-source path must be a literal SQL string. When the LINQ path carries
        // a non-constant array index (parameter ElementAt, dynamic expression), the path would
        // need to splice the value through CONCAT(...) which JSON_TABLE rejects. Bail out so
        // EF Core's standard "LINQ expression could not be translated" error surfaces in place
        // of an invalid-SQL MySqlException.
        if (HasDynamicArrayIndex(jsonQueryExpression.Path))
        {
            return null;
        }

        var structuralType = jsonQueryExpression.StructuralType;
        var lastNamedSegment = jsonQueryExpression.Path.LastOrDefault(static segment => segment.PropertyName is not null);
        var aliasHint = lastNamedSegment.PropertyName ?? jsonQueryExpression.JsonColumn.Name;
        var alias = _queryCompilationContext.SqlAliasManager.GenerateTableAlias(aliasHint);

        var columns = new List<MySqlJsonTableExpression.ColumnInfo>();

        foreach (var prop in structuralType.GetPropertiesInHierarchy())
        {
            var jsonPropertyName = prop.GetJsonPropertyName();
            if (jsonPropertyName is null)
            {
                continue;
            }

            var typeMapping = prop.GetRelationalTypeMapping();
            var asJson = typeMapping.ElementTypeMapping is not null;

            columns.Add(
                new MySqlJsonTableExpression.ColumnInfo(
                    Name: jsonPropertyName,
                    TypeMapping: typeMapping,
                    Path: [new PathSegment(jsonPropertyName)],
                    AsJson: asJson,
                    ForOrdinality: false));
        }

        var containerColumnName = structuralType.GetContainerColumnName()
            ?? throw new UnreachableException("Owned-JSON structural type must declare a container column.");

        var containerColumn = structuralType
            .ContainingEntityType.GetTableMappings()
            .SelectMany(static m => m.Table.Columns)
            .Single(c => c.Name == containerColumnName);

        var nestedJsonPropertyNames = structuralType switch
        {
            IEntityType entityType => entityType
                .GetNavigationsInHierarchy()
                .Where(n => n.ForeignKey.IsOwnership
                    && n.TargetEntityType.IsMappedToJson()
                    && n.ForeignKey.PrincipalToDependent == n)
                .Select(n => n.TargetEntityType.GetJsonPropertyName()
                    ?? throw new UnreachableException(
                        "Owned-JSON navigation target must declare a JSON property name.")),
            IComplexType complexType => complexType
                .GetComplexProperties()
                .Select(p => p.ComplexType.GetJsonPropertyName()
                    ?? throw new UnreachableException(
                        "Owned-JSON complex sub-type must declare a JSON property name.")),
            _ => throw new UnreachableException(
                "JsonQueryExpression.StructuralType must be IEntityType or IComplexType."),
        };

        foreach (var name in nestedJsonPropertyNames)
        {
            columns.Add(
                new MySqlJsonTableExpression.ColumnInfo(
                    Name: name,
                    TypeMapping: containerColumn.StoreTypeMapping,
                    Path: [new PathSegment(name)],
                    AsJson: true,
                    ForOrdinality: false));
        }

        // Ordinality column for stable ordering across the rowset. JSON_TABLE FOR ORDINALITY is
        // 1-based; the spec test corpus does not depend on a particular start index, only on
        // deterministic ordering.
        var keyTypeMapping = _typeMappingSource.FindMapping(typeof(int))!;
        columns.Add(
            new MySqlJsonTableExpression.ColumnInfo(
                Name: "key",
                TypeMapping: keyTypeMapping,
                Path: null,
                AsJson: false,
                ForOrdinality: true));

        var jsonTableExpression = new MySqlJsonTableExpression(
            alias: alias,
            jsonExpression: jsonQueryExpression.JsonColumn,
            path: jsonQueryExpression.Path,
            columnInfos: columns);

#pragma warning disable EF1001 // RelationalQueryableMethodTranslatingExpressionVisitor.CreateSelect(JsonQueryExpression, ...) is EF Core internal; the JSON_TABLE shaper needs it.
        var select = CreateSelect(
            jsonQueryExpression,
            jsonTableExpression,
            identifierColumnName: "key",
            identifierColumnType: typeof(int),
            identifierColumnTypeMapping: keyTypeMapping);
#pragma warning restore EF1001

        select.AppendOrdering(
            new OrderingExpression(
                select.CreateColumnExpression(
                    jsonTableExpression,
                    "key",
                    typeof(int),
                    keyTypeMapping,
                    columnNullable: false),
                ascending: true));

        return new ShapedQueryExpression(
            select,
            new RelationalStructuralTypeShaperExpression(
                jsonQueryExpression.StructuralType,
                new ProjectionBindingExpression(select, new ProjectionMember(), typeof(ValueBuffer)),
                nullable: false));
    }

    // Mirrors System.SharedTypeExtensions.TryGetSequenceType (EF Core internal); inlined so the
    // translator does not depend on the internal extension surface.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification =
            "The Types passed here come from EF Core's primitive-collection translation pipeline and are well-known IEnumerable<T>/T[] shapes preserved by the runtime.")]
    private static Type? TryGetSequenceType(
        Type type
    )
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return type.GenericTypeArguments[0];
        }

        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType
                && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return iface.GenericTypeArguments[0];
            }
        }

        return null;
    }

    private static Type UnwrapNullableType(
        Type type
    ) => Nullable.GetUnderlyingType(type) ?? type;

    private static bool IsNullableType(
        Type type
    ) => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification =
            "Nullable<T> generic instantiation is preserved by the runtime; EF Core relies on this same construction throughout the query pipeline.")]
    private static Type MakeNullable(
        Type type
    ) => type.IsValueType && Nullable.GetUnderlyingType(type) is null ? typeof(Nullable<>).MakeGenericType(type) : type;

    private static bool HasDynamicArrayIndex(
        IReadOnlyList<PathSegment> path
    )
    {
        foreach (var segment in path)
        {
            if (segment.ArrayIndex is not null and not SqlConstantExpression)
            {
                return true;
            }
        }

        return false;
    }
}
