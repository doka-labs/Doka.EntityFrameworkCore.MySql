namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

public partial class QueryEnumerationBenchmark
{
    /// <summary>Prepares complete feature compilation with setup-only SQL and collection-shape controls.</summary>
    private Func<object> CreateFeatureOperation() => Scenario switch
    {
        "compile-cte-multi-reference" => CreateMultiReferenceCteOperation(),
        "compile-cte-deep-graph" => CreateDeepCteGraphOperation(),
        "compile-cte-wide-pruned" => CreateWidePrunedCteOperation(),
        "compile-recursive-numeric" => CreateNumericRecursiveCteOperation(),
        "compile-window-fluent" => CreateFluentWindowOperation(),
        "compile-window-index-global" => CreateGlobalIndexedWindowOperation(),
        "compile-window-index-grouped" => CreateGroupedIndexedWindowOperation(),
        _ => throw new ArgumentOutOfRangeException(nameof(Scenario)),
    };

    /// <summary>Compiles one ordinary definition consumed by three independently aliased references.</summary>
    private Func<object> CreateMultiReferenceCteOperation()
    {
        var query = _context
            .Rows
            .Select(row => new SourceProjection
            {
                Id = row.Id,
                Name = row.Name,
            })
            .WithCte(rows => rows
                .Join(rows,
                    left => left.Id,
                    right => right.Id,
                    (left, right) => new
                    {
                        left.Id,
                        left.Name,
                        OtherName = right.Name,
                    })
                .Join(rows,
                    pair => pair.Id,
                    third => third.Id,
                    (pair, third) => new
                    {
                        pair.Id,
                        pair.Name,
                        pair.OtherName,
                        ThirdName = third.Name,
                    }));

        var operation = Compile(query);
        RequireOrdinaryCteDefinitions(1);
        RequireCompiledSql("INNER JOIN", "OtherName", "ThirdName");
        RequireOccurrences("FROM `EnumerationRows`", 1);
        ReportFeatureShape(query, typeof(Row), selectorFields: 4, references: 3, dependencyDepth: 1);

        return operation;
    }

    /// <summary>Compiles six dependent definitions while the root also remains a final consumer input.</summary>
    private Func<object> CreateDeepCteGraphOperation()
    {
        var query = _context
            .PlainRows
            .Select(row => row.Id)
            .WithCte(first =>
                first
                    .Select(value => value + 1)
                    .WithCte(second =>
                        second
                            .Select(value => value + 1)
                            .WithCte(third =>
                                third
                                    .Select(value => value + 1)
                                    .WithCte(fourth =>
                                        fourth
                                            .Select(value => value + 1)
                                            .WithCte(fifth =>
                                                fifth
                                                    .Select(value => value + 1)
                                                    .WithCte(sixth =>
                                                        first.Join(sixth,
                                                            left => left,
                                                            right => right,
                                                            (left, right) => left + right)))))));

        var operation = Compile(query);
        RequireOrdinaryCteDefinitions(6);
        RequireCompiledSql("INNER JOIN", "+ 1");
        RequireOccurrences("FROM `EnumerationPlainRows`", 1);
        ReportFeatureShape(query, typeof(PlainRow), selectorFields: 1, references: 7, dependencyDepth: 6);

        return operation;
    }

