namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Proves finite scalar text growth before constructing recursive frontier type mappings.</summary>
internal static class MySqlRecursiveTextCapacity
{
    /// <summary>Widens only proved flat projections and returns the mappings the retranslated step must retain.</summary>
    public static Dictionary<ProjectionMember, RelationalTypeMapping>? WidenAnchor(
        ShapedQueryExpression anchor,
        ShapedQueryExpression step,
        ShapedQueryExpression frontier,
        SqlExpression maximumDepth,
        int bindingId
    )
    {
        if (maximumDepth is not SqlConstantExpression { Value: int depth }
            || anchor.ShaperExpression is StructuralTypeShaperExpression
            || !TryGetProjectionMap(anchor, out var anchorMap)
            || !TryGetProjectionMap(step, out var stepMap)
            || !TryGetProjectionMap(frontier, out var frontierMap))
        {
            return null;
        }

        var leaf = FrontierFinder.Find(step.QueryExpression, bindingId);

        if (leaf is null)
        {
            return null;
        }

        Dictionary<ProjectionMember, RelationalTypeMapping>? mappings = null;
        var select = (SelectExpression)anchor.QueryExpression;

        foreach (var (member, expression) in anchorMap.ToArray())
        {
            if (expression is not SqlExpression { Type: { } type, TypeMapping: { } mapping } scalar
                || type != typeof(string)
                || mapping.ClrType != typeof(string)
                || mapping.Converter is not null
                || !stepMap.TryGetValue(member, out var stepExpression)
                || stepExpression is not SqlExpression value
                || !frontierMap.TryGetValue(member, out var frontierExpression)
                || frontierExpression is not ColumnExpression frontierColumn
                || scalar is not CollateExpression known
                || !TryCharacterSet(known.Collation, out var charSet)
                || !TryMaximumCharacters(known.Operand, select, out var initialCapacity)
                || !TryLinearGrowth(value, leaf.Alias!, frontierColumn.Name, known.Collation, out var growth))
            {
                continue;
            }

            int capacity;

            try
            {
                // WHY: This is a proved bound from actual source capacity and selected iterations,
                // not a guessed default length. CHAR(1) is the minimum needed for an empty bound.
                capacity = Math.Max(1, checked(initialCapacity + checked(growth * depth)));
            }
            catch (OverflowException error)
            {
                throw new InvalidOperationException(
                    "The proved recursive text capacity exceeds the native bounded mapping.",
                    error);
            }

            var widened = mapping.WithStoreTypeAndSize(
                "char(" + capacity.ToString(CultureInfo.InvariantCulture) + ")",
                capacity);

            // WHY: Keep the original comparison inside the cast as well as after it. Stripping
            // that operation would silently repair an invalid source-charset/collation pairing
            // by converting its charset before the database can validate the original expression.
            anchorMap[member] = new MySqlRecursiveTextCastExpression(known,
                capacity,
                charSet,
                known.Collation,
                widened);

            (mappings ??= []).Add(member, widened);
        }

        if (mappings is not null)
        {
            select.ReplaceProjection(anchorMap);
        }

        return mappings;
    }

    /// <summary>Preserves the widened mapping on the actual step and therefore on later parameters and comparisons.</summary>
    public static void ApplyStepMappings(
        ShapedQueryExpression step,
        Dictionary<ProjectionMember, RelationalTypeMapping> mappings
    )
    {
        if (!TryGetProjectionMap(step, out var projections))
        {
            throw new InvalidOperationException("A bounded recursive text projection lost its flat SQL shape.");
        }

        foreach (var (member, mapping) in mappings)
        {
            if (!projections.TryGetValue(member, out var expression)
                || expression is not SqlExpression scalar)
            {
                throw new InvalidOperationException("A bounded recursive text projection changed its member identity.");
            }

            projections[member] = RebindMappedProjection(scalar, mapping);
        }

        ((SelectExpression)step.QueryExpression).ReplaceProjection(projections);
    }

