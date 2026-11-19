namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies escape-safe native string membership and its exact engine boundary.</summary>
public sealed class MySqlJsonStringMembershipSqlTests
{
    /// <summary>Only affected engine versions use raw JSON extraction and exact decoded comparison.</summary>
    /// <param name="mariaDb">Whether the target is MariaDB.</param>
    /// <param name="major">The major version.</param>
    /// <param name="minor">The minor version.</param>
    /// <param name="patch">The patch version.</param>
    /// <param name="decoding">Whether decoded membership is required.</param>
    [Theory]
    [InlineData(true, 10, 11, 19, true)]
    [InlineData(true, 11, 4, 13, true)]
    [InlineData(true, 11, 8, 9, true)]
    [InlineData(true, 12, 3, 2, true)]
    [InlineData(true, 12, 3, 3, false)]
    [InlineData(true, 12, 3, 4, false)]
    [InlineData(true, 13, 0, 0, true)]
    [InlineData(true, 13, 0, 1, true)]
    [InlineData(true, 13, 0, 2, false)]
    [InlineData(false, 8, 4, 0, false)]
    [InlineData(false, 9, 7, 0, false)]
    public void String_membership_uses_the_exact_engine_fix_boundary(
        bool mariaDb,
        int major,
        int minor,
        int patch,
        bool decoding
    )
    {
        // Arrange
        var version = new Version(major, minor, patch);

        // WHY: Future-version emitter controls do not extend the supported live-engine matrix.
        var target = mariaDb
            ? MySqlServerVersion.MariaDb(version, MySqlServerVersionCompatibilityMode.AllowUnsupported)
            : MySqlServerVersion.MySql(version);

        using var context = CreateContext(target);

        // Act
        var actual = GenerateMembership(context, typeof(string), negated: false);

        // Assert
        var sql = actual.Sql;
        Assert.Equal(!decoding, target.Profile.Engine.Has(EngineCapability.JsonContainsComparesDecodedStrings));
        Assert.Equal(!decoding, sql.Contains("JSON_CONTAINS(", StringComparison.Ordinal));
        Assert.Contains("USING utf8mb4)", sql, StringComparison.Ordinal);

        if (decoding)
        {
            Assert.Contains("JSON PATH '$'", sql, StringComparison.Ordinal);
            Assert.Contains("JSON_TYPE(`members`.`value`) = 'STRING'", sql, StringComparison.Ordinal);
            Assert.Contains("CAST(JSON_UNQUOTE(`members`.`value`) AS BINARY)", sql, StringComparison.Ordinal);
            Assert.Contains("LIMIT 1", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("JSON_NORMALIZE", sql, StringComparison.Ordinal);
        }
    }

    /// <summary>Negation inverts membership without treating a SQL-null document as an empty array.</summary>
    /// <param name="negated">Whether to invert the membership result.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decoded_membership_preserves_negation_nulls_and_source_metadata(
        bool negated
    )
    {
        // Arrange
        using var context = CreateContext(MySqlServerVersion.MariaDb(new Version(11, 8, 9)));

        // Act
        var actual = GenerateMembership(context, typeof(string), negated);

        // Assert
        var sql = actual.Sql;
        Assert.Contains("IS NULL THEN NULL ELSE", sql, StringComparison.Ordinal);
        Assert.Contains("IS NULL OR JSON_TYPE(`members`.`value`) = 'NULL'", sql, StringComparison.Ordinal);
        Assert.Contains(negated
                ? ") IS NULL END)"
                : ") IS NOT NULL END)",
            sql,
            StringComparison.Ordinal);

        Assert.DoesNotContain("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.False(actual.Source.ColumnInfos![0].AsJson);
    }

    /// <summary>The verified string escape defect does not reroute numeric or boolean candidates.</summary>
    /// <param name="boolean">Whether to generate a boolean rather than integer candidate.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Non_string_membership_keeps_its_native_sql(
        bool boolean
    )
    {
        // Arrange
        using var context = CreateContext(MySqlServerVersion.MariaDb(new Version(11, 8, 9)));

        // Act
        var actual = GenerateMembership(context,
            boolean
                ? typeof(bool)
                : typeof(int),
            negated: false);

        // Assert
        var sql = actual.Sql;
        Assert.Contains("JSON_CONTAINS(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_TABLE(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("AS BINARY", sql, StringComparison.Ordinal);
    }

    /// <summary>Characters use decoded string equality without changing their JSON scalar type.</summary>
    [Fact]
    public void Character_membership_uses_decoded_string_equality()
    {
        // Arrange
        using var context = CreateContext(MySqlServerVersion.MariaDb(new Version(11, 8, 9)));

        // Act
        var actual = GenerateMembership(context, typeof(char), negated: false);

        // Assert
        Assert.Contains("JSON_TYPE(`members`.`value`) = 'STRING'", actual.Sql, StringComparison.Ordinal);
        Assert.Contains("AS BINARY", actual.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", actual.Sql, StringComparison.Ordinal);
    }

    /// <summary>Only the projected value descriptor is rewritten; nested paths and input metadata survive.</summary>
    /// <param name="mariaDb">Whether the engine is MariaDB instead of MySQL.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decoded_membership_preserves_paths_and_unrelated_source_descriptors(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb
            ? MySqlServerVersion.MariaDb(new Version(12, 3, 3))
            : MySqlServerVersion.MySql(new Version(8, 4, 0)));

        // Act
        var actual = GenerateMembership(context, typeof(string), negated: false, nested: true);

        // Assert
        Assert.Contains("'$.items[*]'", actual.Sql, StringComparison.Ordinal);
        Assert.Contains("`value` JSON PATH '$.name'", actual.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`unused`", actual.Sql, StringComparison.Ordinal);
        Assert.Equal(3, actual.Source.ColumnInfos!.Count);
        Assert.False(actual.Source.ColumnInfos[0].AsJson);
        Assert.Equal("name",
            Assert.Single(actual.Source.ColumnInfos[0].Path!)
                .PropertyName);

        Assert.Equal("unused", actual.Source.ColumnInfos[2].Name);
    }

    /// <summary>Builds a complete finalized SQL tree without letting LINQ fold the regression shape away.</summary>
    private static (string Sql, MySqlJsonTableExpression Source) GenerateMembership(
        DbContext context,
        Type itemType,
        bool negated,
        bool nested = false
    )
    {
        var factory = context.GetService<ISqlExpressionFactory>();
        var mapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(itemType)!;

        List<MySqlJsonTableExpression.ColumnInfo> columns =
        [
            new("value",
                mapping,
                nested
                    ? [new PathSegment("name")]
                    : null),
            new("ordinal",
                factory.Constant(1)
                    .TypeMapping!,
                ForOrdinality: true),
        ];

        if (nested)
        {
            columns.Add(new MySqlJsonTableExpression.ColumnInfo("unused", mapping));
        }

        var source = new MySqlJsonTableExpression(
            "members",
            new ColumnExpression("Tags", "outer_rows", typeof(string), MySqlStringTypeMapping.Default, nullable: true),
            path: nested
                ? [new PathSegment("items")]
                : null,
            columnInfos: columns);

        var value = new ColumnExpression("value", "members", itemType, mapping, nullable: true);
        var item = itemType == typeof(string)
            ? factory.Constant("Caf\u00e9", mapping)
            : itemType == typeof(char)
                ? factory.Constant('\u00e9', mapping)
                : itemType == typeof(bool)
                    ? factory.Constant(true, mapping)
                    : factory.Constant(1, mapping);

        var membership = factory.In(item, CreateSelect([source], value));
        var root = CreateSelect([],
            negated
                ? factory.Not(membership)
                : membership);

        var sql = context
            .GetService<IQuerySqlGeneratorFactory>()
            .Create()
            .GetCommand(root)
            .CommandText;

        return (sql, source);
    }

    /// <summary>Creates isolated provider services without opening a database connection.</summary>
    private static DbContext CreateContext(
        MySqlServerVersion version
    ) => new(new DbContextOptionsBuilder()
        .EnableServiceProviderCaching(false)

        // WHY: Transient SQL-emitter fixtures must not depend on another test's global provider count.
        .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
        .UseMySql("Server=localhost;Database=unused;User ID=unused;Password=unused;", version)
        .Options);

    /// <summary>Uses EF's relational node constructor to exercise actual generator dispatch.</summary>
    private static SelectExpression CreateSelect(
        IReadOnlyList<TableExpressionBase> tables,
        SqlExpression projection
    )
    {
        // WHY: Finalized relational nodes pin the emitter boundary independently of LINQ optimizations.
#pragma warning disable EF1001
        return new SelectExpression(
            alias: null,
            tables,
            predicate: null,
            groupBy: [],
            having: null,
            [new ProjectionExpression(projection, "value")],
            distinct: false,
            orderings: [],
            offset: null,
            limit: null,
            new SqlAliasManager());
#pragma warning restore EF1001
    }
}
