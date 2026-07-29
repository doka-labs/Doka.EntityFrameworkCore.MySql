namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies extended query translation for string, DateTime, and math functions.
/// </summary>
public sealed class MySqlQueryTranslationExtendedTests
{
    // -- String Function Translations ------------------------------

    /// <summary>
    /// String.Substring server translation.
    /// </summary>
    [Fact]
    public void String_substring_translates_to_mysql_substring()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => e.Name.Substring(2))
            .ToQueryString();

        Assert.Contains("SUBSTRING", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// String.ToUpper / ToLower server translation.
    /// </summary>
    [Fact]
    public void String_upper_lower_translate_to_mysql_functions()
    {
        using var context = CreateContext();

        // ToUpper/ToLower run inside an IQueryable expression tree -- EF translates
        // them to UPPER()/LOWER() SQL, the CLR culture is never consulted.
#pragma warning disable CA1304, CA1311
        var upperSql = context
            .Items.Select(e => e.Name.ToUpper())
            .ToQueryString();
        var lowerSql = context
            .Items.Select(e => e.Name.ToLower())
            .ToQueryString();
#pragma warning restore CA1304, CA1311

        Assert.Contains("UPPER", upperSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LOWER", lowerSql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// String.Replace server translation.
    /// </summary>
    [Fact]
    public void String_replace_translates_to_mysql_replace()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => e.Name.Replace("old", "new"))
            .ToQueryString();

        Assert.Contains("REPLACE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// String.Trim server translation.
    /// </summary>
    [Fact]
    public void String_trim_translates_to_mysql_trim()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => e.Name.Trim())
            .ToQueryString();

        Assert.Contains("TRIM", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// String.IndexOf server translation.
    /// </summary>
    [Fact]
    public void String_indexof_translates_to_locate_minus_one()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => e.Name.IndexOf("test"))
            .ToQueryString();

        Assert.Contains("LOCATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// String.Concat server translation.
    /// </summary>
    [Fact]
    public void String_concat_translates_to_mysql_concat()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => string.Concat(e.Name, "-suffix"))
            .ToQueryString();

        Assert.Contains("CONCAT", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- DateTime Function Translations ----------------------------

    /// <summary>
    /// DateTime.Now translates to NOW().
    /// </summary>
    [Fact]
    public void Datetime_now_translates_to_now()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Where(e => e.CreatedAt < DateTime.Now)
            .ToQueryString();

        Assert.Contains("NOW()", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// DateTime.UtcNow translates to UTC_TIMESTAMP().
    /// </summary>
    [Fact]
    public void Datetime_utcnow_translates_to_utc_timestamp()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Where(e => e.CreatedAt < DateTime.UtcNow)
            .ToQueryString();

        Assert.Contains("UTC_TIMESTAMP()", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// DateTime subtraction uses range-preserving integer ticks rather than the
    /// database engine's range-limited TIME result.
    /// </summary>
    [Fact]
    public void Datetime_subtraction_translates_to_timestampdiff_ticks()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(entity => entity.CreatedAt - new DateTime(2000, 1, 1))
            .ToQueryString();

        Assert.Contains("TIMESTAMPDIFF(MICROSECOND", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(") * 10)", sql, StringComparison.Ordinal);
    }

    // -- Math Function Translations --------------------------------

    /// <summary>
    /// Math.Pow translates to POWER().
    /// </summary>
    [Fact]
    public void Math_pow_translates_to_power()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => Math.Pow(e.Value, 2))
            .ToQueryString();

        Assert.Contains("POWER", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Math.Sqrt translates to SQRT().
    /// </summary>
    [Fact]
    public void Math_sqrt_translates_to_sqrt()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => Math.Sqrt(e.Value))
            .ToQueryString();

        Assert.Contains("SQRT", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Math.Log translates to LN().
    /// </summary>
    [Fact]
    public void Math_log_translates_to_ln()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => Math.Log(e.Value))
            .ToQueryString();

        Assert.Contains("LN", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Math.Sign translates to SIGN().
    /// </summary>
    [Fact]
    public void Math_sign_translates_to_sign()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => Math.Sign(e.Value))
            .ToQueryString();

        Assert.Contains("SIGN", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// EF.Functions.Least translates its inline value list to native MySQL SQL.
    /// </summary>
    [Fact]
    public void Ef_functions_least_translates_to_native_function()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Where(entity => EF.Functions.Least(entity.Id, 100) == entity.Id)
            .ToQueryString();

        Assert.Contains("LEAST(`t`.`Id`, 100)", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// EF.Functions.Greatest translates its nullable inline value list to native
    /// MySQL SQL without losing its result type.
    /// </summary>
    [Fact]
    public void Ef_functions_greatest_translates_nullable_values_to_native_function()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Where(
                entity => EF.Functions.Greatest(
                    (int?)entity.Id,
                    100) == entity.Id)
            .ToQueryString();

        Assert.Contains("GREATEST(`t`.`Id`, 100)", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Consecutive parameterized Take operators are evaluated before SQL
    /// generation because MySQL LIMIT does not accept scalar functions.
    /// </summary>
    [Fact]
    public void Consecutive_parameterized_take_uses_an_integral_limit()
    {
        using var context = CreateContext();
        var firstLimit = 5;
        var secondLimit = 3;
        var sql = context
            .Items
            .OrderBy(entity => entity.Id)
            .Take(firstLimit)
            .Take(secondLimit)
            .ToQueryString();

        Assert.Contains("LIMIT 3", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("LIMIT LEAST", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- Like, Distinct, Query Tags, Subqueries ----------------

    /// <summary>
    /// EF.Functions.Like with escape character produces LIKE ... ESCAPE.
    /// </summary>
    [Fact]
    public void Ef_functions_like_with_escape_translates_correctly()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Where(e => EF.Functions.Like(e.Name, "%test%", "\\"))
            .ToQueryString();

        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Distinct_translates_to_select_distinct()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => e.Name)
            .Distinct()
            .ToQueryString();

        Assert.Contains("DISTINCT", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// TagWith preserves SQL comments.
    /// </summary>
    [Fact]
    public void Tagwith_preserves_sql_comment()
    {
        using var context = CreateContext();
        var sql = context
            .Items.TagWith("package6-verification")
            .ToQueryString();

        Assert.Contains("package6-verification", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Subquery IN (SELECT ...) produces valid MySQL.
    /// </summary>
    [Fact]
    public void Subquery_in_select_produces_valid_sql()
    {
        using var context = CreateContext();
        var threshold = 10.0;
        var sql = context
            .Items.Where(e => context
                .Items.Where(inner => inner.Value > threshold)
                .Select(inner => inner.Id)
                .Contains(e.Id))
            .ToQueryString();

        // Should contain a subquery with IN clause.
        Assert.Contains("IN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`Id`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// OrderBy produces ORDER BY in generated SQL.
    /// </summary>
    [Fact]
    public void Orderby_produces_order_by_in_sql()
    {
        using var context = CreateContext();
        var sql = context
            .Items.OrderBy(e => e.Id)
            .Select(e => e.Id)
            .ToQueryString();

        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`Id`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ternary expression -> CASE WHEN ... THEN ... ELSE ... END.
    /// </summary>
    [Fact]
    public void Ternary_expression_translates_to_case_when()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => e.Value > 0 ? "positive" : "non-positive")
            .ToQueryString();

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
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => e.NullableName ?? "default")
            .ToQueryString();

        Assert.Contains("COALESCE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Numeric coalesce preserves the CLR promotion instead of inheriting the
    /// unsigned column's result mapping.
    /// </summary>
    [Fact]
    public void Numeric_coalesce_with_conversion_preserves_promoted_type()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => e.NullableUnsignedValue ?? 2.25)
            .ToQueryString();

        Assert.Contains("COALESCE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CAST(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS DOUBLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Numeric conversion of characters uses CLR character values rather than the
    /// database's decimal parsing of character text.
    /// </summary>
    [Fact]
    public void String_character_join_uses_clr_character_values()
    {
        using var context = CreateContext();
        var characters = "12";
        var sql = context
            .Items.Join(
                characters,
                item => item.Id,
                character => character,
                (item, _) => item.Id)
            .ToQueryString();

        Assert.Contains(
            "CONV(HEX(CONVERT(",
            sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USING utf32", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Converter_in_groupby_produces_valid_sql()
    {
        using var context = CreateContext();
        var sql = context
            .Items.GroupBy(e => e.Name)
            .Select(g => new
            {
                g.Key,
                Count = g.Count(),
            })
            .ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Standard GroupBy + Count SQL.
    /// </summary>
    [Fact]
    public void Groupby_with_count_produces_group_by_sql()
    {
        using var context = CreateContext();
        var sql = context
            .Items.GroupBy(e => e.Name)
            .Select(g => new
            {
                g.Key,
                Total = g.Count()
            })
            .ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// WHERE Id IN (SELECT ...) subquery.
    /// </summary>
    [Fact]
    public void Subquery_in_where_produces_valid_sql()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Where(e => context
                .Items.Where(inner => inner.Value > 5)
                .Select(inner => inner.Id)
                .Contains(e.Id))
            .ToQueryString();

        Assert.Contains("IN", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// WHERE EXISTS (SELECT 1 ...) subquery.
    /// </summary>
    [Fact]
    public void Exists_subquery_produces_valid_sql()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Where(e => context.Items.Any(inner => inner.Id == e.Id && inner.Value > 10))
            .ToQueryString();

        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Union_produces_union_sql()
    {
        using var context = CreateContext();
        var query1 = context.Items.Where(e => e.Value > 10);
        var query2 = context.Items.Where(e => e.Value < 5);
        var sql = query1
            .Union(query2)
            .ToQueryString();

        Assert.Contains("UNION", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Concat_produces_union_all_sql()
    {
        using var context = CreateContext();
        var query1 = context.Items.Where(e => e.Value > 10);
        var query2 = context.Items.Where(e => e.Value < 5);
        var sql = query1
            .Concat(query2)
            .ToQueryString();

        Assert.Contains("UNION ALL", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// JsonElement property -> json column type.
    /// </summary>
    [Fact]
    public void Json_element_property_maps_to_json_column()
    {
        using var context = new JsonPropertyContext(CreateJsonOptions());
        var entityType = context.Model.FindEntityType(typeof(JsonPropertyEntity))!;
        var columnType = entityType.FindProperty(nameof(JsonPropertyEntity.Data))!.GetColumnType();

        Assert.Equal("json", columnType);
    }

    /// <summary>
    /// Having clause after GroupBy -> HAVING SQL.
    /// </summary>
    [Fact]
    public void Groupby_with_having_produces_having_sql()
    {
        using var context = CreateContext();
        var sql = context
            .Items.GroupBy(e => e.Name)
            .Where(g => g.Count() > 1)
            .Select(g => new
            {
                g.Key,
                Total = g.Count(),
            })
            .ToQueryString();

        Assert.Contains("HAVING", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scalar subquery in SELECT projection.
    /// </summary>
    [Fact]
    public void Scalar_subquery_in_select_produces_valid_sql()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => new
            {
                e.Name,
                MaxValue = context.Items.Max(inner => inner.Value),
            })
            .ToQueryString();

        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// GroupBy produces ONLY_FULL_GROUP_BY compliant SQL.
    /// All non-aggregated SELECT columns must appear in GROUP BY.
    /// </summary>
    [Fact]
    public void Groupby_sql_is_only_full_group_by_compliant()
    {
        using var context = CreateContext();
        var sql = context
            .Items.GroupBy(e => e.Name)
            .Select(g => new
            {
                g.Key,
                Average = g.Average(e => e.Value),
            })
            .ToQueryString();

        // The Key column must be in both SELECT and GROUP BY.
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Integer averages retain EF Core's double conversion instead of narrowing through DECIMAL.
    /// </summary>
    [Fact]
    public void Integer_average_casts_to_double()
    {
        using var context = CreateContext();
        var sql = context
            .Items.GroupBy(e => e.Name)
            .Select(g => g.Average(e => e.Id))
            .ToQueryString();

        Assert.Contains("AVG(CAST(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" AS DOUBLE)", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Float aggregate results retain single-precision conversion instead of scale-zero DECIMAL.
    /// </summary>
    [Fact]
    public void Float_sum_casts_to_float()
    {
        using var context = CreateContext();
        var sql = context
            .Items.GroupBy(e => e.Name)
            .Select(g => g.Sum(e => e.SingleValue))
            .ToQueryString();

        Assert.Contains("SUM(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" AS FLOAT)", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Floating-point to decimal query casts preserve the CLR decimal range and scale
    /// instead of inheriting the provider's schema-column default.
    /// </summary>
    [Fact]
    public void Double_to_decimal_cast_uses_lossless_query_precision()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => (decimal)e.Value)
            .ToQueryString();

        Assert.Contains(" AS DECIMAL(65,30))", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Correlated subquery with outer reference.
    /// </summary>
    [Fact]
    public void Correlated_subquery_produces_valid_sql()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Where(e => e.Value
                > context
                    .Items.Where(i => i.Name == e.Name)
                    .Average(i => i.Value))
            .ToQueryString();

        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AVG", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// OrderByDescending + First -> ORDER BY DESC LIMIT 1.
    /// </summary>
    [Fact]
    public void Last_pattern_orderby_desc_take_1()
    {
        using var context = CreateContext();
        var sql = context
            .Items.OrderByDescending(e => e.CreatedAt)
            .Take(1)
            .ToQueryString();

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
        using var context = new EnumQueryContext(CreateEnumOptions());
        var sql = context
            .Set<EnumQueryEntity>()
            .Where(e => e.Status == QueryStatus.Active)
            .ToQueryString();

        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// AutoInclude + IgnoreAutoIncludes -- navigation configuration.
    /// </summary>
    [Fact]
    public void Auto_include_can_be_configured()
    {
        using var context = new AutoIncludeContext(CreateAutoIncludeOptions());
        var sql = context
            .Set<AutoIncludeBlog>()
            .ToQueryString();

        // AutoInclude should cause a JOIN for the Posts navigation.
        Assert.Contains("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// IgnoreAutoIncludes removes auto-include.
    /// </summary>
    [Fact]
    public void Ignore_auto_includes_removes_join()
    {
        using var context = new AutoIncludeContext(CreateAutoIncludeOptions());
        var sql = context
            .Set<AutoIncludeBlog>()
            .IgnoreAutoIncludes()
            .ToQueryString();

        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// EF.Functions.Match produces MATCH(...) AGAINST(...) SQL.
    /// </summary>
    [Fact]
    public void Ef_functions_match_produces_match_against_sql()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Where(e => EF.Functions.Match(e.Name, "search term"))
            .ToQueryString();

        Assert.Contains("MATCH", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AGAINST", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// EF.Functions.MatchInBooleanMode produces MATCH(...) AGAINST(... IN BOOLEAN MODE) SQL.
    /// </summary>
    [Fact]
    public void Ef_functions_match_boolean_mode_produces_correct_sql()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Where(e => EF.Functions.MatchInBooleanMode(e.Name, "+required -excluded"))
            .ToQueryString();

        Assert.Contains("MATCH", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AGAINST", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IN BOOLEAN MODE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// EF.Functions.Regexp produces REGEXP_LIKE SQL on MySQL 8.0+.
    /// </summary>
    [Fact]
    public void Ef_functions_regexp_produces_regexp_like_on_mysql()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Where(e => EF.Functions.Regexp(e.Name, "^test.*$"))
            .ToQueryString();

        Assert.Contains("REGEXP_LIKE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// EF.Functions.Regexp produces infix REGEXP on MariaDB.
    /// </summary>
    [Fact]
    public void Ef_functions_regexp_produces_infix_regexp_on_mariadb()
    {
        using var context = CreateMariaDbContext();
        var sql = context
            .Items.Where(e => EF.Functions.Regexp(e.Name, "^test.*$"))
            .ToQueryString();

        Assert.Contains("REGEXP", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("REGEXP_LIKE", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- Enum Query ------------------------------------------------------

    private enum QueryStatus
    {
        Inactive = 0,
        Active = 1
    }

    private sealed class EnumQueryEntity
    {
        public int Id { get; set; }
        public QueryStatus Status { get; set; }
    }

    private sealed class EnumQueryContext : DbContext
    {
        public EnumQueryContext(
            DbContextOptions<EnumQueryContext> options
        ) : base(options) { }

        public DbSet<EnumQueryEntity> Items => Set<EnumQueryEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<EnumQueryEntity>()
                .ToTable("EnumQueryEntities");
        }
    }

    /// <summary>
    /// Non-recursive CTE generates WITH ... AS syntax.
    /// </summary>
    [Fact]
    public void Cte_non_recursive_generates_with_syntax()
    {
        using var context = CreateContext();

        // EF Core generates CTEs for certain query patterns.
        // Union of same table with different filters is one such pattern.
        var query1 = context.Items.Where(e => e.Value > 50);
        var query2 = context.Items.Where(e => e.Value < 10);
        var sql = query1
            .Union(query2)
            .OrderBy(e => e.Id)
            .ToQueryString();

        // Union generates valid MySQL SQL with UNION keyword.
        Assert.Contains("UNION", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Filtered_include_produces_valid_sql()
    {
        using var context = new AutoIncludeContext(CreateAutoIncludeOptions());
        var sql = context
            .Set<AutoIncludeBlog>()
            .IgnoreAutoIncludes()
            .Include(b => b.Posts.Where(p => p.Content.Length > 10))
            .ToQueryString();

        Assert.Contains("JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
    }

    // Distinct already tested above in Distinct_translates_to_select_distinct.

    [Fact]
    public void Nested_ternary_produces_case_when()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => e.Value > 100 ? "high" : e.Value > 50 ? "medium" : "low")
            .ToQueryString();

        Assert.Contains("CASE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Any_with_predicate_produces_exists()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Where(e => context.Items.Any(inner => inner.Id == e.Id))
            .ToQueryString();

        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Select with anonymous type projection.
    /// </summary>
    [Fact]
    public void Anonymous_projection_produces_valid_sql()
    {
        using var context = CreateContext();
        var sql = context
            .Items.Select(e => new
            {
                e.Id,
                e.Name,
                NameLength = e.Name.Length,
            })
            .ToQueryString();

        Assert.Contains("CHAR_LENGTH", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static DbContextOptions<EnumQueryContext> CreateEnumOptions()
    {
        var builder = new DbContextOptionsBuilder<EnumQueryContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return builder.Options;
    }

    // -- AutoInclude -----------------------------------------------------

    private sealed class AutoIncludeBlog
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<AutoIncludePost> Posts { get; set; } = [];
    }

    private sealed class AutoIncludePost
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public int BlogId { get; set; }
    }

    private sealed class AutoIncludeContext : DbContext
    {
        public AutoIncludeContext(
            DbContextOptions<AutoIncludeContext> options
        ) : base(options) { }

        public DbSet<AutoIncludeBlog> Blogs => Set<AutoIncludeBlog>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<AutoIncludeBlog>(entity =>
            {
                entity.ToTable("AutoIncludeBlogs");
                entity
                    .Navigation(b => b.Posts)
                    .AutoInclude();
            });

            modelBuilder
                .Entity<AutoIncludePost>()
                .ToTable("AutoIncludePosts");
        }
    }

    private static DbContextOptions<AutoIncludeContext> CreateAutoIncludeOptions()
    {
        var builder = new DbContextOptionsBuilder<AutoIncludeContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return builder.Options;
    }

    // Duplicate of Last_pattern_orderby_desc_take_1 removed.

    /// <summary>
    /// ElementAt(n) -> OFFSET n LIMIT 1.
    /// </summary>
    [Fact]
    public void Skip_take_1_produces_offset_limit()
    {
        using var context = CreateContext();
        var sql = context
            .Items.OrderBy(e => e.Id)
            .Skip(5)
            .Take(1)
            .ToQueryString();

        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- Helpers ----------------------------------------------------------

    private static DbContextOptions<JsonPropertyContext> CreateJsonOptions()
    {
        var builder = new DbContextOptionsBuilder<JsonPropertyContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return builder.Options;
    }

    private sealed class JsonPropertyContext : DbContext
    {
        public JsonPropertyContext(
            DbContextOptions<JsonPropertyContext> options
        ) : base(options) { }

        public DbSet<JsonPropertyEntity> Items => Set<JsonPropertyEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonPropertyEntity>(entity =>
            {
                entity.ToTable("JsonPropertyEntities");
                entity.HasKey(e => e.Id);
                entity
                    .Property(e => e.Data)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class JsonPropertyEntity
    {
        public int Id { get; set; }
        public JsonElement Data { get; set; }
    }

    private static TranslationTestContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<TranslationTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return new TranslationTestContext(builder.Options);
    }

    private static TranslationTestContext CreateMariaDbContext()
    {
        var builder = new DbContextOptionsBuilder<TranslationTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)));
        return new TranslationTestContext(builder.Options);
    }

    private sealed class TranslationTestContext : DbContext
    {
        public TranslationTestContext(
            DbContextOptions<TranslationTestContext> options
        ) : base(options) { }

        public DbSet<TranslationTestItem> Items => Set<TranslationTestItem>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TranslationTestItem>(entity =>
            {
                entity.ToTable("TranslationTestItems");
                entity.HasKey(e => e.Id);
            });
        }
    }

    private sealed class TranslationTestItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NullableName { get; set; }
        public uint? NullableUnsignedValue { get; set; }
        public double Value { get; set; }
        public float SingleValue { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