    /// <summary>Replaces an existing result mapping after the finite-width proof instead of invoking default type inference.</summary>
    private static SqlExpression RebindMappedProjection(
        SqlExpression expression,
        RelationalTypeMapping mapping
    ) => expression switch
    {
        // WHY: EF's SqlExpressionFactory.ApplyTypeMapping deliberately returns already mapped
        // expressions unchanged. These proved scalar forms need their actual widened result mapping.
        CollateExpression collate => new CollateExpression(RebindMappedProjection(collate.Operand, mapping),
            collate.Collation),
        SqlBinaryExpression binary => new SqlBinaryExpression(binary.OperatorType,
            binary.Left,
            binary.Right,
            binary.Type,
            mapping),
        SqlFunctionExpression function => function.ApplyTypeMapping(mapping),
        ColumnExpression column => column.ApplyTypeMapping(mapping),
        _ => throw new InvalidOperationException(
            "A proved recursive text projection changed its scalar expression kind."),
    };

    /// <summary>Checks exact once-frontier concatenation with bounded literal growth; arbitrary functions remain unqualified.</summary>
    public static bool TryLinearGrowth(
        SqlExpression expression,
        string frontierAlias,
        string columnName,
        string? collation,
        out int growth
    )
    {
        try
        {
            var result = CountGrowth(expression, frontierAlias, columnName, collation);
            growth = result.Characters;

            return result is { Valid: true, Frontiers: 1 };
        }
        catch (OverflowException)
        {
            growth = 0;

            return false;
        }
    }

    /// <summary>Tests an actual finite anchor cast against the same step bound after SQL tree rewriting.</summary>
    public static bool IsProvedCastGrowth(
        SelectExpression anchor,
        SqlExpression anchorValue,
        SqlExpression stepValue,
        MySqlRecursiveCteReferenceExpression frontier,
        string columnName
    )
    {
        var source = ResolveColumn(ref anchor, anchorValue, out _);

        if (source is not MySqlRecursiveTextCastExpression cast
            || frontier.MaximumDepth is not SqlConstantExpression { Value: int depth }
            || !TryMaximumCharacters(cast.Operand, anchor, out var initialCapacity)
            || !TryLinearGrowth(stepValue, frontier.Alias!, columnName, cast.Collation, out var growth))
        {
            return false;
        }

        return initialCapacity + (long)growth * depth <= cast.Capacity;
    }

    /// <summary>Gets only explicitly known charset-prefixed collations; shared UCA aliases do not imply one encoding.</summary>
    private static bool TryCharacterSet(
        string collation,
        out string charSet
    )
    {
        MySqlSqlTokenValidator.ValidateIdentifier(collation, nameof(collation));
        var separator = collation.IndexOf('_');
        charSet = separator > 0
            ? collation[..separator]
            : string.Empty;

        return charSet.ToLowerInvariant() is "utf8mb4"
            or "utf8mb3"
            or "utf8"
            or "ascii"
            or "latin1"
            or "ucs2"
            or "utf16"
            or "utf16le"
            or "utf32";
    }

    /// <summary>Derives source character bounds from actual literals, physical mappings or an explicit finite cast.</summary>
    private static bool TryMaximumCharacters(
        SqlExpression expression,
        SelectExpression scope,
        out int characters
    )
    {
        // WHY: Collation wrappers do not contribute a bound; resolve their operands
        // in the same scope without retaining a frame for each wrapper.
        while (true)
        {
            expression = ResolveColumn(ref scope, expression, out var physical);

            switch (expression)
            {
                case SqlConstantExpression { Value: string value } constant
                    when constant.TypeMapping?.Converter is null:
                    characters = CountCharacters(value);

                    return true;

                case SqlConstantExpression { Value: null }:
                    characters = 0;

                    return true;

                case CollateExpression collate:
                    expression = collate.Operand;
                    continue;

                case MySqlRecursiveTextCastExpression cast:
                    characters = cast.Capacity;

                    return true;

                case ColumnExpression { TypeMapping: { } mapping } when physical:
                    if (IsBoundedCharacterType(mapping.StoreTypeNameBase)
                        && TryDeclaredSize(mapping, out var size))
                    {
                        characters = size;

                        return true;
                    }

                    break;
            }

            characters = 0;

            return false;
        }
    }

