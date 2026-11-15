using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies composed string parameters against bounded model columns.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterTests
{
    /// <summary>Null and inferred literal/parameter fallbacks do not compete with the consumer mapping.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB.</param>
    /// <param name="scenario">The independently reported non-authoritative filter scalar.</param>
    [Theory]
    [InlineData(false, "nullable")]
    [InlineData(true, "nullable")]
    [InlineData(false, "literal")]
    [InlineData(true, "literal")]
    [InlineData(false, "parameter")]
    [InlineData(true, "parameter")]
    public void Composed_string_parameter_ignores_filter_scalar_fallback(
        bool mariaDb,
        string scenario
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        string[] values = ["alpha", "x"];
        string?[] nullable = [null, "alpha"];
        var needle = "alpha";
        var query = scenario switch
        {
            "nullable" => context.Items.Where(item => EF.Parameter(nullable)
                .Where(value => value == null || value.Length > 1).Contains(item.OptionalName)),
            "literal" => context.Items.Where(item => EF.Parameter(values)
                .Where(value => value == "alpha").Contains(item.Name)),
            "parameter" => context.Items.Where(item => EF.Parameter(values)
                .Where(value => value == needle).Contains(item.Name)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("FROM_BASE64(", sql, StringComparison.Ordinal);
    }

    /// <summary>Uses the scalar consumer's full mapping through functions and explicit collation.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB.</param>
    /// <param name="scenario">The independently reported scalar consumer.</param>
    [Theory]
    [InlineData(false, "lower")]
    [InlineData(true, "lower")]
    [InlineData(false, "upper")]
    [InlineData(true, "upper")]
    [InlineData(false, "trim")]
    [InlineData(true, "trim")]
    [InlineData(false, "any-trim")]
    [InlineData(true, "any-trim")]
    [InlineData(false, "collate")]
    [InlineData(true, "collate")]
    [InlineData(false, "nested")]
    [InlineData(true, "nested")]
    public void Composed_string_parameter_uses_typed_scalar_consumer(
        bool mariaDb,
        string scenario
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        string[] values = ["alpha", "x"];
        var query = scenario switch
        {
            "lower" => QueryUsingCaseFunction(context.Items, values, nameof(string.ToLower)),
            "upper" => QueryUsingCaseFunction(context.Items, values, nameof(string.ToUpper)),
            "trim" => context.Items.Where(item => EF.Parameter(values).Where(value => value.Length > 1).Contains(item.Name.Trim())),
            "any-trim" => context.Items.Where(item => EF.Parameter(values).Any(value => value.Length > 1 && value == item.Name.Trim())),
            "collate" => context.Items.Where(item => EF.Parameter(values).Where(value => value.Length > 1).Contains(EF.Functions.Collate(item.Name, "utf8mb4_bin"))),
            "nested" => QueryUsingCaseFunction(
                context.Items.OrderBy(item => item.Id).Take(3), values, nameof(string.ToLower), trimFirst: true),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("FROM_BASE64(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` varchar(64)", sql, StringComparison.Ordinal);
        Assert.Contains(scenario switch
        {
            "lower" or "nested" => "LOWER(",
            "upper" => "UPPER(",
            "trim" or "any-trim" => "TRIM(",
            _ => "COLLATE utf8mb4_bin",
        }, sql, StringComparison.Ordinal);
    }

    /// <summary>Builds the exact parameterless case-function overload for server translation.</summary>
    private static IQueryable<PrimitiveCollectionEntity> QueryUsingCaseFunction(
        IQueryable<PrimitiveCollectionEntity> source,
        string[] values,
        string methodName,
        bool trimFirst = false
    )
    {
        var item = Expression.Parameter(typeof(PrimitiveCollectionEntity), "item");
        Expression name = Expression.Property(item, nameof(PrimitiveCollectionEntity.Name));

        if (trimFirst)
        {
            name = Expression.Call(name, typeof(string).GetMethod(nameof(string.Trim), Type.EmptyTypes)!);
        }

        // WHY: The provider translates this parameterless overload using database
        // collation. Building its expression explicitly avoids a client-culture
        // call and does not substitute an unsupported invariant/culture overload.
        var transformed = Expression.Call(name, typeof(string).GetMethod(methodName, Type.EmptyTypes)!);
        Expression<Func<string, bool>> membership = value => EF.Parameter(values)
            .Where(candidate => candidate.Length > 1).Contains(value);

        var body = Microsoft.EntityFrameworkCore.Query.ReplacingExpressionVisitor.Replace(
            membership.Parameters[0], transformed, membership.Body);

        return source.Where(Expression.Lambda<Func<PrimitiveCollectionEntity, bool>>(body, item));
    }

    /// <summary>Preserves bounded string inference after pagination and mapped SQL roots.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB.</param>
    /// <param name="scenario">The independently reported mapped-consumer shape.</param>
    [Theory]
    [InlineData(false, "take")]
    [InlineData(true, "take")]
    [InlineData(false, "skip-take")]
    [InlineData(true, "skip-take")]
    [InlineData(false, "any")]
    [InlineData(true, "any")]
    [InlineData(false, "from-sql")]
    [InlineData(true, "from-sql")]
    public void Composed_string_parameter_uses_mapped_relational_consumer(
        bool mariaDb,
        string scenario
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        string[] values = ["alpha", "x", new('z', 80)];
        var query = scenario switch
        {
            "take" => context.Items.OrderBy(item => item.Id).Take(3)
                .Where(item => EF.Parameter(values).Where(value => value.Length > 1).Contains(item.Name)),
            "skip-take" => context.Items.OrderBy(item => item.Id).Skip(1).Take(2)
                .Where(item => EF.Parameter(values).Where(value => value.Length > 1).Contains(item.Name)),
            "any" => context.Items.OrderBy(item => item.Id).Take(3)
                .Where(item => EF.Parameter(values).Any(value => value.Length > 1 && value == item.Name)),
            "from-sql" => context.Items.FromSqlRaw("SELECT * FROM `Items`")
                .Where(item => EF.Parameter(values).Where(value => value.Length > 1).Contains(item.Name)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("CHAR_LENGTH(JSON_UNQUOTE(JSON_QUOTE(CONVERT(FROM_BASE64(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` varchar(64)", sql, StringComparison.Ordinal);

        if (scenario != "from-sql")
        {
            Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
            Assert.True(sql.IndexOf("LIMIT", StringComparison.Ordinal) < sql.IndexOf("JSON_TABLE", StringComparison.Ordinal));
        }
    }

    /// <summary>Preserves a logical string length predicate before bounded-column membership.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Filtered_string_parameter_contains_preserves_length_and_lossless_transport(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        string[] values = ["alpha", "x", new('z', 80)];

        // Act
        var sql = context.Items
            .Where(item => EF.Parameter(values).Where(value => value.Length > 1).Contains(item.Name))
            .ToQueryString();

        // Assert
        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("CHAR_LENGTH(JSON_UNQUOTE(JSON_QUOTE(CONVERT(FROM_BASE64(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` varchar(64)", sql, StringComparison.Ordinal);
    }

    /// <summary>Preserves an inner literal comparison without conflicting with the model mapping.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Filtered_string_parameter_contains_preserves_literal_predicate(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        string[] values = ["alpha", "x"];

        // Act
        var sql = context.Items
            .Where(item => EF.Parameter(values).Where(value => value != "x").Contains(item.Name))
            .ToQueryString();

        // Assert
        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("FROM_BASE64(", sql, StringComparison.Ordinal);
        Assert.Contains("<> 'x'", sql, StringComparison.Ordinal);
    }

    /// <summary>Infers the model mapping when the authoritative comparison is inside Any.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Filtered_string_parameter_any_preserves_model_comparison(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        string[] values = ["alpha", "x"];

        // Act
        var sql = context.Items
            .Where(item => EF.Parameter(values).Any(value => value.Length > 1 && value == item.Name))
            .ToQueryString();

        // Assert
        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("CHAR_LENGTH(JSON_UNQUOTE(JSON_QUOTE(CONVERT(FROM_BASE64(", sql, StringComparison.Ordinal);
        Assert.Contains("`Name`", sql, StringComparison.Ordinal);
    }

    /// <summary>Rejects converter composition whose CLR predicate cannot commute with provider conversion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Filtered_converted_string_parameter_does_not_erase_mapping_conflict(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        string[] values = ["alpha", "x"];
        var query = context.Items.Where(item => EF.Parameter(values)
            .Where(value => value.Length > 1)
            .Contains(item.ConvertedName));

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => query.ToQueryString());

        // Assert
        Assert.Contains("Conflicting type mappings", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Pagination does not make a non-commuting converter safe to normalize.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Derived_converted_string_parameter_keeps_mapping_conflict(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        string[] values = ["alpha", "x"];
        var query = context.Items.OrderBy(item => item.Id).Take(3)
            .Where(item => EF.Parameter(values).Where(value => value.Length > 1).Contains(item.ConvertedName));

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => query.ToQueryString());

        // Assert
        Assert.Contains("Conflicting type mappings", exception.Message, StringComparison.Ordinal);
    }
}
