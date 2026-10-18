namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies semantic reconstruction at EF's provider SQL-expression quotation boundary.</summary>
public sealed partial class MySqlMaterializedSourceExpressionTests
{
    /// <summary>A quoted source retains its lexical identity, column schema and annotations.</summary>
    [Fact]
    [Experimental("EF9100")]
    public void Quote_preserves_binding_source_schema_and_annotations()
    {
        using var context = CreateContext();
        var source = CreateLeafSource();
        var expression = new MySqlMaterializedSourceExpression("rows", 17, source,
            new Dictionary<string, IAnnotation>
            {
                ["Doka:Reference"] = new Annotation("Doka:Reference", "reference-value"),
                ["Doka:Enabled"] = new Annotation("Doka:Enabled", true),
                ["Doka:Optional"] = new Annotation("Doka:Optional", null),
            });

        var reconstructed = RoundTrip(expression, context);

        Assert.Equal("rows", reconstructed.Alias);
        Assert.Equal(17, reconstructed.BindingId);
        Assert.Equal("leaf_seed", reconstructed.Source.Alias);
        Assert.Equal("reference-value", reconstructed.FindAnnotation("Doka:Reference")?.Value);
        Assert.Equal(true, reconstructed.FindAnnotation("Doka:Enabled")?.Value);
        Assert.NotNull(reconstructed.FindAnnotation("Doka:Optional"));
        Assert.Null(reconstructed.FindAnnotation("Doka:Optional")!.Value);
        Assert.Collection(reconstructed.Source.Projection,
            projection =>
            {
                Assert.Equal("Token", projection.Alias);
                var value = Assert.IsType<SqlConstantExpression>(projection.Expression);
                Assert.Equal(Guid.Empty, value.Value);
                Assert.IsType<MySqlGuidBinaryTypeMapping>(value.TypeMapping);
                Assert.Equal("binary(16)", value.TypeMapping!.StoreType);
            },
            projection =>
            {
                Assert.Equal("OptionalName", projection.Alias);
                var value = Assert.IsType<SqlConstantExpression>(projection.Expression);
                Assert.Null(value.Value);
                Assert.Equal(typeof(string), value.Type);
                Assert.Equal(MySqlStringTypeMapping.Default.StoreType, value.TypeMapping!.StoreType);
            });
    }

    /// <summary>Changing a table alias cannot change its statement-local identity or source columns.</summary>
    [Fact]
    [Experimental("EF9100")]
    public void Quote_of_an_aliased_reference_preserves_the_completed_source()
    {
        using var context = CreateContext();
        var original = new MySqlMaterializedSourceExpression("first", 23, CreateLeafSource());
        var aliased = (MySqlMaterializedSourceExpression)original.WithAlias("second");

        var reconstructed = RoundTrip(aliased, context);

        Assert.Equal("second", reconstructed.Alias);
        Assert.Equal(original.BindingId, reconstructed.BindingId);
        Assert.Same(original.Source, aliased.Source);
        Assert.Equal(original.Source.Projection.Select(projection => projection.Alias),
            reconstructed.Source.Projection.Select(projection => projection.Alias));
        Assert.Equal("first", original.Alias);
    }

    /// <summary>Nested references preserve both aliases of one logical source and their typed column links.</summary>
    [Fact]
    [Experimental("EF9100")]
    public void Quote_preserves_nested_source_references_and_projection_links()
    {
        using var context = CreateContext();
        var expression = CreateNestedSource();

        var reconstructed = RoundTrip(expression, context);

        Assert.Equal(32, reconstructed.BindingId);
        Assert.Equal("pair_seed", reconstructed.Source.Alias);
        Assert.Collection(reconstructed.Source.Tables,
            table => AssertNestedReference(table, "first", 31),
            table => AssertNestedReference(table, "second", 31));
        var first = Assert.IsType<MySqlMaterializedSourceExpression>(reconstructed.Source.Tables[0]);
        var second = Assert.IsType<MySqlMaterializedSourceExpression>(reconstructed.Source.Tables[1]);
        Assert.Same(first.Definition, second.Definition);
        Assert.Collection(reconstructed.Source.Projection,
            projection =>
            {
                Assert.Equal("FirstToken", projection.Alias);
                var column = Assert.IsType<ColumnExpression>(projection.Expression);
                Assert.Equal("first", column.TableAlias);
                Assert.Equal("Token", column.Name);
                Assert.False(column.IsNullable);
                Assert.IsType<MySqlGuidBinaryTypeMapping>(column.TypeMapping);
            },
            projection =>
            {
                Assert.Equal("SecondName", projection.Alias);
                var column = Assert.IsType<ColumnExpression>(projection.Expression);
                Assert.Equal("second", column.TableAlias);
                Assert.Equal("OptionalName", column.Name);
                Assert.True(column.IsNullable);
            });
    }

    /// <summary>Reconstruction preserves the ordinary SQL generator's statement and binding identities.</summary>
    [Fact]
    [Experimental("EF9100")]
    public void Quote_reconstruction_preserves_generated_sql()
    {
        using var context = CreateContext();
        var expression = CreateNestedSource();
        var generators = context.GetService<IQuerySqlGeneratorFactory>();
        var expected = generators.Create().GetCommand(expression.Source).CommandText;

        var actual = generators.Create().GetCommand(RoundTrip(expression, context).Source).CommandText;

        Assert.Equal(expected, actual);
    }

