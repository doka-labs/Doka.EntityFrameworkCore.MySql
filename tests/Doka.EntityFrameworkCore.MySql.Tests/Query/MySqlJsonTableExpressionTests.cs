namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies that provider-specific <c>JSON_TABLE</c> metadata survives EF Core's
/// precompiled-query expression-quoting boundary.
/// </summary>
public sealed class MySqlJsonTableExpressionTests
{
    /// <summary>
    /// A quoted expression must reconstruct the provider node rather than falling
    /// back to the base table-valued-function shape, which cannot carry paths or
    /// column descriptors.
    /// </summary>
    [Fact]
    [Experimental("EF9100")]
    public void Quote_preserves_paths_column_metadata_and_type_mappings()
    {
        var arrayIndex = new SqlConstantExpression(2, typeMapping: null);
        var expression = new MySqlJsonTableExpression(
            "json_rows",
            new SqlConstantExpression("[]", MySqlStringTypeMapping.Default),
            [
                new PathSegment("items"),
                new PathSegment(arrayIndex),
            ],
            [
                new MySqlJsonTableExpression.ColumnInfo(
                    "payload",
                    MySqlStringTypeMapping.Default,
                    [new PathSegment("value")],
                    AsJson: true),
                new MySqlJsonTableExpression.ColumnInfo("ordinal", MySqlStringTypeMapping.Default, ForOrdinality: true),
            ]);

        var quoted = expression.Quote();
        using var context = CreateContext();
        var typeMappingSource = context.GetService<IRelationalTypeMappingSource>();
        var boundQuote = new TypeMappingSourceBindingExpressionVisitor(typeMappingSource).Visit(quoted);
        var reconstructed = Expression
            .Lambda<Func<MySqlJsonTableExpression>>(boundQuote)
            .Compile()();

        Assert.Equal("json_rows", reconstructed.Alias);
        Assert.Equal(
            "[]",
            Assert.IsType<SqlConstantExpression>(reconstructed.JsonExpression)
                .Value);

        Assert.Collection(
            Assert.IsAssignableFrom<IReadOnlyList<PathSegment>>(reconstructed.Path),
            segment => Assert.Equal("items", segment.PropertyName),
            segment => Assert.Equal(
                2,
                Assert.IsType<SqlConstantExpression>(segment.ArrayIndex)
                    .Value));

        Assert.Collection(
            Assert.IsAssignableFrom<IReadOnlyList<MySqlJsonTableExpression.ColumnInfo>>(reconstructed.ColumnInfos),
            column =>
            {
                Assert.Equal("payload", column.Name);
                Assert.Equal(MySqlStringTypeMapping.Default.StoreType, column.TypeMapping.StoreType);
                Assert.True(column.AsJson);
                Assert.False(column.ForOrdinality);
                Assert.Equal(
                    "value",
                    Assert.Single(column.Path!)
                        .PropertyName);
            },
            column =>
            {
                Assert.Equal("ordinal", column.Name);
                Assert.False(column.AsJson);
                Assert.True(column.ForOrdinality);
                Assert.Null(column.Path);
            });
    }

    private static QuoteContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<QuoteContext>().UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=password;",
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .Options;

        return new QuoteContext(options);
    }

    /// <summary>
    /// Models the binding performed by EF Core's precompiled-query generator for
    /// type mappings. The intermediate quote tree deliberately carries a parameter
    /// instead of embedding a scoped provider service as a constant.
    /// </summary>
    private sealed class TypeMappingSourceBindingExpressionVisitor : ExpressionVisitor
    {
        private readonly IRelationalTypeMappingSource _typeMappingSource;

        public TypeMappingSourceBindingExpressionVisitor(
            IRelationalTypeMappingSource typeMappingSource
        )
        {
            _typeMappingSource = typeMappingSource;
        }

        protected override Expression VisitParameter(
            ParameterExpression node
        ) => node.Name == "relationalTypeMappingSource"
            ? Expression.Constant(_typeMappingSource, node.Type)
            : base.VisitParameter(node);
    }

    private sealed class QuoteContext : DbContext
    {
        public QuoteContext(
            DbContextOptions<QuoteContext> options
        ) : base(options) { }
    }
}