    /// <summary>Follows ordinary completed projections without mistaking a nominal CLR fallback for physical width.</summary>
    private static SqlExpression ResolveColumn(
        ref SelectExpression scope,
        SqlExpression expression,
        out bool physical
    )
    {
        while (expression is ColumnExpression column)
        {
            SelectExpression? nested = null;

            foreach (var source in scope.Tables)
            {
                var table = source;

                while (table is JoinExpressionBase join)
                {
                    table = join.Table;
                }

                if (table.Alias == column.TableAlias)
                {
                    if (table is TableExpression { Table: ITable })
                    {
                        physical = true;

                        return expression;
                    }

                    nested = table switch
                    {
                        SelectExpression select => select,
                        MySqlMaterializedSourceExpression reference => reference.Source,
                        _ => null,
                    };

                    break;
                }
            }

            var projection = nested?.Projection.FirstOrDefault(candidate => candidate.Alias == column.Name);

            if (nested is null
                || projection is null)
            {
                physical = false;

                return expression;
            }

            scope = nested;
            expression = projection.Expression;
        }

        physical = false;

        return expression;
    }

    /// <summary>Accepts explicit complete type declarations even when EF's optional Size facet is absent.</summary>
    private static bool TryDeclaredSize(
        RelationalTypeMapping mapping,
        out int size
    )
    {
        if (mapping.Size is { } declared and >= 0)
        {
            size = declared;

            return true;
        }

        var open = mapping.StoreType.IndexOf('(');
        var close = mapping.StoreType.IndexOf(')');
        size = 0;

        return open >= 0
            && close > open
            && int.TryParse(mapping.StoreType.AsSpan(open + 1, close - open - 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out size)
            && size >= 0;
    }

    /// <summary>Limits finite character bounds to native character-count declarations.</summary>
    private static bool IsBoundedCharacterType(
        string name
    ) => name.Equals("char", StringComparison.OrdinalIgnoreCase)
        || name.Equals("varchar", StringComparison.OrdinalIgnoreCase)
        || name.Equals("nchar", StringComparison.OrdinalIgnoreCase)
        || name.Equals("nvarchar", StringComparison.OrdinalIgnoreCase);

    /// <summary>Counts Unicode scalar characters, avoiding UTF-16 surrogate pairs as a false native capacity.</summary>
    private static int CountCharacters(
        string value
    )
    {
        var count = 0;

        var characters = value.EnumerateRunes();

        while (characters.MoveNext())
        {
            count++;
        }

        return count;
    }

    /// <summary>Recognizes one bounded concatenation tree without evaluating application expressions.</summary>
    private static (bool Valid, int Frontiers, int Characters) CountGrowth(
        SqlExpression expression,
        string frontierAlias,
        string columnName,
        string? collation
    )
    {
        // WHY: Accepted collation and empty-coalesce wrappers retain the same growth
        // calculation. Only genuine concatenation branches require recursive results.
        while (true)
        {
            switch (expression)
            {
                case ColumnExpression column when column.TableAlias == frontierAlias && column.Name == columnName:
                    return (true, 1, 0);

                case SqlConstantExpression { Value: string value } constant
                    when constant.TypeMapping?.Converter is null:
                    return (true, 0, CountCharacters(value));

                case CollateExpression explicitCollation when string.Equals(explicitCollation.Collation,
                    collation,
                    StringComparison.OrdinalIgnoreCase):
                    expression = explicitCollation.Operand;
                    continue;

                case SqlFunctionExpression
                {
                    IsBuiltIn: true, Name: "COALESCE", Arguments: [var value, SqlConstantExpression { Value: "" }],
                }:
                    expression = value;
                    continue;

                case SqlBinaryExpression { OperatorType: ExpressionType.Add, Type: { } type } binary
                    when type == typeof(string):
                    var left = CountGrowth(binary.Left, frontierAlias, columnName, collation);
                    var right = CountGrowth(binary.Right, frontierAlias, columnName, collation);

                    return (left.Valid && right.Valid, checked(left.Frontiers + right.Frontiers),
                        checked(left.Characters + right.Characters));

                case SqlFunctionExpression { IsBuiltIn: true, Name: "CONCAT", Arguments: { } arguments }:
                    var frontiers = 0;
                    var characters = 0;

                    foreach (var argument in arguments)
                    {
                        var part = CountGrowth(argument, frontierAlias, columnName, collation);

                        if (!part.Valid)
                        {
                            return (false, 0, 0);
                        }

                        frontiers = checked(frontiers + part.Frontiers);
                        characters = checked(characters + part.Characters);
                    }

                    return (true, frontiers, characters);

                default:
                    return (false, 0, 0);
            }
        }
    }

    /// <summary>Gets scalar projection members while excluding entity, collection and client-index shapes.</summary>
    private static bool TryGetProjectionMap(
        ShapedQueryExpression source,
        out Dictionary<ProjectionMember, Expression> projections
    )
    {
        var visitor = new ProjectionMapCollector((SelectExpression)source.QueryExpression);
        visitor.Visit(source.ShaperExpression);
        projections = visitor.Projections;

        return visitor.Valid;
    }

    /// <summary>Collects only real server projection members, retaining their exact EF identity.</summary>
    private sealed class ProjectionMapCollector : ExpressionVisitor
    {
        private readonly SelectExpression _select;

        /// <summary>Initializes the immutable dependencies for this operation.</summary>
        public ProjectionMapCollector(
            SelectExpression select
        )
        {
            _select = select;
        }

        /// <summary>Gets the actual scalar projection members retained by the flat shaper.</summary>
        public Dictionary<ProjectionMember, Expression> Projections { get; } = [];

        /// <summary>Gets whether every bound projection can be rewritten without an entity or client shape.</summary>
        public bool Valid { get; private set; } = true;

        /// <inheritdoc />
        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is ProjectionBindingExpression binding
                && ReferenceEquals(binding.QueryExpression, _select)
                && binding.ProjectionMember is { } member
                && _select.GetProjection(binding) is SqlExpression scalar)
            {
                Projections[member] = scalar;

                return node;
            }

            if (node is StructuralTypeShaperExpression or ProjectionBindingExpression)
            {
                Valid = false;

                return node;
            }

            return base.VisitExtension(node);
        }
    }

    /// <summary>Finds the finite current frontier without traversing independent completed definitions.</summary>
    private sealed class FrontierFinder : ExpressionVisitor
    {
        private readonly int _bindingId;

        private MySqlRecursiveCteReferenceExpression? _frontier;

        /// <summary>Initializes the immutable dependencies for this operation.</summary>
        public FrontierFinder(
            int bindingId
        )
        {
            _bindingId = bindingId;
        }

        /// <summary>Finds only the current direct recursion identity, ignoring independent definition graphs.</summary>
        public static MySqlRecursiveCteReferenceExpression? Find(
            Expression expression,
            int bindingId
        )
        {
            var visitor = new FrontierFinder(bindingId);
            visitor.Visit(expression);

            return visitor._frontier;
        }

        /// <inheritdoc />
        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is MySqlRecursiveCteReferenceExpression frontier
                && frontier.BindingId == _bindingId)
            {
                _frontier = frontier;

                return node;
            }

            return node is MySqlMaterializedSourceExpression
                ? node
                : base.VisitExtension(node);
        }
    }
}