    /// <summary>Quoting the owning command preserves SQL even when its references reconstruct in independent local scopes.</summary>
    [Fact]
    [Experimental("EF9100")]
    public void Quote_of_the_command_select_preserves_aliases_binding_ids_and_generated_sql()
    {
        using var context = CreateContext();
        var source = CreateNestedSource().Source;
        var generators = context.GetService<IQuerySqlGeneratorFactory>();
        var expected = generators.Create().GetCommand(source).CommandText;

        var quoted = source.Quote();
        var bound = new TypeMappingSourceBindingExpressionVisitor(context.GetService<IRelationalTypeMappingSource>())
            .Visit(quoted);
        var reconstructed = Expression.Lambda<Func<SelectExpression>>(bound).Compile()();
        var actual = generators.Create().GetCommand(reconstructed).CommandText;

        Assert.Equal(expected, actual);
        Assert.Collection(reconstructed.Tables,
            table => AssertNestedReference(table, "first", 31),
            table => AssertNestedReference(table, "second", 31));
    }

    /// <summary>A cyclic internal source graph fails instead of reading an uninitialized local definition.</summary>
    [Fact]
    [Experimental("EF9100")]
    public void Quote_rejects_a_cyclic_source_graph()
    {
        var tables = new List<TableExpressionBase>();
        var source = CreateSource("cyclic_seed", tables, []);
        var expression = new MySqlMaterializedSourceExpression("cycle", 41, source);
        tables.Add(expression);

        var exception = Assert.Throws<InvalidOperationException>(() => expression.Quote());

        Assert.Contains("acyclic", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Builds one parent definition with two aliases of one shared leaf definition.</summary>
    private static MySqlMaterializedSourceExpression CreateNestedSource()
    {
        var first = new MySqlMaterializedSourceExpression("first", 31, CreateLeafSource());
        var second = first.WithAlias("second");
        var source = CreateSource("pair_seed", [first, second],
        [
            new ProjectionExpression(new ColumnExpression("Token", "first", typeof(Guid),
                MySqlGuidBinaryTypeMapping.Default, nullable: false), "FirstToken"),
            new ProjectionExpression(new ColumnExpression("OptionalName", "second", typeof(string),
                MySqlStringTypeMapping.Default, nullable: true), "SecondName"),
        ]);

        return new MySqlMaterializedSourceExpression("pairs", 32, source);
    }

    /// <summary>Checks the semantic identity and schema of one reconstructed nested reference.</summary>
    private static void AssertNestedReference(TableExpressionBase table, string alias, int bindingId)
    {
        var reference = Assert.IsType<MySqlMaterializedSourceExpression>(table);
        Assert.Equal(alias, reference.Alias);
        Assert.Equal(bindingId, reference.BindingId);
        Assert.Equal("leaf_seed", reference.Source.Alias);
        Assert.Equal(["Token", "OptionalName"], reference.Source.Projection.Select(projection => projection.Alias));
    }

    /// <summary>Builds an immutable scalar schema with a native GUID and a typed nullable payload.</summary>
    private static SelectExpression CreateLeafSource()
        => CreateSource("leaf_seed", [],
        [
            new ProjectionExpression(new SqlConstantExpression(Guid.Empty, MySqlGuidBinaryTypeMapping.Default), "Token"),
            new ProjectionExpression(new SqlConstantExpression(null, typeof(string), MySqlStringTypeMapping.Default),
                "OptionalName"),
        ]);

    /// <summary>Constructs the exact immutable relational fixture required by the quotation contract.</summary>
    private static SelectExpression CreateSource(
        string alias,
        List<TableExpressionBase> tables,
        List<ProjectionExpression> projections
    )
    {
        // WHY: EF exposes no public immutable SELECT factory outside this documented provider seam.
#pragma warning disable EF1001 // Exact SQL-node fixture construction; qualified on the supported EF patch matrix.
        return SelectExpression.CreateImmutable(alias, tables, projections, new SqlAliasManager());
#pragma warning restore EF1001
    }

    /// <summary>Reconstructs the quoted SQL node after binding EF's type-mapping service placeholder.</summary>
    [Experimental("EF9100")]
    private static MySqlMaterializedSourceExpression RoundTrip(MySqlMaterializedSourceExpression expression, QuoteContext context)
    {
        var quoted = expression.Quote();
        var bound = new TypeMappingSourceBindingExpressionVisitor(context.GetService<IRelationalTypeMappingSource>())
            .Visit(quoted);

        return Expression.Lambda<Func<MySqlMaterializedSourceExpression>>(bound).Compile()();
    }

    /// <summary>Creates a context only for EF's provider type-mapping service.</summary>
    private static QuoteContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<QuoteContext>()
            .UseMySql("Server=localhost;Database=quote;User ID=unused;",
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .Options;

        return new QuoteContext(options);
    }

    /// <summary>Supplies the same mapping-service placeholder binding as EF's generated reconstruction.</summary>
    private sealed class TypeMappingSourceBindingExpressionVisitor(IRelationalTypeMappingSource source) : ExpressionVisitor
    {
        /// <inheritdoc />
        protected override Expression VisitParameter(ParameterExpression node)
            => node.Name == "relationalTypeMappingSource" ? Expression.Constant(source, node.Type) : base.VisitParameter(node);
    }

    /// <summary>Hosts mapping services without executing queries.</summary>
    private sealed class QuoteContext : DbContext
    {
        /// <summary>Initializes the quotation service context.</summary>
        public QuoteContext(DbContextOptions<QuoteContext> options) : base(options) { }
    }
}
