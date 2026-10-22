namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlQueryTranslationExtendedTests
{
    /// <summary>
    /// Consecutive parameterized Take operators are evaluated before SQL
    /// generation because MySQL LIMIT does not accept scalar functions.
    /// </summary>
    [Fact]
    public void Consecutive_parameterized_take_uses_an_integral_limit()
    {
        // Arrange
        using var context = CreateContext();
        var firstLimit = 5;
        var secondLimit = 3;

        // Act
        var sql = context
            .Items
            .OrderBy(entity => entity.Id)
            .Take(firstLimit)
            .Take(secondLimit)
            .ToQueryString();

        // Assert
        Assert.Contains("LIMIT 3", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("LIMIT LEAST", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies distinct translates to select distinct.</summary>
    [Fact]
    public void Distinct_translates_to_select_distinct()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => e.Name)
            .Distinct()
            .ToQueryString();

        // Assert
        Assert.Contains("DISTINCT", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// TagWith preserves SQL comments.
    /// </summary>
    [Fact]
    public void Tagwith_preserves_sql_comment()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.TagWith("package6-verification")
            .ToQueryString();

        // Assert
        Assert.Contains("package6-verification", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Subquery IN (SELECT ...) produces valid MySQL.
    /// </summary>
    [Fact]
    public void Subquery_in_select_produces_valid_sql()
    {
        // Arrange
        using var context = CreateContext();
        var threshold = 10.0;

        // Act
        var sql = context
            .Items.Where(e => context
                .Items.Where(inner => inner.Value > threshold)
                .Select(inner => inner.Id)
                .Contains(e.Id))
            .ToQueryString();

        // Assert
        Assert.Contains("IN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`Id`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// OrderBy produces ORDER BY in generated SQL.
    /// </summary>
    [Fact]
    public void Orderby_produces_order_by_in_sql()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.OrderBy(e => e.Id)
            .Select(e => e.Id)
            .ToQueryString();

        // Assert
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`Id`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ternary expression -> CASE WHEN ... THEN ... ELSE ... END.
    /// </summary>
    [Fact]
    public void Ternary_expression_translates_to_case_when()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => e.Value > 0 ? "positive" : "non-positive")
            .ToQueryString();

        // Assert
        Assert.Contains("CASE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHEN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("THEN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ELSE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Null coalesce ?? -> COALESCE(a, b).
    /// </summary>
    [Fact]
    public void Null_coalesce_translates_to_coalesce()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => e.NullableName ?? "default")
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "COALESCE");
    }

    /// <summary>
    /// WHERE Id IN (SELECT ...) subquery.
    /// </summary>
    [Fact]
    public void Subquery_in_where_produces_valid_sql()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Where(e => context
                .Items.Where(inner => inner.Value > 5)
                .Select(inner => inner.Id)
                .Contains(e.Id))
            .ToQueryString();

        // Assert
        Assert.Contains("IN", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// WHERE EXISTS (SELECT 1 ...) subquery.
    /// </summary>
    [Fact]
    public void Exists_subquery_produces_valid_sql()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Where(e => context.Items.Any(inner => inner.Id == e.Id && inner.Value > 10))
            .ToQueryString();

        // Assert
        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies union produces union sql.</summary>
    [Fact]
    public void Union_produces_union_sql()
    {
        // Arrange
        using var context = CreateContext();
        var query1 = context.Items.Where(e => e.Value > 10);
        var query2 = context.Items.Where(e => e.Value < 5);

        // Act
        var sql = query1
            .Union(query2)
            .ToQueryString();

        // Assert
        Assert.Contains("UNION", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies concat produces union all sql.</summary>
    [Fact]
    public void Concat_produces_union_all_sql()
    {
        // Arrange
        using var context = CreateContext();
        var query1 = context.Items.Where(e => e.Value > 10);
        var query2 = context.Items.Where(e => e.Value < 5);

        // Act
        var sql = query1
            .Concat(query2)
            .ToQueryString();

        // Assert
        Assert.Contains("UNION ALL", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// JsonElement property -> json column type.
    /// </summary>
    [Fact]
    public void Json_element_property_maps_to_json_column()
    {
        // Arrange
        using var context = new JsonPropertyContext(CreateJsonOptions());

        // Act
        var entityType = context.Model.FindEntityType(typeof(JsonPropertyEntity))!;
        var columnType = entityType.FindProperty(nameof(JsonPropertyEntity.Data))!.GetColumnType();

        // Assert
        Assert.Equal("json", columnType);
    }

    /// <summary>
    /// Scalar subquery in SELECT projection.
    /// </summary>
    [Fact]
    public void Scalar_subquery_in_select_produces_valid_sql()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => new
            {
                e.Name,
                MaxValue = context.Items.Max(inner => inner.Value),
            })
            .ToQueryString();

        // Assert
        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Correlated subquery with outer reference.
    /// </summary>
    [Fact]
    public void Correlated_subquery_produces_valid_sql()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Where(e => e.Value
                > context
                    .Items.Where(i => i.Name == e.Name)
                    .Average(i => i.Value))
            .ToQueryString();

        // Assert
        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        MySqlSqlAssert.ContainsFunction(sql, "AVG");
    }

    /// <summary>
    /// OrderByDescending + First -> ORDER BY DESC LIMIT 1.
    /// </summary>
    [Fact]
    public void Last_pattern_orderby_desc_take_1()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.OrderByDescending(e => e.CreatedAt)
            .Take(1)
            .ToQueryString();

        // Assert
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DESC", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Enum query with int comparison.
    /// </summary>
    [Fact]
    public void Enum_int_comparison_in_where()
    {
        // Arrange
        using var context = new EnumQueryContext(CreateEnumOptions());

        // Act
        var sql = context
            .Set<EnumQueryEntity>()
            .Where(e => e.Status == QueryStatus.Active)
            .ToQueryString();

        // Assert
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that EF Core can compose LINQ over a parameterized CTE without
    /// inlining the value into the CTE body.
    /// </summary>
    [Fact]
    public void Cte_composition_preserves_parameterization()
    {
        // Arrange
        using var context = CreateContext();
        var minimumValue = 50D;

        // Act
        var sql = context.Items
            .FromSqlInterpolated(
                $"""
                WITH `FilteredItems` AS (
                    SELECT *
                    FROM `TranslationTestItems`
                    WHERE `Value` > {minimumValue}
                )
                SELECT * FROM `FilteredItems`
                """)
            .Where(item => item.Name.StartsWith('A'))
            .OrderBy(item => item.Id)
            .ToQueryString();

        // Assert
        Assert.Contains("WITH `FilteredItems` AS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`Value` > @p0", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that composition fails before execution when the configured
    /// server predates the documented CTE boundary.
    /// </summary>
    [Fact]
    public void Cte_composition_rejects_unsupported_server_version()
    {
        // Arrange
        using var context = CreateContext(
            MySqlServerVersion.MySql(
                new Version(8, 0, 0),
                MySqlServerVersionCompatibilityMode.AllowUnsupported));

        var query = context.Items
            .FromSqlRaw(
                """
                WITH `Items` AS (
                    SELECT * FROM `TranslationTestItems`
                )
                SELECT * FROM `Items`
                """)
            .Where(item => item.Id > 0);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Equal(
            "The configured database engine does not support common table expressions.",
            exception.Message);
    }

    /// <summary>Verifies nested ternary produces case when.</summary>
    [Fact]
    public void Nested_ternary_produces_case_when()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => e.Value > 100 ? "high" : e.Value > 50 ? "medium" : "low")
            .ToQueryString();

        // Assert
        Assert.Contains("CASE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies any with predicate produces exists.</summary>
    [Fact]
    public void Any_with_predicate_produces_exists()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Where(e => context.Items.Any(inner => inner.Id == e.Id))
            .ToQueryString();

        // Assert
        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Select with anonymous type projection.
    /// </summary>
    [Fact]
    public void Anonymous_projection_produces_valid_sql()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => new
            {
                e.Id,
                e.Name,
                NameLength = e.Name.Length,
            })
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "CHAR_LENGTH");
    }

    /// <summary>
    /// ElementAt(n) -> OFFSET n LIMIT 1.
    /// </summary>
    [Fact]
    public void Skip_take_1_produces_offset_limit()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.OrderBy(e => e.Id)
            .Skip(5)
            .Take(1)
            .ToQueryString();

        // Assert
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
    }
}