    /// <summary>Compiles the full fifty-field model and validates the four-column shared CTE schema demanded by both references.</summary>
    private Func<object> CreateWidePrunedCteOperation()
    {
        var query = _context
            .FeatureWideRows
            .AsNoTracking()
            .WithCte(rows =>
                rows.Join(rows,
                    first => first.Id,
                    second => second.ParentId ?? 0,
                    (first, second) => new
                    {
                        First = EF.Property<int>(first, "Payload01"),
                        Second = EF.Property<int>(second, "Payload48"),
                    }));

        var operation = Compile(query);
        RequireOrdinaryCteDefinitions(1);
        RequireOccurrences("FROM `EnumerationFeatureWideRows`", 1);
        var sql = CompiledFeatureSql();
        var header = Regex.Match(sql,
            @"`__doka_cte_\d+(?:_\d+)?` \((?<columns>[^)]+)\) AS \(");

        var fields = header
            .Groups["columns"]
            .Value
            .Split(", ");

        var properties = (_context.Model.FindEntityType(typeof(FeatureWideRow))
            ?? throw new InvalidOperationException("The fifty-field model type is missing.")).GetProperties();

        if (!header.Success
            || fields.Length != 4
            || properties.Count() != 50
            || !fields.Contains("`Id`", StringComparer.Ordinal)
            || !fields.Contains("`ParentId`", StringComparer.Ordinal)
            || !fields.Contains("`Payload01`", StringComparer.Ordinal)
            || !fields.Contains("`Payload48`", StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The fifty-field CTE fixture did not retain exactly its four demanded fields.");
        }

        // WHY: Absence of every unused field proves actual pruning, rather than
        // merely finding two selected names somewhere in a still-wide statement.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 2; index <= 47; index++)
        {
            var unused = "`Payload" + index.ToString("D2", CultureInfo.InvariantCulture) + "`";

            if (sql.Contains(unused, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The CTE fixture retained an unused shadow field.");
            }
        }

        Console.WriteLine(
            $"cte-header-fields={fields.Length}; cte-header-collection={fields.GetType().FullName}; shadow-fields=48");

        ReportFeatureShape(query, typeof(FeatureWideRow), selectorFields: 2, references: 2, dependencyDepth: 1);

        return operation;
    }

    /// <summary>Compiles the actual bounded numeric recursive arm, root guard and ordered scalar result.</summary>
    private Func<object> CreateNumericRecursiveCteOperation()
    {
        var query = _context
            .PlainRows
            .Where(row => row.Id == 1)
            .Select(row => row.Id)
            .WithRecursiveCte(frontier => frontier.Select(value => value + 1),
                rows => rows.OrderBy(value => value),
                32);

        var operation = Compile(query);
        RequireCompiledSql("WITH RECURSIVE", "UNION ALL", "__doka_depth", "ORDER BY", "+ 1");
        RequireOccurrences("FROM `EnumerationPlainRows`", 1);
        ReportFeatureShape(query, typeof(PlainRow), selectorFields: 1, references: 2, dependencyDepth: 1);

        return operation;
    }

    /// <summary>Compiles ranking, a running frame and a defaulted offset through the public fluent window API.</summary>
    private Func<object> CreateFluentWindowOperation()
    {
        var query = _context.Rows.Select(row => new
        {
            row.Id,
            Number = EF
                .Functions
                .Window()
                .PartitionBy(row.GroupId)
                .OrderBy(row.Name)
                .ThenBy(row.Id)
                .RowNumber(),
            Running = EF
                .Functions
                .Window()
                .PartitionBy(row.GroupId)
                .OrderBy(row.Name)
                .ThenBy(row.Id)
                .RowsBetween(MySqlWindowFrameBound.UnboundedPreceding, MySqlWindowFrameBound.CurrentRow)
                .Sum(row.Id),
            Previous = EF
                .Functions
                .Window()
                .PartitionBy(row.GroupId)
                .OrderBy(row.Name)
                .ThenBy(row.Id)
                .Lag<int>(row.Id, 1, -1),
        });

        var operation = Compile(query);
        RequireCompiledSql("ROW_NUMBER()",
            "SUM(",
            "LAG(",
            "PARTITION BY",
            "ORDER BY",
            "ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW");

        RequireOccurrences("FROM `EnumerationRows`", 1);
        ReportFeatureShape(query, typeof(Row), selectorFields: 4, references: 1, dependencyDepth: 0);

        return operation;
    }

    /// <summary>Compiles global indexed LINQ after a completed source page and before an outer ordinal predicate.</summary>
    private Func<object> CreateGlobalIndexedWindowOperation()
    {
        var query = _context
            .PlainRows
            .OrderBy(row => row.Id)
            .Skip(250)
            .Take(1000)
            .Select((row, index) => new
            {
                row.Id,
                Index = index,
            })
            .Where(row => row.Index < 50);

        var operation = Compile(query);
        RequireCompiledSql("ROW_NUMBER()", "FROM (", "LIMIT", "OFFSET", "WHERE");
        RequireOccurrences("ROW_NUMBER()", 1);
        RequireOccurrences("FROM `EnumerationPlainRows`", 1);
        ReportFeatureShape(query, typeof(PlainRow), selectorFields: 2, references: 1, dependencyDepth: 0);

        return operation;
    }

    /// <summary>Compiles the actual grouped indexed collection and its independent grouped COUNT result.</summary>
    private Func<object> CreateGroupedIndexedWindowOperation()
    {
        var query = _context
            .Rows
            .GroupBy(row => row.GroupId)
            .Select(group => new
            {
                Group = group.Key,
                Count = group.Count(),
                Items = group
                    .OrderBy(row => row.Name)
                    .ThenBy(row => row.Id)
                    .Select((row, index) => new
                    {
                        row.Id,
                        Index = index,
                    })
                    .ToList(),
            });

        var operation = Compile(query);
        RequireCompiledSql("ROW_NUMBER()", "PARTITION BY", "GROUP BY", "COUNT(*)", "LEFT JOIN");
        RequireOccurrences("ROW_NUMBER()", 1);
        Console.WriteLine("grouped-item-selector-fields=2; grouped-item-result=System.Collections.Generic.List");
        ReportFeatureShape(query, typeof(Row), selectorFields: 3, references: 2, dependencyDepth: 0);

        return operation;
    }

    /// <summary>Checks lexical definitions rather than counting aliases in reference sites.</summary>
    private void RequireOrdinaryCteDefinitions(
        int expected
    )
    {
        RequireCompiledSql("__doka_cte_");
        var sql = CompiledFeatureSql();
        var definitions = Regex.Matches(sql,
            @"`__doka_cte_\d+(?:_\d+)?` \(");

        if (definitions.Count != expected
            || !Regex.IsMatch(sql, @"\bWITH\s+")
            || Regex.IsMatch(sql, @"\bWITH\s+RECURSIVE\b"))
        {
            throw new InvalidOperationException(
                "The ordinary CTE fixture did not preserve its intended dependency graph.");
        }
    }

    /// <summary>Rejects setup whose SQL silently duplicates or bypasses the intended feature boundary.</summary>
    private void RequireOccurrences(
        string fragment,
        int expected
    )
    {
        var sql = CompiledFeatureSql();
        var count = 0;
        var start = 0;

        while (sql.IndexOf(fragment, start, StringComparison.Ordinal) is var position
            && position >= 0)
        {
            count++;
            start = position + fragment.Length;
        }

        if (count != expected)
        {
            throw new InvalidOperationException(
                $"The {Scenario} SQL has {count} occurrences of '{fragment}'; expected {expected}.");
        }
    }

    /// <summary>Requires setup to have produced the same SQL used by the existing fingerprint control.</summary>
    private string CompiledFeatureSql() => _compiledQuerySql
        ?? throw new InvalidOperationException("The feature query was not compiled during setup.");

    /// <summary>Reports measured input collections and model width outside the compiler benchmark invocation.</summary>
    private void ReportFeatureShape(
        IQueryable query,
        Type sourceType,
        int selectorFields,
        int references,
        int dependencyDepth
    )
    {
        var properties = (_context.Model.FindEntityType(sourceType)
            ?? throw new InvalidOperationException("The feature source model type is missing.")).GetProperties();

        var arguments = ((MethodCallExpression)query.Expression).Arguments;

        if (_context.Database.GetDbConnection()
                .State
            != ConnectionState.Closed)
        {
            throw new InvalidOperationException("A compiler-only benchmark must not open a database connection.");
        }

        Console.WriteLine(
            $"feature-source-model-fields={properties.Count()}; model-properties={properties.GetType().FullName}; "
            + $"selector-fields={selectorFields}; references={references}; dependency-depth={dependencyDepth}; "
            + $"query-arguments={arguments.GetType().FullName}; argument-count={arguments.Count}; sql-chars={CompiledFeatureSql().Length}");
    }
}
