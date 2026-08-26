namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Comprehensive coverage tests for all query translations that were previously untested:
/// DateTime.Add*, TimeSpan members, string methods, math functions, GROUP_CONCAT, edge cases.
/// </summary>
public sealed class MySqlQueryTranslationCoverageTests
{
    // -- DateTime.Add* Translations --

    [Fact]
    public void DateTime_AddYears_translates_to_interval_year()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddYears(1) > DateTime.Now)
            .ToQueryString();

        MySqlSqlAssert.ContainsDateAdd(sql, "YEAR");
    }

    [Fact]
    public void DateTime_AddMonths_translates_to_interval_month()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddMonths(6) > DateTime.Now)
            .ToQueryString();

        MySqlSqlAssert.ContainsDateAdd(sql, "MONTH");
    }

    [Fact]
    public void DateTime_AddDays_translates_to_interval_day()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddDays(30) > DateTime.Now)
            .ToQueryString();

        MySqlSqlAssert.ContainsDateAdd(sql, "DAY");
    }

    [Fact]
    public void DateTime_AddHours_translates_to_interval_hour()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddHours(12) > DateTime.Now)
            .ToQueryString();

        MySqlSqlAssert.ContainsDateAdd(sql, "HOUR");
    }

    [Fact]
    public void DateTime_AddMinutes_translates_to_interval_minute()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddMinutes(45) > DateTime.Now)
            .ToQueryString();

        MySqlSqlAssert.ContainsDateAdd(sql, "MINUTE");
    }

    [Fact]
    public void DateTime_AddSeconds_translates_to_interval_second()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddSeconds(90) > DateTime.Now)
            .ToQueryString();

        MySqlSqlAssert.ContainsDateAdd(sql, "SECOND");
    }

    // -- TimeSpan Member Translations --

    [Fact]
    public void TimeSpan_TotalSeconds_translates_to_time_to_sec()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Duration.TotalSeconds)
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "TIME_TO_SEC");
    }

    [Fact]
    public void TimeSpan_TotalMinutes_translates_to_time_to_sec_divided()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Duration.TotalMinutes)
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "TIME_TO_SEC");

        Assert.Contains("60", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void TimeSpan_TotalHours_translates_to_time_to_sec_divided()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Duration.TotalHours)
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "TIME_TO_SEC");

        Assert.Contains("3600", sql, StringComparison.Ordinal);
    }

    // -- String Method Translations --

    [Fact]
    public void String_TrimStart_translates_to_ltrim()
    {
        using var context = CreateContext();

        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Name.TrimStart())
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "LTRIM");
    }

    [Fact]
    public void String_TrimEnd_translates_to_rtrim()
    {
        using var context = CreateContext();

        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Name.TrimEnd())
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "RTRIM");
    }

    [Fact]
    public void String_PadLeft_translates_to_lpad()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Name.PadLeft(10, '0'))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "LPAD");
    }

    [Fact]
    public void String_PadRight_translates_to_rpad()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Name.PadRight(10, '_'))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "RPAD");
    }

    [Fact]
    public void String_Substring_two_args_translates_to_mysql_substring()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Name.Substring(2, 5))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "SUBSTRING");
    }

    [Fact]
    public void String_Concat_three_args_translates_to_concat()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => string.Concat(e.Name, " ", e.Name))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "CONCAT");
    }

    [Fact]
    public void String_Equals_instance_translates_server_side()
    {
        using var context = CreateContext();

        // string.Equals inside an IQueryable expression -- EF translates it to a
        // server-side equality comparison; CLR StringComparison is not consulted.
#pragma warning disable CA1309
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.Name.Equals("test"))
            .ToQueryString();
