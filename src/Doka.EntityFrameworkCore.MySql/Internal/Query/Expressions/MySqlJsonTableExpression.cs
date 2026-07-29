namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Represents a MySQL / MariaDB <c>JSON_TABLE</c> table-valued function call in the SQL tree.
/// <c>JSON_TABLE</c> turns a JSON document into a rowset, letting EF Core compose
/// <see cref="JsonScalarExpression"/>-style scalar projections AND owned-JSON entity sets into
/// the relational query tree the same way SQL Server uses <c>OPENJSON</c>. The expression carries
/// the source JSON argument, the JSON path that selects the row source, and the column
/// definitions (<see cref="ColumnInfo"/>) that fill the rowset.
/// </summary>
/// <remarks>
/// Cross-engine: MySQL 8.0+ and MariaDB 10.6+ implement the same <c>JSON_TABLE</c> grammar
/// (`JSON_TABLE(doc, path COLUMNS (col type PATH '$.x', key FOR ORDINALITY, nested JSON PATH '$.n'))`),
/// so a single SQL-emission path covers both engines. The translator is the LINQ-side
/// counterpart that turns <c>SqlExpression</c> primitive-collection sources and
/// <see cref="JsonQueryExpression"/> owned-entity sources into instances of this expression.
/// </remarks>
internal sealed class MySqlJsonTableExpression : TableValuedFunctionExpression
{
    /// <summary>
    /// Describes one column inside the <c>COLUMNS</c> clause of a <c>JSON_TABLE</c> call.
    /// <see cref="Name"/> becomes the column alias; <see cref="TypeMapping"/> drives the
    /// <c>type</c> token; <see cref="Path"/> renders as <c>PATH '$.x'</c> when non-null;
    /// <see cref="AsJson"/> forces the column type to <c>JSON</c> so nested objects flow
    /// through as raw JSON (the JSON_TABLE equivalent of OPENJSON's <c>AS JSON</c> suffix);
    /// <see cref="ForOrdinality"/> emits <c>FOR ORDINALITY</c> instead of a type + path
    /// (matches OPENJSON's <c>key</c> column for primitive collection ordering).
    /// </summary>
    public readonly record struct ColumnInfo(
        string Name,
        RelationalTypeMapping TypeMapping,
        IReadOnlyList<PathSegment>? Path = null,
        bool AsJson = false,
        bool ForOrdinality = false
    );

    public SqlExpression JsonExpression => Arguments[0];

    public IReadOnlyList<PathSegment>? Path { get; }

    public IReadOnlyList<ColumnInfo>? ColumnInfos { get; }

    public MySqlJsonTableExpression(
        string alias,
        SqlExpression jsonExpression,
        IReadOnlyList<PathSegment>? path = null,
        IReadOnlyList<ColumnInfo>? columnInfos = null
    ) : base(alias, "JSON_TABLE", schema: null, builtIn: true, arguments: [jsonExpression])
    {
        if (columnInfos is { Count: 0 })
        {
            columnInfos = null;
        }

        Path = path;
        ColumnInfos = columnInfos;
    }

    private MySqlJsonTableExpression(
        string alias,
        SqlExpression jsonExpression,
        IReadOnlyList<PathSegment>? path,
        IReadOnlyList<ColumnInfo>? columnInfos,
        IReadOnlyDictionary<string, IAnnotation>? annotations
    ) : base(alias, "JSON_TABLE", schema: null, builtIn: true, arguments: [jsonExpression], annotations: annotations)
    {
        if (columnInfos is { Count: 0 })
        {
            columnInfos = null;
        }

        Path = path;
        ColumnInfos = columnInfos;
    }

    protected override Expression VisitChildren(
        ExpressionVisitor visitor
    )
    {
        var jsonExpression = (SqlExpression)visitor.Visit(JsonExpression);

        PathSegment[]? newPath = null;

        if (Path is not null)
        {
            for (var i = 0; i < Path.Count; i++)
            {
                var segment = Path[i];

                if (segment.PropertyName is not null)
                {
                    newPath?[i] = segment;

                    continue;
                }

                var visited = (SqlExpression?)visitor.Visit(segment.ArrayIndex);

                if (visited is not null
                    && !ReferenceEquals(visited, segment.ArrayIndex))
                {
                    if (newPath is null)
                    {
                        newPath = new PathSegment[Path.Count];
                        for (var j = 0; j < i; j++)
                        {
                            newPath[j] = Path[j];
                        }
                    }

                    newPath[i] = new PathSegment(visited);
                }
                else
                {
                    newPath?[i] = segment;
                }
            }
        }

        return Update(jsonExpression, (IReadOnlyList<PathSegment>?)newPath ?? Path, ColumnInfos);
    }

