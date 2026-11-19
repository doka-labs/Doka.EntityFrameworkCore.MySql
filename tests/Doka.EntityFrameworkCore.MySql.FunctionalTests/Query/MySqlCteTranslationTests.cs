namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Qualifies lexical ordinary CTE composition without opening a connection.</summary>
public sealed partial class MySqlCteTranslationTests
{
    /// <summary>One ordinary definition preserves its complete mapped source and outer predicate.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ordinary_cte_has_one_definition_without_a_materialization_arm(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var minimum = 3;

        // Act
        var sql = context
            .Rows
            .Where(row => row.Id > minimum)
            .WithCte(rows => rows.Where(row => row.Id < 20))
            .ToQueryString();

        // Assert
        AssertOrdinary(sql, 1);
        Assert.Equal(1, CountOccurrences(sql, "FROM `CteRows`"));
        Assert.Contains("@minimum", sql, StringComparison.Ordinal);
        Assert.Contains("< 20", sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>The source remains a bag when a consumer explicitly concatenates both references.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scalar_bag_concatenation_does_not_acquire_a_hidden_distinct(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Select(row => row.Name)
            .WithCte(rows => rows.Concat(rows))
            .ToQueryString();

        // Assert
        AssertOrdinary(sql, 1);
        Assert.Equal(1, CountOccurrences(sql, "UNION ALL"));
        Assert.DoesNotContain("DISTINCT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(sql, "FROM `CteRows`"));
    }

    /// <summary>Each dependency is declared once before its consumer definition.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dependent_ctes_share_the_completed_parent_definition(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Select(row => row.Id)
            .WithCte(first =>
                first
                    .Where(id => id > 4)
                    .WithCte(second =>
                        first.Join(second, id => id, id => id, (left, right) => left + right)))
            .ToQueryString();

        // Assert
        AssertOrdinary(sql, 2);
        Assert.Equal(1, CountOccurrences(sql, "FROM `CteRows`"));
        Assert.True(sql.IndexOf("`__doka_cte_0` (", StringComparison.Ordinal)
            < sql.IndexOf("`__doka_cte_1` (", StringComparison.Ordinal),
            sql);
    }

    /// <summary>Separately bound expressions retain distinct identities despite an identical source.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Separate_ordinary_bindings_are_not_merged_by_structural_equality(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Select(row => row.Id)
            .WithCte(first =>
                context
                    .Rows
                    .Select(row => row.Id)
                    .WithCte(second =>
                        first.Join(second, id => id, id => id, (left, right) => left + right)))
            .ToQueryString();

        // Assert
        AssertOrdinary(sql, 2);
        Assert.Equal(2, CountOccurrences(sql, "FROM `CteRows`"));
    }

    /// <summary>An ordinary source retains its source page before multiple consumer references.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_page_remains_inside_the_single_definition(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var skip = 2;
        var take = 4;

        // Act
        var sql = context
            .Rows
            .OrderBy(row => row.Id)
            .Skip(skip)
            .Take(take)
            .Select(row => row.Id)
            .WithCte(rows => rows.Concat(rows))
            .OrderBy(id => id)
            .ToQueryString();

        var page = Regex.Match(
            sql,
            @"LIMIT\s+(?<take>@\w+)\s+OFFSET\s+(?<skip>@\w+)");