#pragma warning restore CA1309

        Assert.Contains("`Name`", sql, StringComparison.Ordinal);
        Assert.Contains("=", sql, StringComparison.Ordinal);
    }

    // -- Math Function Translations --

    [Fact]
    public void Math_Log10_translates_to_log10()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Log10(e.Score))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "LOG10");
    }

    [Fact]
    public void Math_Exp_translates_to_exp()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Exp(e.Score))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "EXP");
    }

    // Math.Sign already tested in MySqlQueryTranslationExtendedTests.

    [Fact]
    public void Math_Sin_translates_to_sin()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Sin(e.Score))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "SIN");
    }

    [Fact]
    public void Math_Cos_translates_to_cos()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Cos(e.Score))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "COS");
    }

    [Fact]
    public void Math_Tan_translates_to_tan()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Tan(e.Score))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "TAN");
    }

    [Fact]
    public void Math_Atan2_translates_to_atan2()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Atan2(e.Score, 1.0))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "ATAN2");
    }

    // Math.Pow, Math.Sqrt, Math.Log (1-arg) already tested in MySqlQueryTranslationExtendedTests.

    [Fact]
    public void Math_Log_two_args_translates_to_log()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Log(e.Score, 2.0))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "LOG");
    }

    [Fact]
    public void Math_Round_one_arg_translates_to_round()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Round(e.Score))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "ROUND");
    }

    // -- GROUP_CONCAT --

    [Fact]
    public void String_Join_translates_to_group_concat()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .GroupBy(e => e.Category)
            .Select(g => new
            {
                Category = g.Key,
                Names = string.Join(", ", g.Select(e => e.Name))
            })
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "GROUP_CONCAT");

        Assert.Contains("SEPARATOR", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GROUP_CONCAT(`c`.`Name`, ", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that multiple aggregate orderings retain their declared order
    /// inside MySQL's GROUP_CONCAT grammar.
    /// </summary>
    [Fact]
    public void String_Join_with_multiple_orderings_translates_to_group_concat()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .GroupBy(e => e.Category)
            .Select(g => string.Join(
                ", ",
                g
                    .OrderBy(e => e.Name)
                    .ThenByDescending(e => e.Id)
                    .Select(e => e.Name)))
            .ToQueryString();

        Assert.Contains("ORDER BY `c`.`Name` ASC, `c`.`Id` DESC", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- DateTime member translations --

    [Fact]
    public void DateTime_Day_translates_to_day_function()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.CreatedAt.Day)
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "DAY");
    }

    [Fact]
    public void DateTime_Hour_translates_to_hour_function()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.CreatedAt.Hour)
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "HOUR");
    }

    [Fact]
    public void DateTime_Minute_translates_to_minute_function()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.CreatedAt.Minute)
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "MINUTE");
    }

    [Fact]
    public void DateTime_Microsecond_translates_to_engine_component()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(entity => entity.CreatedAt.Microsecond)
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "MICROSECOND");

        Assert.Contains("% 1000", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DateTime_Nanosecond_preserves_engine_precision_boundary()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(entity => entity.CreatedAt.Nanosecond)
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "MICROSECOND");

        Assert.Contains("* 1000", sql, StringComparison.Ordinal);
        Assert.Contains("% 1000", sql, StringComparison.Ordinal);
    }

    // DateTime.Now and DateTime.UtcNow already tested in MySqlQueryTranslationExtendedTests.

    // -- Binary GUID and signed-integral translations --

    /// <summary>
    /// Verifies that binary GUID formatting remains server-side and reconstructs
    /// the canonical dashed representation instead of casting binary bytes to text.
    /// </summary>
    [Fact]
    public void Guid_ToString_translates_binary_value_to_canonical_text()
    {
        using var context = CreateContext();
        var expected = "00112233-4455-6677-8899-aabbccddeeff";
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.Token.ToString() == expected)
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "HEX");

        Assert.Contains("LOWER(CONCAT(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SUBSTRING(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@expected", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that numeric and temporal generic LIKE operands retain their
    /// native SQL expressions without a provider-side text conversion.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generic_Like_translates_numeric_and_datetime_values_directly(
        bool isMariaDb
    )
    {
        using var context = CreateContext(isMariaDb);
        var sql = context
            .Set<CoverageEntity>()
            .Where(entity => EF.Functions.Like(entity.SignedValue, "%12%")
                && EF.Functions.Like(entity.CreatedAt, "2025-08%"))
            .ToQueryString();

        Assert.Contains("`c`.`SignedValue` LIKE '%12%'", sql, StringComparison.Ordinal);
        Assert.Contains("`c`.`CreatedAt` LIKE '2025-08%'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CAST(`c`.`SignedValue`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CAST(`c`.`CreatedAt`", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that nullable operands retain SQL null semantics and that the
    /// escape overload forwards the declared escape character.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generic_Like_translates_nullable_values_and_escape_character(
        bool isMariaDb
    )
    {
        using var context = CreateContext(isMariaDb);
        var sql = context
            .Set<CoverageEntity>()
            .Where(entity => EF.Functions.Like(entity.OptionalNumber, "12!_%", "!")
                || EF.Functions.Like(entity.OptionalCreatedAt, "2025-08%"))
            .ToQueryString();

        Assert.Contains("`c`.`OptionalNumber` LIKE '12!_%' ESCAPE '!'", sql, StringComparison.Ordinal);
        Assert.Contains("`c`.`OptionalCreatedAt` LIKE '2025-08%'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COALESCE(", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that binary GUID LIKE reuses canonical GUID formatting while a
    /// text GUID remains a direct LIKE operand.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generic_Like_honors_binary_and_text_guid_mappings(
        bool isMariaDb
    )
    {
        using var context = CreateContext(isMariaDb);
        var sql = context
            .Set<CoverageEntity>()
            .Where(entity => EF.Functions.Like(entity.Token, "00112233-%")
                && EF.Functions.Like(entity.TextToken, "00112233-%"))
            .ToQueryString();

        Assert.Contains("HEX(`c`.`Token`)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LOWER(CONCAT(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`c`.`TextToken` LIKE '00112233-%'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("HEX(`c`.`TextToken`)", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that an explicitly generic string call retains the standard
    /// server-side string LIKE behavior.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Explicit_generic_string_Like_translates_directly(
        bool isMariaDb
    )
    {
        using var context = CreateContext(isMariaDb);
        var sql = context
            .Set<CoverageEntity>()
            .Where(entity => MySqlDbFunctionsExtensions.Like<string>(EF.Functions, entity.Name, "A%"))
            .ToQueryString();

        Assert.Contains("`c`.`Name` LIKE 'A%'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the generic API rejects unsupported scalar types instead
    /// of falling back to object formatting or client evaluation.
    /// </summary>
    [Fact]
    public void Generic_Like_rejects_unsupported_types()
    {
        using var context = CreateContext();

        AssertUnsupportedLike<DateOnly>(context, nameof(CoverageEntity.BirthDate));
        AssertUnsupportedLike<TimeOnly>(context, nameof(CoverageEntity.StartTime));
        AssertUnsupportedLike<TimeSpan>(context, nameof(CoverageEntity.Duration));
        AssertUnsupportedLike<bool>(context, nameof(CoverageEntity.IsActive));
        AssertUnsupportedLike<byte[]>(context, nameof(CoverageEntity.BinaryData));
        AssertUnsupportedLike<UnsupportedLikeValue>(context, nameof(CoverageEntity.CustomValue));
        AssertUnsupportedLike<object>(context, nameof(CoverageEntity.Name));
    }

    /// <summary>
    /// Verifies that the generic API cannot execute an accidental client-side call.
    /// </summary>
    [Fact]
    public void Generic_Like_rejects_client_side_execution()
    {
        Assert.Throws<InvalidOperationException>(() => EF.Functions.Like(123, "%23%"));
        Assert.Throws<InvalidOperationException>(() => EF.Functions.Like<int?>(null, "%"));
        Assert.Throws<InvalidOperationException>(() => EF.Functions.Like(Guid.Empty, "%", "!"));
        Assert.Throws<InvalidOperationException>(() => EF.Functions.Like<string?>(null, "%", null));
    }

    /// <summary>
    /// Verifies that explicit generic string calls produce the same SQL as
    /// EF Core's non-generic string API, including nullable strings.
    /// </summary>
    [Fact]
    public void String_Like_generic_and_non_generic_calls_produce_the_same_SQL()
    {
        System.Linq.Expressions.Expression<Func<CoverageEntity, bool>> standard =
            entity => DbFunctionsExtensions.Like(EF.Functions, entity.Name, "A%");

        System.Linq.Expressions.Expression<Func<CoverageEntity, bool>> nullable =
            entity => DbFunctionsExtensions.Like(EF.Functions, entity.OptionalText!, "A%", "!");

        using var context = CreateContext();
        Assert.Equal(
            context.Set<CoverageEntity>().Where(standard).ToQueryString(),
            context.Set<CoverageEntity>().Where(entity => EF.Functions.Like<string>(entity.Name, "A%")).ToQueryString());
        Assert.Equal(
            context.Set<CoverageEntity>().Where(nullable).ToQueryString(),
            context.Set<CoverageEntity>()
                .Where(entity => EF.Functions.Like<string?>(entity.OptionalText, "A%", "!"))
                .ToQueryString());
    }

    /// <summary>
    /// Verifies parameter mapping without applying numeric or GUID converters
    /// to a string pattern or escape parameter.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generic_Like_parameterizes_patterns_and_escape_characters(
        bool isMariaDb
    )
    {
        using var context = CreateContext(isMariaDb);
        var pattern = "%' OR 1=1 --";
        var escapeCharacter = "!";
        var sql = context.Set<CoverageEntity>()
            .Where(entity => EF.Functions.Like(entity.OptionalToken, pattern, escapeCharacter)
                || EF.Functions.Like(entity.TextToken, pattern, escapeCharacter)
                || EF.Functions.Like(entity.SignedValue, pattern, escapeCharacter))
            .ToQueryString();

        Assert.Contains("LIKE @pattern", sql, StringComparison.Ordinal);
        Assert.Contains("ESCAPE @escapeCharacter", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("LIKE '%", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COALESCE(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HEX(`c`.`OptionalToken`)", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that GUID parameters use canonical text even when the pattern
    /// is a column and the match operand is not a mapped property.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generic_Like_formats_a_guid_parameter_before_a_column_pattern(
        bool isMariaDb
    )
    {
        using var context = CreateContext(isMariaDb);
        var token = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        var sql = context.Set<CoverageEntity>()
            .Where(entity => EF.Functions.Like(token, entity.Name))
            .Select(entity => entity.Id)
            .ToQueryString();

        Assert.Contains("HEX(@token)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LOWER(CONCAT(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIKE `c`.`Name`", sql, StringComparison.Ordinal);
        Assert.InRange(sql.Length, 1, 1024);

        Guid? optionalToken = token;
        var nullableSql = context.Set<CoverageEntity>()
            .Where(entity => EF.Functions.Like(optionalToken, entity.Name))
            .Select(entity => entity.Id)
            .ToQueryString();

        Assert.Contains("HEX(@optionalToken)", nullableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIKE `c`.`Name`", nullableSql, StringComparison.Ordinal);
        Assert.InRange(nullableSql.Length, 1, 1024);
    }

    private static void AssertUnsupportedLike<T>(
        CoverageContext context,
        string propertyName
    )
    {
        var exception = Assert.Throws<InvalidOperationException>(() => context
            .Set<CoverageEntity>()
            .Where(entity => EF.Functions.Like(EF.Property<T>(entity, propertyName), "%"))
            .ToQueryString());

        Assert.Contains(
            $"The generic LIKE translation does not support CLR type '{typeof(T).FullName}'.",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that signed and unsigned shifts apply CLR width masks and that
    /// complement operations do not leak the engines' unsigned 64-bit results.
    /// </summary>
    [Fact]
    public void Signed_integral_bitwise_operations_preserve_clr_semantics()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => new
            {
                SignedIntLeft = e.SignedValue << e.ShiftCount,
                SignedIntRight = e.SignedValue >> e.ShiftCount,
                SignedLongLeft = e.SignedLongValue << e.ShiftCount,
                SignedLongRight = e.SignedLongValue >> e.ShiftCount,
                UnsignedIntLeft = e.UnsignedValue << e.ShiftCount,
                UnsignedIntRight = e.UnsignedValue >> e.ShiftCount,
                UnsignedLongLeft = e.UnsignedLongValue << e.ShiftCount,
                UnsignedLongRight = e.UnsignedLongValue >> e.ShiftCount,
                Complement = ~e.SignedValue,
                And = e.SignedValue & e.AltId,
                Or = e.SignedValue | e.AltId,
                Xor = e.SignedValue ^ e.AltId,
            })
            .ToQueryString();

        Assert.Contains("CASE WHEN `c`.`SignedValue` < 0", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS SIGNED) <<", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS SIGNED) END", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" & 31)", sql, StringComparison.Ordinal);
        Assert.Contains(" & 63)", sql, StringComparison.Ordinal);
        Assert.Contains("4294967295", sql, StringComparison.Ordinal);
        Assert.Contains("2147483648", sql, StringComparison.Ordinal);
        Assert.Contains(" & ", sql, StringComparison.Ordinal);
        Assert.Contains(" | ", sql, StringComparison.Ordinal);
        Assert.Contains(" ^ ", sql, StringComparison.Ordinal);
    }

    // -- Temporal component and precision translations --

    /// <summary>
    /// Verifies every stored TimeSpan total and component family in a single
    /// server-side projection, including the engines' microsecond boundary.
    /// </summary>
    [Fact]
    public void TimeSpan_components_and_totals_translate_server_side()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => new
            {
                e.Duration.Days,
                e.Duration.Hours,
                e.Duration.Minutes,
                e.Duration.Seconds,
                e.Duration.Milliseconds,
                e.Duration.Microseconds,
                e.Duration.Nanoseconds,
                e.Duration.TotalDays,
                e.Duration.TotalHours,
                e.Duration.TotalMinutes,
                e.Duration.TotalSeconds,
                e.Duration.TotalMilliseconds,
                e.Duration.TotalMicroseconds,
                e.Duration.TotalNanoseconds,
            })
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "TIME_TO_SEC");
        MySqlSqlAssert.ContainsFunction(sql, "MICROSECOND");

        Assert.Contains("1000000000", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that tick-backed TimeSpan members produced by DateTime subtraction
    /// preserve long ranges without routing through the engines' TIME type.
    /// </summary>
    [Fact]
    public void DateTime_difference_components_translate_from_ticks()
    {
        using var context = CreateContext();
        var baseline = new DateTime(2000, 1, 1);
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => new
            {
                Days = (e.CreatedAt - baseline).Days,
                Hours = (e.CreatedAt - baseline).Hours,
                Minutes = (e.CreatedAt - baseline).Minutes,
                Seconds = (e.CreatedAt - baseline).Seconds,
                Milliseconds = (e.CreatedAt - baseline).Milliseconds,
                Microseconds = (e.CreatedAt - baseline).Microseconds,
                Nanoseconds = (e.CreatedAt - baseline).Nanoseconds,
                TotalDays = (e.CreatedAt - baseline).TotalDays,
                TotalHours = (e.CreatedAt - baseline).TotalHours,
                TotalMinutes = (e.CreatedAt - baseline).TotalMinutes,
                TotalSeconds = (e.CreatedAt - baseline).TotalSeconds,
                TotalMilliseconds = (e.CreatedAt - baseline).TotalMilliseconds,
                TotalMicroseconds = (e.CreatedAt - baseline).TotalMicroseconds,
                TotalNanoseconds = (e.CreatedAt - baseline).TotalNanoseconds,
            })
            .ToQueryString();

        Assert.Contains("TIMESTAMPDIFF(MICROSECOND", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("* 100.0", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("TIME(", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies the actual translator path for a column value. Constant and
    /// parameter Parse inputs are evaluated before SQL generation by EF Core.
    /// </summary>
    [Fact]
    public void DateTime_Parse_from_column_translates_to_str_to_date()
    {
        using var context = CreateContext();
        var entity = System.Linq.Expressions.Expression.Parameter(
            typeof(CoverageEntity),
            "entity");

        var name = System.Linq.Expressions.Expression.Property(
            entity,
            nameof(CoverageEntity.Name));

        var createdAt = System.Linq.Expressions.Expression.Property(
            entity,
            nameof(CoverageEntity.CreatedAt));

        var parse = System.Linq.Expressions.Expression.Call(
            typeof(DateTime).GetMethod(
                nameof(DateTime.Parse),
                [typeof(string)])!,
            name);

        var predicate = System.Linq.Expressions.Expression.Lambda<Func<CoverageEntity, bool>>(
            System.Linq.Expressions.Expression.GreaterThanOrEqual(parse, createdAt),
            entity);

        var sql = context
            .Set<CoverageEntity>()
            .Where(predicate)
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "STR_TO_DATE");

        Assert.Contains("%c/%e/%Y %H:%i:%s", sql, StringComparison.Ordinal);
    }

    // -- Helpers --

    private static CoverageContext CreateContext(
        bool isMariaDb = false
    )
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<CoverageContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            isMariaDb
                ? MySqlServerVersion.MariaDb(new Version(11, 4, 0))
                : MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return new CoverageContext(builder.Options);
    }

    private sealed class CoverageEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? OptionalText { get; set; }
        public string Category { get; set; } = string.Empty;
        public double Score { get; set; }
        public double AltScore { get; set; }
        public int AltId { get; set; }
        public int SignedValue { get; set; }
        public long SignedLongValue { get; set; }
        public uint UnsignedValue { get; set; }
        public ulong UnsignedLongValue { get; set; }
        public int ShiftCount { get; set; }
        public Guid Token { get; set; }
        public Guid TextToken { get; set; }
        public Guid? OptionalToken { get; set; }
        public int? OptionalNumber { get; set; }
        public DateTime? OptionalCreatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateOnly BirthDate { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeSpan Duration { get; set; }
        public bool IsActive { get; set; }
        public byte[] BinaryData { get; set; } = [];
        public UnsupportedLikeValue CustomValue { get; set; }
    }

    private enum UnsupportedLikeValue
    {
        First,
        Second,
    }

    private sealed class CoverageContext : DbContext
    {
        public CoverageContext(
            DbContextOptions<CoverageContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<CoverageEntity>(e =>
            {
                e.ToTable("CoverageEntities");
                e.HasKey(x => x.Id);
                e.Property(x => x.CustomValue).HasConversion<int>();
                e
                    .Property(x => x.TextToken)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
            });
        }
    }
}
