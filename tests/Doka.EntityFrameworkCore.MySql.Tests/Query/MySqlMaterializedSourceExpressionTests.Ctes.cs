namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlMaterializedSourceExpressionTests
{
    /// <summary>Ordinary evaluation semantics survive the same real expression quotation as shared sources.</summary>
    [Fact]
    [Experimental("EF9100")]
    public void Ordinary_cte_quote_preserves_its_explicit_binding_kind()
    {
        // Arrange
        using var context = CreateContext();
        var definition = new MySqlMaterializedSourceDefinition(CreateLeafSource(), MySqlSourceBindingKind.Ordinary);
        var expression = new MySqlMaterializedSourceExpression("ordinary", 57, definition, annotations: null);

        // Act
        var reconstructed = RoundTrip(expression, context);

        // Assert
        Assert.Equal(MySqlSourceBindingKind.Ordinary, reconstructed.Kind);
        Assert.Equal(57, reconstructed.BindingId);
        Assert.Equal("ordinary", reconstructed.Alias);
        Assert.Equal(["Token", "OptionalName"], reconstructed.Source.Projection.Select(projection => projection.Alias));
        Assert.Equal(MySqlSourceBindingKind.Ordinary, expression.Kind);
    }

    /// <summary>Aliasing preserves the shared definition and does not change an ordinary relation into a fence.</summary>
    [Fact]
    public void Ordinary_cte_aliasing_retains_definition_identity_and_kind()
    {
        // Arrange
        var definition = new MySqlMaterializedSourceDefinition(CreateLeafSource(), MySqlSourceBindingKind.Ordinary);
        var expression = new MySqlMaterializedSourceExpression("first", 57, definition, annotations: null);

        // Act
        var aliased = Assert.IsType<MySqlMaterializedSourceExpression>(expression.WithAlias("second"));

        // Assert
        Assert.Same(definition, aliased.Definition);
        Assert.Equal(MySqlSourceBindingKind.Ordinary, aliased.Kind);
        Assert.Equal("second", aliased.Alias);
        Assert.Equal("first", expression.Alias);
    }

    /// <summary>A normal visitor rewrite preserves ordinary semantics along with the new completed schema.</summary>
    [Fact]
    public void Ordinary_cte_definition_rewrite_retains_binding_kind()
    {
        // Arrange
        var definition = new MySqlMaterializedSourceDefinition(CreateLeafSource(), MySqlSourceBindingKind.Ordinary);
        var expression = new MySqlMaterializedSourceExpression("ordinary", 57, definition, annotations: null);
        var visitor = new RenameCteSourceVisitor();

        // Act
        var rewritten = Assert.IsType<MySqlMaterializedSourceExpression>(visitor.Visit(expression));

        // Assert
        Assert.Equal(MySqlSourceBindingKind.Ordinary, rewritten.Kind);
        Assert.Equal("rewritten_seed", rewritten.Source.Alias);
        Assert.NotSame(definition, rewritten.Definition);
        Assert.Equal("leaf_seed", definition.Source.Alias);
    }

    /// <summary>The real EF C# reconstruction includes the ordinary contract rather than its legacy default.</summary>
    [Fact]
    [Experimental("EF9100")]
    public void Actual_csharp_translation_preserves_the_ordinary_cte_kind()
    {
        // Arrange
        using var context = CreateContext();
        var definition = new MySqlMaterializedSourceDefinition(CreateLeafSource(), MySqlSourceBindingKind.Ordinary);
        var expression = new MySqlMaterializedSourceExpression("ordinary", 57, definition, annotations: null);

        // Act
        var translation = TranslateConstantToCSharp(expression, context);

        // Assert
        Assert.Contains("Ordinary", translation.Code, StringComparison.Ordinal);
        Assert.Equal(1, CountDefinitionConstructions(translation.Code));
        AssertValidSyntax(translation);
    }

    /// <summary>Invalid internal evaluation metadata is rejected before it can choose an SQL contract.</summary>
    [Fact]
    public void Undefined_cte_binding_kind_is_rejected()
    {
        // Arrange
        var source = CreateLeafSource();

        void act() => _ = new MySqlMaterializedSourceDefinition(source, (MySqlSourceBindingKind)100);

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.Equal("kind",
            Assert.IsType<ArgumentOutOfRangeException>(exception)
                .ParamName);
    }

    /// <summary>Rebuilds only the completed definition so the kind-copy contract is independently visible.</summary>
    private sealed class RenameCteSourceVisitor : ExpressionVisitor
    {
        /// <inheritdoc />
        protected override Expression VisitExtension(
            Expression node
        ) => node is SelectExpression source
            ? source.WithAlias("rewritten_seed")
            : base.VisitExtension(node);
    }
}