        // Assert
        AssertOrdinary(sql, 1);
        Assert.Equal(1, CountOccurrences(sql, "LIMIT"));
        Assert.Equal(1, CountOccurrences(sql, "OFFSET"));
        Assert.Equal(2, CountOccurrences(sql, "ORDER BY"));
        Assert.True(page.Success, sql);
        AssertParameterValue(sql, page.Groups["skip"].Value, skip);
        AssertParameterValue(sql, page.Groups["take"].Value, take);
    }

    /// <summary>Different reference projections preserve their union of required typed columns.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void References_keep_the_common_schema_needed_by_each_consumer(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Select(row => new
            {
                row.Id,
                row.Name,
                row.OptionalName,
            })
            .WithCte(rows => rows.Join(rows,
                row => row.Id,
                row => row.Id,
                (left, right) => new
                {
                    left.Name,
                    right.OptionalName,
                }))
            .ToQueryString();

        // Assert
        AssertOrdinary(sql, 1);
        Assert.Contains("`Name`", sql, StringComparison.Ordinal);
        Assert.Contains("`OptionalName`", sql, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(sql, "FROM `CteRows`"));
    }

    /// <summary>CTE declarations alone do not force evaluation of an unused ordinary source.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unused_ordinary_definition_is_pruned_from_the_statement(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Select(_ => Guid.NewGuid())
            .WithCte(_ => context.Rows.Select(row => row.Id))
            .ToQueryString();

        // Assert
        Assert.DoesNotContain("WITH", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UUID()", sql, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(sql, "FROM `CteRows`"));
    }

    /// <summary>Binding the filtered root does not suppress a separately filtered navigation target.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_and_navigated_target_filters_keep_their_own_scope(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .WithCte(rows => rows.Select(row => row.Target.Name))
            .ToQueryString();

        // Assert
        AssertOrdinary(sql, 1);
        Assert.Equal(1, CountOccurrences(sql, "FROM `CteRows`"));
        Assert.Contains("`TenantId` =", sql, StringComparison.Ordinal);
        Assert.Contains("`IsVisible`", sql, StringComparison.Ordinal);
    }

    /// <summary>Explicit query-filter opt-out remains effective across the binding.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Explicit_filter_opt_out_removes_the_navigated_target_filter(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .WithCte(rows => rows.Select(row => row.Target.Name))
            .IgnoreQueryFilters()
            .ToQueryString();

        // Assert
        AssertOrdinary(sql, 1);
        Assert.DoesNotContain("`IsVisible`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`TenantId` =", sql, StringComparison.Ordinal);
    }

    /// <summary>Reference Includes retain normal entity shaping outside the flat source definition.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reference_include_is_translated_on_the_composed_entity(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .WithCte(rows => rows.Include(row => row.Target))
            .ToQueryString();

        // Assert
        AssertOrdinary(sql, 1);
        Assert.Contains("JOIN", sql, StringComparison.Ordinal);
        Assert.Contains("`CteTargets`", sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Materialized and ordinary definitions retain their distinct native contracts in one header.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mixed_materialized_binding_keeps_only_its_own_unreachable_arm(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Select(row => row.Id)
            .WithCte(ordinary =>
                ordinary.WithMaterializedSource(shared =>
                    ordinary.Join(shared, id => id, id => id, (left, right) => left + right)))
            .ToQueryString();

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "WITH RECURSIVE"));
        Assert.Equal(1, CountOccurrences(sql, "WHERE 1 = 0"));
        Assert.Equal(1, CountOccurrences(sql, "`__doka_cte_0` ("));
        Assert.Equal(1, CountOccurrences(sql, "`__doka_source_1` ("));
        Assert.Equal(1, CountOccurrences(sql, "FROM `CteRows`"));
    }

    /// <summary>Native ordinary CTEs do not gain the materialized source's volatile-value promise.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Volatile_ordinary_projection_has_no_forced_recursive_arm(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Select(row => new
            {
                row.Id,
                Token = Guid.NewGuid(),
            })
            .WithCte(rows => rows.Join(rows,
                row => row.Id,
                row => row.Id,
                (left, right) => new
                {
                    First = left.Token,
                    Second = right.Token,
                }))
            .ToQueryString();

        // Assert
        AssertOrdinary(sql, 1);
        Assert.Equal(1, CountOccurrences(sql, "UUID()"));
        Assert.DoesNotContain("UNION ALL", sql, StringComparison.Ordinal);
    }

    /// <summary>Ordinary set-source duplicates retain the explicit UNION ALL operator.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Flat_set_source_retains_its_native_bag_operation(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var source = context.Rows.Select(row => row.Id);

        // Act
        var sql = source
            .Concat(source)
            .WithCte(rows => rows.Where(id => id > 3))
            .ToQueryString();

        // Assert
        AssertOrdinary(sql, 1);
        Assert.Equal(1, CountOccurrences(sql, "UNION ALL"));
        Assert.Equal(2, CountOccurrences(sql, "FROM `CteRows`"));
    }

    /// <summary>A native aggregate source remains a flat typed SQL relation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Flat_aggregate_source_is_not_client_grouping(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .GroupBy(row => row.TenantId)
            .Select(group => new
            {
                Tenant = group.Key,
                Count = group.Count(),
            })
            .WithCte(rows => rows.Where(row => row.Count > 1))
            .ToQueryString();

        // Assert
        AssertOrdinary(sql, 1);
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(", sql, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(sql, "FROM `CteRows`"));
    }

    /// <summary>Inspects definition multiplicity independently from a particular SQL indentation.</summary>
    private static void AssertOrdinary(
        string sql,
        int definitionCount
    )
    {
        Assert.Equal(1, Regex.Count(sql, @"(?m)^WITH\r?$"));
        Assert.DoesNotContain("WITH RECURSIVE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WHERE 1 = 0", sql, StringComparison.Ordinal);
        Assert.Equal(definitionCount,
            Regex.Count(sql, @"`__doka_cte_\d+(?:_\d+)?` \("));
    }

    /// <summary>Checks the actual SQL parameter value without depending on EF's captured-variable naming.</summary>
    private static void AssertParameterValue(
        string sql,
        string name,
        int value
    ) => Assert.Matches(
        Regex.Escape(name)
        + @"\s*=\s*'?"
        + value.ToString(CultureInfo.InvariantCulture)
        + @"(?:'|\b)",
        sql);

    /// <summary>Counts structural SQL occurrences whose multiplicity distinguishes references from definitions.</summary>
    private static int CountOccurrences(
        string sql,
        string fragment
    )
    {
        var count = 0;
        var offset = 0;

        while ((offset = sql.IndexOf(fragment, offset, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }

        return count;
    }
}
