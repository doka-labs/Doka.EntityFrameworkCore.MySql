using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Xunit.Abstractions;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlMaterializedSourceExpressionTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initializes quotation evidence output for the test instance.</summary>
    public MySqlMaterializedSourceExpressionTests(ITestOutputHelper output) => _output = output;

    /// <summary>EF's actual late-quotation translation retains the local scope and required constructor accessors.</summary>
    [Fact]
    [Experimental("EF9100")]
    public void Actual_csharp_translation_keeps_definition_locals_inside_the_delegate()
    {
        // Arrange
        using var context = CreateContext();
        var expression = CreateNestedSource();

        // Act
        var translation = TranslateConstantToCSharp(expression, context);

        // Assert
        Assert.Contains("sourceDefinition", translation.Code, StringComparison.Ordinal);
        Assert.Contains(".Invoke()", translation.Code, StringComparison.Ordinal);
        Assert.DoesNotContain("QuotedReference", translation.Code, StringComparison.Ordinal);
        Assert.Equal(2, CountDefinitionConstructions(translation.Code));
        Assert.Contains(translation.UnsafeAccessors, accessor =>
            accessor.Contains("UnsafeAccessorKind.Constructor", StringComparison.Ordinal)
            && accessor.Contains(nameof(MySqlMaterializedSourceExpression), StringComparison.Ordinal));
        AssertValidSyntax(translation);
    }

    /// <summary>Repeated references add one reconstruction per distinct definition instead of expanding every path.</summary>
    [Theory]
    [InlineData(4, false)]
    [InlineData(8, false)]
    [InlineData(12, false)]
    [InlineData(4, true)]
    [InlineData(8, true)]
    [InlineData(12, true)]
    [Experimental("EF9100")]
    public void Actual_csharp_translation_preserves_linear_definition_growth(int depth, bool commandSelect)
    {
        // Arrange
        using var context = CreateContext();
        var warmup = CreateGraph(2);
        // WHY: Source references and their owning SELECT enter different late-quotation paths.
        _ = TranslateConstantToCSharp(commandSelect ? warmup.Source : warmup, context);
        var expression = CreateGraph(depth);
        object constant = commandSelect ? expression.Source : expression;
        var expectedDefinitions = commandSelect ? depth * 2 : depth + 1;

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        var translation = TranslateConstantToCSharp(constant, context);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var actual = CountDefinitionConstructions(translation.Code);

        // Assert
        Assert.Equal(expectedDefinitions, actual);
        // WHY: Structural budgets detect path duplication without depending on runner CPU speed.
        Assert.InRange(translation.Code.Length, 0, expectedDefinitions * 6_000);
        Assert.InRange(allocated, 0, expectedDefinitions * 500_000L);
        AssertValidSyntax(translation);
        _output.WriteLine("Depth={0}; command select={1}; definitions={2}; generated characters={3}; allocated={4} B.",
            depth, commandSelect, expectedDefinitions, translation.Code.Length, allocated);
    }

    /// <summary>The real C# generator rejects cycles before emitting an uninitialized definition reference.</summary>
    [Fact]
    [Experimental("EF9100")]
    public void Actual_csharp_translation_rejects_a_cyclic_source_graph()
    {
        // Arrange
        using var context = CreateContext();
        var tables = new List<TableExpressionBase>();
        var source = CreateSource("cyclic_seed", tables, []);
        var expression = new MySqlMaterializedSourceExpression("cycle", 41, source);
        tables.Add(expression);
        Action act = () => TranslateConstantToCSharp(expression, context);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Contains("acyclic", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Uses EF's Constant-to-Quote boundary and retains every required unsafe-accessor declaration.</summary>
    [Experimental("EF9100")]
    private static (string Code, IReadOnlyCollection<string> UnsafeAccessors) TranslateConstantToCSharp(
        object expression,
        QuoteContext context
    )
    {
        var unsafeAccessors = new HashSet<string>();
        // WHY: Translating an eagerly optimized Quote AST would skip EF's actual late-quotation boundary.
#pragma warning disable EF1001 // Exercise EF's actual design-time expression translator instead of a substitute generator.
        var helper = new CSharpHelper(context.GetService<ITypeMappingSource>());
        var code = helper.Expression(Expression.Constant(expression), new HashSet<string>(), unsafeAccessors, null, null);
#pragma warning restore EF1001

        return (code, unsafeAccessors);
    }

    /// <summary>Checks generated expression and accessor syntax without claiming cross-assembly or NativeAOT compilation.</summary>
    private static void AssertValidSyntax((string Code, IReadOnlyCollection<string> UnsafeAccessors) translation)
    {
        var source = "static class QuotedCommand { static object Read() => " + translation.Code
            + "; " + string.Join(Environment.NewLine, translation.UnsafeAccessors) + " }";

        var diagnostics = CSharpSyntaxTree.ParseText(source).GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.Empty(diagnostics);
    }

    /// <summary>Counts emitted definition constructors rather than local variables or reference accessors.</summary>
    private static int CountDefinitionConstructions(string code)
        => Regex.Count(code, @"new\s+(?:[\w.]+\.)?MySqlMaterializedSourceDefinition\s*\(");

    /// <summary>Builds a DAG whose two aliases at each level reuse the same completed child definition.</summary>
    private static MySqlMaterializedSourceExpression CreateGraph(int depth)
    {
        var source = CreateLeafSource();

        for (var index = 0; index < depth; index++)
        {
            var first = new MySqlMaterializedSourceExpression("first", index, source);
            var second = first.WithAlias("second");
            source = CreateSource("seed" + index.ToString(CultureInfo.InvariantCulture),
                [first, second],
            [
                new ProjectionExpression(new ColumnExpression("Token", "first", typeof(Guid),
                    MySqlGuidBinaryTypeMapping.Default, nullable: false), "Token"),
                new ProjectionExpression(new ColumnExpression("OptionalName", "second", typeof(string),
                    MySqlStringTypeMapping.Default, nullable: true), "OptionalName"),
            ]);
        }

        return new MySqlMaterializedSourceExpression("result", depth, source);
    }
}
