namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Shares the like responsibility without changing the test fixture.</summary>
public sealed partial class MySqlQueryTranslationCoverageTests
{
    // DateTime.Now and DateTime.UtcNow already tested in MySqlQueryTranslationExtendedTests.

    // -- Binary GUID and signed-integral translations --

    /// <summary>
    /// Verifies that binary GUID formatting remains server-side and reconstructs
    /// the canonical dashed representation instead of casting binary bytes to text.
    /// </summary>
    [Fact]
    public void Guid_ToString_translates_binary_value_to_canonical_text()
    {
        // Arrange
        using var context = CreateContext();
        var expected = "00112233-4455-6677-8899-aabbccddeeff";

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.Token.ToString() == expected)
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext(isMariaDb);

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Where(entity => EF.Functions.Like(entity.SignedValue, "%12%")
                && EF.Functions.Like(entity.CreatedAt, "2025-08%"))
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext(isMariaDb);

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Where(entity => EF.Functions.Like(entity.OptionalNumber, "12!_%", "!")
                || EF.Functions.Like(entity.OptionalCreatedAt, "2025-08%"))
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext(isMariaDb);

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Where(entity => EF.Functions.Like(entity.Token, "00112233-%")
                && EF.Functions.Like(entity.TextToken, "00112233-%"))
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext(isMariaDb);

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Where(entity => MySqlDbFunctionsExtensions.Like<string>(EF.Functions, entity.Name, "A%"))
            .ToQueryString();

        // Assert
        Assert.Contains("`c`.`Name` LIKE 'A%'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the generic API rejects unsupported scalar types instead
    /// of falling back to object formatting or client evaluation.
    /// </summary>
    [Theory]
    [InlineData("date")]
    [InlineData("time")]
    [InlineData("duration")]
    [InlineData("boolean")]
    [InlineData("binary")]
    [InlineData("custom")]
    [InlineData("object")]
    public void Generic_Like_rejects_unsupported_types(
        string shape
    )
    {
        // Arrange
        using var context = CreateContext();
        var entities = context.Set<CoverageEntity>();
        var (query, clrType) = shape switch
        {
            "date" => (entities.Where(entity => EF.Functions.Like(entity.BirthDate, "%")), typeof(DateOnly)),
            "time" => (entities.Where(entity => EF.Functions.Like(entity.StartTime, "%")), typeof(TimeOnly)),
            "duration" => (entities.Where(entity => EF.Functions.Like(entity.Duration, "%")), typeof(TimeSpan)),
            "boolean" => (entities.Where(entity => EF.Functions.Like(entity.IsActive, "%")), typeof(bool)),
            "binary" => (entities.Where(entity => EF.Functions.Like(entity.BinaryData, "%")), typeof(byte[])),
            "custom" => (entities.Where(entity => EF.Functions.Like(entity.CustomValue, "%")), typeof(UnsupportedLikeValue)),
            "object" => (
                entities.Where(entity => EF.Functions.Like(EF.Property<object>(entity, nameof(CoverageEntity.Name)), "%")),
                typeof(object)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var failure = Record.Exception(() => query.ToQueryString());

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains(
            $"The generic LIKE translation does not support CLR type '{clrType.FullName}'.",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the generic API cannot execute an accidental client-side call.
    /// </summary>
    [Theory]
    [InlineData("integer")]
    [InlineData("nullable-integer")]
    [InlineData("guid")]
    [InlineData("nullable-string")]
    public void Generic_Like_rejects_client_side_execution(
        string shape
    )
    {
        // Arrange
        Action invoke = shape switch
        {
            "integer" => () => EF.Functions.Like(123, "%23%"),
            "nullable-integer" => () => EF.Functions.Like<int?>(null, "%"),
            "guid" => () => EF.Functions.Like(Guid.Empty, "%", "!"),
            "nullable-string" => () => EF.Functions.Like<string?>(null, "%", null),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var exception = Record.Exception(invoke);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>
    /// Verifies that explicit generic string calls produce the same SQL as
    /// EF Core's non-generic string API, including nullable strings.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void String_Like_generic_and_non_generic_calls_produce_the_same_SQL(
        bool optional
    )
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var expected = optional
            ? context.Set<CoverageEntity>()
                .Where(entity => DbFunctionsExtensions.Like(EF.Functions, entity.OptionalText!, "A%", "!"))
                .ToQueryString()
            : context.Set<CoverageEntity>()
                .Where(entity => DbFunctionsExtensions.Like(EF.Functions, entity.Name, "A%"))
                .ToQueryString();

        var actual = optional
            ? context.Set<CoverageEntity>()
                .Where(entity => EF.Functions.Like<string?>(entity.OptionalText, "A%", "!"))
                .ToQueryString()
            : context.Set<CoverageEntity>()
                .Where(entity => EF.Functions.Like<string>(entity.Name, "A%"))
                .ToQueryString();

        // Assert
        Assert.Equal(expected, actual);
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
        // Arrange
        using var context = CreateContext(isMariaDb);
        var pattern = "%' OR 1=1 --";
        var escapeCharacter = "!";

        // Act
        var sql = context.Set<CoverageEntity>()
            .Where(entity => EF.Functions.Like(entity.OptionalToken, pattern, escapeCharacter)
                || EF.Functions.Like(entity.TextToken, pattern, escapeCharacter)
                || EF.Functions.Like(entity.SignedValue, pattern, escapeCharacter))
            .ToQueryString();

        // Assert
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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Generic_Like_formats_a_guid_parameter_before_a_column_pattern(
        bool isMariaDb,
        bool optional
    )
    {
        // Arrange
        using var context = CreateContext(isMariaDb);
        var token = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        Guid? optionalToken = token;
        var expectedParameter = optional ? "@optionalToken" : "@token";

        // Act
        var sql = optional
            ? context.Set<CoverageEntity>()
                .Where(entity => EF.Functions.Like(optionalToken, entity.Name))
                .Select(entity => entity.Id).ToQueryString()
            : context.Set<CoverageEntity>()
                .Where(entity => EF.Functions.Like(token, entity.Name))
                .Select(entity => entity.Id).ToQueryString();

        // Assert
        Assert.Contains($"HEX({expectedParameter})", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LOWER(CONCAT(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIKE `c`.`Name`", sql, StringComparison.Ordinal);
        Assert.InRange(sql.Length, 1, 1024);

    }

}
