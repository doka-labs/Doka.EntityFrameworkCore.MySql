namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies composed string rowsets against immutable bounded-column data.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterIntegrationTests
{
    private static readonly string[] s_filteredStringScenarios =
    [
        "length", "inequality", "any", "nullable", "negated", "empty", "duplicates",
        "binary-collation", "case-insensitive-collation", "escapes", "unicode", "long-prefix",
        "exact-64", "decoded-length-64", "not-truncated-length-48",
        "take-before-filter", "skip-take-before-filter", "take-before-any", "derived-nullable",
        "derived-negated", "derived-long-prefix", "derived-exact-64", "derived-not-truncated-length",
        "from-sql", "derived-binary-collation", "derived-case-insensitive-collation",
        "scalar-lower", "scalar-upper", "scalar-trim", "scalar-any-trim", "scalar-collate", "scalar-nested",
        "equality-literal", "equality-parameter",
    ];

    /// <summary>Reports every semantic boundary separately on every selected supported engine.</summary>
    public static IEnumerable<object[]> FilteredStringCases => SelectedTargets.SelectMany(target =>
        s_filteredStringScenarios.Select(scenario => new[] { target[0], scenario }));

    /// <summary>Preserves decoded predicates, membership and lossless transport without modifying stored rows.</summary>
    /// <param name="targetName">The selected database engine.</param>
    /// <param name="scenario">The independently reported semantic boundary.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(FilteredStringCases))]
    public async Task Composed_string_parameter_preserves_membership_semantics(
        string targetName,
        string scenario
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);
        string[] ordinary = ["alpha", "x"];
        string[] inequality = ["alpha", "\"quoted\""];
        string?[] nullable = [null, "alpha"];
        string[] empty = [];
        string[] duplicates = ["alpha", "alpha", "x"];
        string[] upperCase = ["Alpha"];
        string[] escapes = ["\"\\q\""];
        string[] unicode = ["gr\u00FCn"];
        string[] tooLong = [new('z', 80)];
        string[] exact = [new('z', 64)];
        string[] quoted = ["\"quoted\""];
        string[] allCaps = ["ALPHA"];
        var needle = "alpha";

        const string mappedSqlRoot = "SELECT * FROM `IntPrimitiveCollectionItems`";

        var (query, expected) = scenario switch
        {
            "length" => (context.Items.Where(item => EF
                .Parameter(ordinary)
                .Where(value => value.Length > 1)
                .Contains(item.Name)), new[] { 1 }),
            "inequality" => (context.Items.Where(item => EF
                .Parameter(inequality)
                .Where(value => value != "alpha")
                .Contains(item.Name)), new[] { 2 }),
            "any" => (context.Items.Where(item => EF
                .Parameter(ordinary)
                .Any(value => value.Length > 1 && value == item.Name)), new[] { 1 }),
            "nullable" => (context.Items.Where(item => EF
                .Parameter(nullable)
                .Where(value => value == null || value.Length > 1)
                .Contains(item.OptionalName)), new[] { 1, 2 }),
            "negated" => (context.Items.Where(item => item.Id <= 3
                && !EF
                    .Parameter(ordinary)
                    .Where(value => value.Length > 1)
                    .Contains(item.Name)), new[] { 2, 3 }),
            "empty" => (context.Items.Where(item => EF
                .Parameter(empty)
                .Where(value => value.Length > 1)
                .Contains(item.Name)), Array.Empty<int>()),
            "duplicates" => (context.Items.Where(item => EF
                .Parameter(duplicates)
                .Where(value => value.Length > 1)
                .Contains(item.Name)), new[] { 1 }),
            "binary-collation" => (context.Items.Where(item => EF
                .Parameter(upperCase)
                .Where(value => value.Length > 1)
                .Contains(item.Name)), Array.Empty<int>()),
            "case-insensitive-collation" => (context.Items.Where(item => EF
                .Parameter(upperCase)
                .Where(value => value.Length > 1)
                .Contains(item.CaseInsensitiveName)), new[] { 1 }),
            "escapes" => (context.Items.Where(item => EF
                .Parameter(escapes)
                .Where(value => value.Length > 1)
                .Contains(item.Name)), new[] { 3 }),
            "unicode" => (context.Items.Where(item => EF
                .Parameter(unicode)
                .Where(value => value.Length == 4)
                .Contains(item.Name)), new[] { RowCount - 2 }),
            "long-prefix" => (context.Items.Where(item => EF
                .Parameter(tooLong)
                .Where(value => value.Length > 1)
                .Contains(item.Name)), Array.Empty<int>()),
            "exact-64" => (context.Items.Where(item => EF
                .Parameter(exact)
                .Where(value => value.Length > 1)
                .Contains(item.Name)), new[] { RowCount - 1 }),
            "decoded-length-64" => (context.Items.Where(item => EF
                .Parameter(exact)
                .Where(value => value.Length == 64)
                .Contains(item.Name)), new[] { RowCount - 1 }),
            "not-truncated-length-48" => (context.Items.Where(item => EF
                .Parameter(exact)
                .Where(value => value.Length == 48)
                .Contains(item.Name)), Array.Empty<int>()),
            "take-before-filter" => (context
                .Items
                .OrderBy(item => item.Id)
                .Take(1)
                .Where(item => EF
                    .Parameter(quoted)
                    .Where(value => value.Length > 1)
                    .Contains(item.Name)), Array.Empty<int>()),
            "skip-take-before-filter" => (context
                .Items
                .OrderBy(item => item.Id)
                .Skip(1)
                .Take(1)
                .Where(item => EF
                    .Parameter(inequality)
                    .Where(value => value != "alpha")
                    .Contains(item.Name)), new[] { 2 }),
            "take-before-any" => (context
                .Items
                .OrderBy(item => item.Id)
                .Take(1)
                .Where(item => EF
                    .Parameter(quoted)
                    .Any(value => value.Length > 1 && value == item.Name)), Array.Empty<int>()),
            "derived-nullable" => (context
                .Items
                .OrderBy(item => item.Id)
                .Skip(1)
                .Take(1)
                .Where(item => EF
                    .Parameter(nullable)
                    .Where(value => value == null || value.Length > 1)
                    .Contains(item.OptionalName)), new[] { 2 }),
            "derived-negated" => (context
                .Items
                .OrderBy(item => item.Id)
                .Take(2)
                .Where(item => !EF
                    .Parameter(ordinary)
                    .Where(value => value.Length > 1)
                    .Contains(item.Name)), new[] { 2 }),
            "derived-long-prefix" => (context
                .Items
                .OrderByDescending(item => item.Id)
                .Take(2)
                .Where(item => EF
                    .Parameter(tooLong)
                    .Where(value => value.Length > 1)
                    .Contains(item.Name)), Array.Empty<int>()),
            "derived-exact-64" => (context
                .Items
                .OrderByDescending(item => item.Id)
                .Take(2)
                .Where(item => EF
                    .Parameter(exact)
                    .Where(value => value.Length == 64)
                    .Contains(item.Name)), new[] { RowCount - 1 }),
            "derived-not-truncated-length" => (context
                .Items
                .OrderByDescending(item => item.Id)
                .Take(2)
                .Where(item => EF
                    .Parameter(exact)
                    .Where(value => value.Length == 48)
                    .Contains(item.Name)), Array.Empty<int>()),
            "from-sql" => (context
                .Items
                .FromSqlRaw(mappedSqlRoot)
                .Where(item => EF
                    .Parameter(ordinary)
                    .Where(value => value.Length > 1)
                    .Contains(item.Name)), new[] { 1 }),
            "derived-binary-collation" => (context
                .Items
                .OrderBy(item => item.Id)
                .Take(1)
                .Where(item => EF
                    .Parameter(upperCase)
                    .Where(value => value.Length > 1)
                    .Contains(item.Name)), Array.Empty<int>()),
            "derived-case-insensitive-collation" => (context
                .Items
                .OrderBy(item => item.Id)
                .Take(1)
                .Where(item => EF
                    .Parameter(upperCase)
                    .Where(value => value.Length > 1)
                    .Contains(item.CaseInsensitiveName)), new[] { 1 }),
            "scalar-lower" => (QueryUsingCaseFunction(context.Items, ordinary, nameof(string.ToLower)), new[] { 1 }),
            "scalar-upper" => (QueryUsingCaseFunction(context.Items, allCaps, nameof(string.ToUpper)), new[] { 1 }),
            "scalar-trim" => (context.Items.Where(item => EF
                .Parameter(ordinary)
                .Where(value => value.Length > 1)
                .Contains(item.Name.Trim())), new[] { 1 }),
            "scalar-any-trim" => (context.Items.Where(item => EF
                .Parameter(ordinary)
                .Any(value => value.Length > 1 && value == item.Name.Trim())), new[] { 1 }),
            "scalar-collate" => (context.Items.Where(item => EF
                .Parameter(upperCase)
                .Where(value => value.Length > 1)
                .Contains(EF.Functions.Collate(item.Name, "utf8mb4_unicode_ci"))), new[] { 1 }),
            "scalar-nested" => (QueryUsingCaseFunction(
                context
                    .Items
                    .OrderBy(item => item.Id)
                    .Take(1),
                ordinary,
                nameof(string.ToLower),
                trimFirst: true), new[] { 1 }),
            "equality-literal" => (context.Items.Where(item => EF
                .Parameter(ordinary)
                .Where(value => value == "alpha")
                .Contains(item.Name)), new[] { 1 }),
            "equality-parameter" => (context.Items.Where(item => EF
                .Parameter(ordinary)
                .Where(value => value == needle)
                .Contains(item.Name)), new[] { 1 }),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        // Act
        var ids = await SelectIdsAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(expected, ids);
    }

    /// <summary>Builds the exact parameterless case-function expression translated using database collation.</summary>
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

        // WHY: These parameterless calls are server expressions, not client-culture
        // operations. An invariant/culture overload would test a different API.
        var transformed = Expression.Call(name, typeof(string).GetMethod(methodName, Type.EmptyTypes)!);
        Expression<Func<string, bool>> membership = value => EF
            .Parameter(values)
            .Where(candidate => candidate.Length > 1)
            .Contains(value);

        var body = ReplacingExpressionVisitor.Replace(
            membership.Parameters[0],
            transformed,
            membership.Body);

        return source.Where(Expression.Lambda<Func<PrimitiveCollectionEntity, bool>>(body, item));
    }
}