    public MySqlJsonTableExpression Update(
        SqlExpression jsonExpression,
        IReadOnlyList<PathSegment>? path,
        IReadOnlyList<ColumnInfo>? columnInfos
    )
    {
        if (columnInfos is { Count: 0 })
        {
            columnInfos = null;
        }

        var jsonChanged = !ReferenceEquals(jsonExpression, JsonExpression);
        var pathChanged = !ReferenceEquals(path, Path) && (path is null || Path is null || !path.SequenceEqual(Path));
        var columnsChanged = !ReferenceEquals(columnInfos, ColumnInfos)
            && (columnInfos is null || ColumnInfos is null || !columnInfos.SequenceEqual(ColumnInfos));

        return jsonChanged || pathChanged || columnsChanged
            ? new MySqlJsonTableExpression(Alias!, jsonExpression, path, columnInfos)
            : this;
    }

    public override TableExpressionBase Clone(
        string? alias,
        ExpressionVisitor cloningExpressionVisitor
    )
    {
        var jsonExpression = (SqlExpression)cloningExpressionVisitor.Visit(JsonExpression);
        var clone = new MySqlJsonTableExpression(alias ?? Alias!, jsonExpression, Path, ColumnInfos);

        foreach (var annotation in GetAnnotations())
        {
            clone.AddAnnotation(annotation.Name, annotation.Value);
        }

        return clone;
    }

    public override MySqlJsonTableExpression WithAlias(
        string newAlias
    ) => new(newAlias, JsonExpression, Path, ColumnInfos);

    protected override TableValuedFunctionExpression WithAnnotations(
        IReadOnlyDictionary<string, IAnnotation> annotations
    ) => new MySqlJsonTableExpression(Alias!, JsonExpression, Path, ColumnInfos, annotations);

    // Quoting is the precompiled-query support path. Spec tests do not exercise precompilation;
    // throw a clear message rather than silently round-tripping through the base TVFE shape
    // (which would lose Path + ColumnInfos and emit a meaningless JSON_TABLE() call).
    public override Expression Quote() => throw new NotSupportedException(
        "Quoting a MySqlJsonTableExpression is not supported; precompiled-query support for "
        + "JSON_TABLE has not been implemented yet.");

    protected override void Print(
        ExpressionPrinter expressionPrinter
    )
    {
        expressionPrinter.Append("JSON_TABLE(");
        expressionPrinter.Visit(JsonExpression);

        if (Path is { Count: > 0 })
        {
            expressionPrinter.Append(", '");
            AppendPath(expressionPrinter, Path);
            expressionPrinter.Append("'");
        }

        if (ColumnInfos is not null)
        {
            expressionPrinter.Append(" COLUMNS(");

            for (var i = 0; i < ColumnInfos.Count; i++)
            {
                if (i > 0)
                {
                    expressionPrinter.Append(", ");
                }

                var column = ColumnInfos[i];
                expressionPrinter.Append(column.Name);

                if (column.ForOrdinality)
                {
                    expressionPrinter.Append(" FOR ORDINALITY");
                    continue;
                }

                expressionPrinter
                    .Append(" ")
                    .Append(column.AsJson ? "JSON" : column.TypeMapping.StoreType);

                if (column.Path is { Count: > 0 })
                {
                    expressionPrinter.Append(" PATH '");
                    AppendPath(expressionPrinter, column.Path);
                    expressionPrinter.Append("'");
                }
            }

            expressionPrinter.Append(")");
        }

        expressionPrinter
            .Append(") AS ")
            .Append(Alias ?? string.Empty);
    }

    private static void AppendPath(
        ExpressionPrinter expressionPrinter,
        IReadOnlyList<PathSegment> path
    )
    {
        expressionPrinter.Append("$");

        foreach (var segment in path)
        {
            if (segment.PropertyName is not null)
            {
                expressionPrinter
                    .Append(".")
                    .Append(segment.PropertyName);
                continue;
            }

            expressionPrinter.Append("[");

            if (segment.ArrayIndex is not null)
            {
                expressionPrinter.Visit(segment.ArrayIndex);
            }

            expressionPrinter.Append("]");
        }
    }
}
