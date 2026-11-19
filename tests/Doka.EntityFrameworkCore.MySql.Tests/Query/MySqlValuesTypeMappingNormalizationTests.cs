namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies the conservative parameter-rowset inference boundary.</summary>
public sealed class MySqlValuesTypeMappingNormalizationTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddEntityFrameworkDokaMySql()
        .BuildServiceProvider();

    /// <inheritdoc />
    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Recognizes the fallback mapping and equivalent plain-string clones.</summary>
    [Theory]
    [InlineData("parameter")]
    [InlineData("equivalent-clones")]
    [InlineData("derived-consumer")]
    [InlineData("from-sql-consumer")]
    [InlineData("function-consumer")]
    [InlineData("scalar-function-consumer")]
    [InlineData("collated-consumer")]
    [InlineData("nested-scalar-consumer")]
    [InlineData("typed-constant-consumer")]
    [InlineData("typed-parameter-consumer")]
    [InlineData("no-model-context")]
    [InlineData("null-with-model")]
    [InlineData("fallback-literal-with-model")]
    [InlineData("fallback-parameter-with-model")]
    public void Untyped_parameter_fallback_is_cleared_after_consumer_mapping_consensus(
        string scenario
    )
    {
        // Arrange
        using var context = CreateContext();
        var source = context.GetService<IRelationalTypeMappingSource>();
        var tree = CreateTree(context, scenario);

        // Act
        var normalized = Assert.IsAssignableFrom<BlockExpression>(
            MySqlValuesTypeMappingNormalizingExpressionVisitor.Normalize(tree, source));

        // Assert
        Assert.NotSame(tree, normalized);
        Assert.Null(Assert.IsType<ColumnExpression>(normalized.Expressions[2])
            .TypeMapping);

        Assert.Same(tree.Expressions[0], normalized.Expressions[0]);
    }

    /// <summary>Leaves typed, wrapped, document and row-dependent sources unchanged.</summary>
    [Theory]
    [InlineData("typed-parameter")]
    [InlineData("typed-value")]
    [InlineData("document")]
    [InlineData("wrapped-parameter")]
    [InlineData("path")]
    public void Ineligible_json_sources_keep_their_explicit_contract(
        string scenario
    )
    {
        // Arrange
        using var context = CreateContext();
        var source = context.GetService<IRelationalTypeMappingSource>();
        var tree = CreateTree(context, scenario);

        // Act
        var normalized = MySqlValuesTypeMappingNormalizingExpressionVisitor.Normalize(tree, source);

        // Assert
        Assert.Same(tree, normalized);
    }

    /// <summary>Never resolves ambiguity by store-type text or first observation.</summary>
    [Theory]
    [InlineData("different-model-size")]
    [InlineData("different-model-comparer")]
    [InlineData("converted-model")]
    [InlineData("converted-fallback")]
    [InlineData("fallback-size")]
    [InlineData("third-mapping")]
    [InlineData("fallback-only-context")]
    [InlineData("missing-consumer")]
    [InlineData("json-consumer")]
    [InlineData("values-consumer")]
    [InlineData("other-parameter-consumer")]
    [InlineData("unmapped-derived-consumer")]
    [InlineData("converted-derived-consumer")]
    [InlineData("nested-unknown-alias")]
    [InlineData("mixed-json-reference")]
    [InlineData("hidden-converted-column")]
    [InlineData("hidden-converted-constant")]
    [InlineData("hidden-converted-parameter")]
    [InlineData("hidden-element-mapping")]
    [InlineData("valid-before-hidden")]
    [InlineData("hidden-before-valid")]
    [InlineData("valid-before-unknown")]
    [InlineData("unknown-before-valid")]
    [InlineData("valid-before-local")]
    [InlineData("local-before-valid")]
    [InlineData("longtext-column-with-model")]
    [InlineData("longtext-function-with-model")]
    [InlineData("converted-null-with-model")]
    [InlineData("hidden-converted-null-with-model")]
    public void Incompatible_or_unowned_mappings_are_not_normalized(
        string scenario
    )
    {
        // Arrange
        using var context = CreateContext();
        var source = context.GetService<IRelationalTypeMappingSource>();
        var tree = CreateTree(context, scenario);

        // Act
        var normalized = MySqlValuesTypeMappingNormalizingExpressionVisitor.Normalize(tree, source);

        // Assert
        Assert.Same(tree, normalized);
    }

    /// <summary>Consensus for one parameter cannot clear another alias's fallback mapping.</summary>
    [Fact]
    public void Independent_alias_without_model_context_keeps_its_mapping()
    {
        // Arrange
        using var context = CreateContext();
        var source = context.GetService<IRelationalTypeMappingSource>();
        var first = CreateTree(context, "parameter");
        var second = new MySqlJsonTableExpression(
            "other",
            new SqlParameterExpression("otherValues", typeof(string[]), typeMapping: null),
            path: null,
            columnInfos: []);

        var otherValue = new ColumnExpression("value", "other", typeof(string), MySqlStringTypeMapping.Default, true);
        var tree = Expression.Block(first, second, otherValue);

        // Act
        var normalized = Assert.IsAssignableFrom<BlockExpression>(
            MySqlValuesTypeMappingNormalizingExpressionVisitor.Normalize(tree, source));

        // Assert
        Assert.NotSame(first, normalized.Expressions[0]);
        Assert.Same(second, normalized.Expressions[1]);
        Assert.Same(otherValue, normalized.Expressions[2]);
    }

    /// <summary>Builds actual EF SQL nodes at the inference boundary without opening a database.</summary>
    private static BlockExpression CreateTree(
        NormalizationContext context,
        string scenario
    )
    {
        var fallback = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(string))!;

        var modelMapping = context.Model.FindEntityType(typeof(Item))!.FindProperty(nameof(Item.Name))!
            .GetRelationalTypeMapping();

        var rawMapping = scenario switch
        {
            "equivalent-clones" => fallback.Clone(),
            "converted-fallback" => (RelationalTypeMapping)fallback.WithComposedConverter(PrefixConverter()),
            "fallback-size" => new MySqlStringTypeMapping("longtext", DbType.String, size: 64),
            _ => fallback,
        };

        SqlExpression input = new SqlParameterExpression(
            "values",
            typeof(string[]),
            scenario == "typed-parameter"
                ? fallback
                : null);

        if (scenario == "document")
        {
            input = new ColumnExpression("Document", "model", typeof(string), fallback, true);
        }
        else if (scenario == "wrapped-parameter")
        {
            input = new SqlFunctionExpression(
                "COALESCE",
                [input, new SqlConstantExpression("[]", fallback)],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                typeof(string),
                fallback);
        }

        var rowset = new MySqlJsonTableExpression(
            "values_rows",
            input,
            scenario == "path"
                ? [new PathSegment("items")]
                : null,
            scenario == "typed-value"
                ? [new MySqlJsonTableExpression.ColumnInfo("value", fallback)]
                : []);

        var rawValue = new ColumnExpression("value", "values_rows", typeof(string), rawMapping, true);
        var comparedValue = new ColumnExpression("value", "values_rows", typeof(string), typeMapping: null, true);

        if (scenario is "converted-model" or "converted-derived-consumer")
        {
            modelMapping = (RelationalTypeMapping)modelMapping.WithComposedConverter(PrefixConverter());
        }

        var modelTable = new TableExpression("model",
            context
                .Model
                .GetRelationalModel()
                .Tables
                .Single());

        Expression consumer = scenario switch
        {
            "derived-consumer" or "unmapped-derived-consumer" or "converted-derived-consumer"
                => CreateDerivedConsumer(context, modelMapping),
            "from-sql-consumer" => new FromSqlExpression("model",
                "SELECT Name FROM Items",
                Expression.Constant(Array.Empty<object>())),
            "function-consumer" => new TableValuedFunctionExpression("model", "MappedItems", []),
            "json-consumer" => new MySqlJsonTableExpression(
                "model",
                new SqlConstantExpression("[]", fallback),
                path: null,
                columnInfos: [new MySqlJsonTableExpression.ColumnInfo("Name", modelMapping)]),
            "values-consumer" => new ValuesExpression(
                "model",
                [new RowValueExpression([new SqlConstantExpression("alpha", modelMapping)])],
                ["Name"]),
            "other-parameter-consumer" => new MySqlJsonTableExpression(
                "model",
                new SqlParameterExpression("other", typeof(string[]), typeMapping: null),
                path: null,
                columnInfos: []),
            "missing-consumer" or "typed-constant-consumer" or "typed-parameter-consumer" => Expression.Empty(),
            _ => modelTable,
        };

        var modelValue = new ColumnExpression(
            scenario == "other-parameter-consumer"
                ? "value"
                : "Name",
            "model",
            typeof(string),
            scenario == "unmapped-derived-consumer"
                ? null
                : modelMapping,
            false);

        var convertedMapping = (RelationalTypeMapping)modelMapping.WithComposedConverter(PrefixConverter());
        SqlExpression consumerValue = scenario switch
        {
            "scalar-function-consumer" => WrapScalar(modelValue, modelMapping),
            "collated-consumer" => new CollateExpression(modelValue, "utf8mb4_bin"),
            "nested-scalar-consumer" => WrapScalar(
                new CollateExpression(WrapScalar(modelValue, modelMapping), "utf8mb4_bin"),
                modelMapping),
            "typed-constant-consumer" => new SqlConstantExpression("alpha", modelMapping),
            "typed-parameter-consumer" => new SqlParameterExpression("needle", typeof(string), modelMapping),
            "nested-unknown-alias" or "valid-before-unknown" or "unknown-before-valid"
                => WrapScalar(new ColumnExpression("Name", "missing", typeof(string), modelMapping, false),
                    modelMapping),
            "mixed-json-reference" or "valid-before-local" or "local-before-valid" => new SqlFunctionExpression(
                "CONCAT",
                [modelValue, rawValue],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                typeof(string),
                modelMapping),
            "hidden-converted-column" or "valid-before-hidden" or "hidden-before-valid"
                => WrapScalar(new ColumnExpression("Name", "model", typeof(string), convertedMapping, false),
                    modelMapping),
            "hidden-converted-constant" => WrapScalar(new SqlConstantExpression("alpha", convertedMapping),
                modelMapping),
            "hidden-converted-parameter" => WrapScalar(
                new SqlParameterExpression("needle", typeof(string), convertedMapping),
                modelMapping),
            "hidden-element-mapping" => WrapScalar(
                new SqlConstantExpression("alpha", modelMapping.Clone(elementMapping: fallback)),
                modelMapping),
            "null-with-model" => new SqlConstantExpression(null, typeof(string), fallback),
            "fallback-literal-with-model" => new SqlConstantExpression("alpha", fallback),
            "fallback-parameter-with-model" => new SqlParameterExpression("needle", typeof(string), fallback),
            "longtext-column-with-model" => new ColumnExpression("LongName", "model", typeof(string), fallback, false),
            "longtext-function-with-model" => WrapScalar(
                new ColumnExpression("LongName", "model", typeof(string), fallback, false),
                fallback),
            "converted-null-with-model" => new SqlConstantExpression(null, typeof(string), convertedMapping),
            "hidden-converted-null-with-model" => WrapScalar(
                new SqlConstantExpression(null, typeof(string), convertedMapping),
                modelMapping),
            _ => modelValue,
        };

        var predicate = new SqlBinaryExpression(
            ExpressionType.Equal,
            comparedValue,
            scenario switch
            {
                "no-model-context" => new SqlConstantExpression("alpha", modelMapping),
                "fallback-only-context" => new SqlConstantExpression("alpha", fallback),
                _ => consumerValue,
            },
            typeof(bool),
            BoolTypeMapping.Default);

        List<Expression> expressions = [rowset, consumer, rawValue, predicate];

        if (scenario is "null-with-model"
            or "fallback-literal-with-model"
            or "fallback-parameter-with-model"
            or "valid-before-hidden"
            or "hidden-before-valid"
            or "valid-before-unknown"
            or "unknown-before-valid"
            or "valid-before-local"
            or "local-before-valid"
            or "longtext-column-with-model"
            or "longtext-function-with-model"
            or "converted-null-with-model"
            or "hidden-converted-null-with-model")
        {
            var valid = new SqlBinaryExpression(
                ExpressionType.Equal,
                comparedValue,
                modelValue,
                typeof(bool),
                BoolTypeMapping.Default);

            if (scenario.StartsWith("valid-before-", StringComparison.Ordinal))
            {
                expressions.Insert(3, valid);
            }
            else
            {
                expressions.Add(valid);
            }
        }

        if (scenario is "different-model-size" or "different-model-comparer" or "equivalent-clones")
        {
            var secondMapping = scenario switch
            {
                "different-model-size" => new MySqlStringTypeMapping("varchar(64)", DbType.String, size: 32),
                "different-model-comparer" => new MySqlStringTypeMapping(
                    "varchar(64)",
                    DbType.String,
                    size: 64,
                    useKeyComparison: true),
                _ => modelMapping.Clone(),
            };

            expressions.Add(new SqlBinaryExpression(
                ExpressionType.Equal,
                comparedValue,
                new ColumnExpression("OtherName", "model", typeof(string), secondMapping, false),
                typeof(bool),
                BoolTypeMapping.Default));
        }

        if (scenario == "third-mapping")
        {
            expressions.Add(new ColumnExpression(
                "value",
                "values_rows",
                typeof(string),
                new MySqlStringTypeMapping("varchar(32)", DbType.String, size: 32),
                true));
        }

        return Expression.Block(expressions);
    }

    /// <summary>Retains an explicit outer result mapping independently of its operand's mapping.</summary>
    private static SqlFunctionExpression WrapScalar(
        SqlExpression operand,
        RelationalTypeMapping mapping
    ) => new(
        "TRIM",
        [operand],
        nullable: true,
        argumentsPropagateNullability: [true],
        typeof(string),
        mapping);

    /// <summary>Represents the actual typed column exposed when EF pushes pagination into a subquery.</summary>
    private static SelectExpression CreateDerivedConsumer(
        NormalizationContext context,
        RelationalTypeMapping mapping
    )
    {
        var table = new TableExpression("inner",
            context
                .Model
                .GetRelationalModel()
                .Tables
                .Single());

        var projected = new ColumnExpression("Name", "inner", typeof(string), mapping, false);

        // WHY: These are EF's real finalized SQL nodes. A SQL fragment would bypass
        // the derived-alias inference boundary this regression must exercise.
#pragma warning disable EF1001
        return new SelectExpression(
            "model",
            [table],
            predicate: null,
            groupBy: [],
            having: null,
            [new ProjectionExpression(projected, "Name")],
            distinct: false,
            orderings: [],
            offset: null,
            limit: new SqlConstantExpression(3, IntTypeMapping.Default),
            new SqlAliasManager());
#pragma warning restore EF1001
    }

    /// <summary>Creates a deliberately non-commuting converter for rejection controls.</summary>
    private static ValueConverter<string, string> PrefixConverter() => new(
        value => "db-" + value,
        value => value.Substring(3));

    private NormalizationContext CreateContext() => new(
        new DbContextOptionsBuilder<NormalizationContext>()

            // WHY: Disabling caching still evaluates EF's process-wide provider count.
            // An explicitly owned graph isolates this SQL-tree test without weakening diagnostics.
            .UseInternalServiceProvider(_services)
            .UseMySql(
                "Server=localhost;Database=unused;User ID=root;Password=unused;",
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .Options);

    private sealed class NormalizationContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public NormalizationContext(
            DbContextOptions<NormalizationContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder
            .Entity<Item>()
            .Property(item => item.Name)
            .HasMaxLength(64);
    }

    private sealed class Item
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
