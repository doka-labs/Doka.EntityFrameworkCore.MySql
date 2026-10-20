using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore.Design.Internal;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies the actual immutable SQL node and EF quotation/rewrite boundaries.</summary>
public sealed class MySqlStraightJoinExpressionTests
{
    /// <summary>Cloning, alias replacement and TRUE simplification retain the exact directed-edge node.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_clone_and_updates_preserve_direction_and_annotations(bool prunable)
    {
        using var context = CreateContext();
        var factory = context.GetService<ISqlExpressionFactory>();
        var source = Source("inner");
        var edge = new MySqlStraightJoinExpression(source, factory.Constant(false), prunable,
            new Dictionary<string, IAnnotation> { ["Doka:Optional"] = new Annotation("Doka:Optional", null) });
        var replacement = source.WithAlias("replacement");

        var clone = edge.Clone(null, new ReplaceSource(source, replacement));
        var updated = edge.Update(replacement, factory.Constant(true));
        var annotated = edge.AddAnnotation("Doka:Value", "kept");

        var clonedEdge = Assert.IsType<MySqlStraightJoinExpression>(clone);
        Assert.Same(replacement, clonedEdge.Table);
        Assert.IsType<MySqlStraightJoinExpression>(updated);
        Assert.IsType<MySqlStraightJoinExpression>(annotated);
        Assert.Equal(prunable, clonedEdge.IsPrunable);
        Assert.Equal(prunable, updated.IsPrunable);
        Assert.NotNull(clonedEdge.FindAnnotation("Doka:Optional"));
        Assert.Equal("kept", annotated.FindAnnotation("Doka:Value")?.Value);
        Assert.Throws<InvalidOperationException>(() => edge.WithAlias("invalid"));
    }

    /// <summary>Actual Quote reconstruction preserves the edge, predicate, pruning and nullable annotation values.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Experimental("EF9100")]
    public void Actual_quote_round_trip_preserves_the_directed_edge(bool prunable)
    {
        using var context = CreateContext();
        var edge = new MySqlStraightJoinExpression(Source("inner"), context.GetService<ISqlExpressionFactory>().Constant(true),
            prunable, new Dictionary<string, IAnnotation>
            {
                ["Doka:Value"] = new Annotation("Doka:Value", 23),
                ["Doka:Optional"] = new Annotation("Doka:Optional", null),
            });
        var quoted = new BindMappings(context.GetService<IRelationalTypeMappingSource>()).Visit(edge.Quote());

        var rebuilt = Expression.Lambda<Func<MySqlStraightJoinExpression>>(quoted).Compile()();

        Assert.Equal("inner", rebuilt.Table.Alias);
        Assert.Equal(prunable, rebuilt.IsPrunable);
        Assert.Equal(23, rebuilt.FindAnnotation("Doka:Value")?.Value);
        Assert.NotNull(rebuilt.FindAnnotation("Doka:Optional"));
        Assert.Null(rebuilt.FindAnnotation("Doka:Optional")!.Value);
        Assert.Equal(edge.ToString(), rebuilt.ToString());
    }

    /// <summary>One quoted DAG retains a shared completed source inside and outside the directed candidate edge.</summary>
    [Fact]
    [Experimental("EF9100")]
    public void Actual_quote_and_csharp_generator_preserve_the_dependent_source_dag()
    {
        using var context = CreateContext();
        var factory = context.GetService<ISqlExpressionFactory>();
        var selected = new MySqlMaterializedSourceExpression("selected", 41, Source("selected_seed"));
        var dependency = (MySqlMaterializedSourceExpression)selected.WithAlias("dependency");
        var candidateBody = Source("candidate_seed", [dependency]);
        var candidates = new MySqlMaterializedSourceExpression("candidates", 42, candidateBody);
        var edge = new MySqlStraightJoinExpression(candidates, factory.Constant(true, context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(bool))), annotations:
            new Dictionary<string, IAnnotation> { ["Doka:Edge"] = new Annotation("Doka:Edge", "kept") });
        var root = new MySqlMaterializedSourceExpression("result", 43, Source("result_seed", [selected, edge]));
        var generator = context.GetService<IQuerySqlGeneratorFactory>();
        var expectedSql = generator.Create().GetCommand(root.Source).CommandText;
        var quote = new BindMappings(context.GetService<IRelationalTypeMappingSource>()).Visit(root.Quote());

        var rebuilt = Expression.Lambda<Func<MySqlMaterializedSourceExpression>>(quote).Compile()();
        var accessors = new HashSet<string>();
#pragma warning disable EF1001 // Exercise EF's actual late Constant-to-Quote design-time boundary.
        var namespaces = new HashSet<string>();
        var csharp = new CSharpHelper(context.GetService<ITypeMappingSource>()).Expression(Expression.Constant(root),
            namespaces, accessors, null, null);
#pragma warning restore EF1001

        var rebuiltSelected = Assert.IsType<MySqlMaterializedSourceExpression>(rebuilt.Source.Tables[0]);
        var rebuiltEdge = Assert.IsType<MySqlStraightJoinExpression>(rebuilt.Source.Tables[1]);
        var rebuiltCandidate = Assert.IsType<MySqlMaterializedSourceExpression>(rebuiltEdge.Table);
        var rebuiltDependency = Assert.IsType<MySqlMaterializedSourceExpression>(rebuiltCandidate.Source.Tables[0]);
        Assert.Same(rebuiltSelected.Definition, rebuiltDependency.Definition);
        Assert.Equal(expectedSql, generator.Create().GetCommand(rebuilt.Source).CommandText);
        Assert.Contains("STRAIGHT_JOIN", expectedSql, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Count(csharp, @"new\s+(?:[\w.]+\.)?MySqlMaterializedSourceDefinition\s*\("));
        Assert.Contains(nameof(MySqlStraightJoinExpression), csharp, StringComparison.Ordinal);
        namespaces.Add("Microsoft.EntityFrameworkCore.Storage");
        var generated = string.Join(Environment.NewLine, namespaces.Select(value => "using " + value + ";"))
            + " static class Generated { public static object Read(IRelationalTypeMappingSource relationalTypeMappingSource) => " + csharp + "; "
            + string.Join(Environment.NewLine, accessors) + " }";
        var syntax = CSharpSyntaxTree.ParseText(generated);
        Assert.DoesNotContain(syntax.GetDiagnostics(), value => value.Severity == DiagnosticSeverity.Error);
        var generatedRoot = CompileAndReconstruct(syntax, context.GetService<IRelationalTypeMappingSource>());
        var generatedEdge = Assert.IsType<MySqlStraightJoinExpression>(generatedRoot.Source.Tables[1]);
        var generatedCandidate = Assert.IsType<MySqlMaterializedSourceExpression>(generatedEdge.Table);
        Assert.Same(Assert.IsType<MySqlMaterializedSourceExpression>(generatedRoot.Source.Tables[0]).Definition,
            Assert.IsType<MySqlMaterializedSourceExpression>(generatedCandidate.Source.Tables[0]).Definition);
        Assert.Equal("kept", generatedEdge.FindAnnotation("Doka:Edge")?.Value);
        Assert.Equal(expectedSql, generator.Create().GetCommand(generatedRoot.Source).CommandText);
    }

    /// <summary>Existing completed-source annotations also survive the actual generated-C# path.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Experimental("EF9100")]
    public void Completed_source_annotations_survive_actual_csharp_reconstruction(bool nullValue)
    {
        using var context = CreateContext();
        object? value = nullValue ? null : "kept";
        var original = new MySqlMaterializedSourceExpression("source", 51, Source("source_seed"));
        var expression = Assert.IsType<MySqlMaterializedSourceExpression>(original.AddAnnotation("Doka:Optional", value));
        var accessors = new HashSet<string>();
        var namespaces = new HashSet<string> { "Microsoft.EntityFrameworkCore.Storage" };
#pragma warning disable EF1001 // Exercise the actual existing Constant-to-Quote consumer, without a directed join.
        var csharp = new CSharpHelper(context.GetService<ITypeMappingSource>()).Expression(Expression.Constant(expression),
            namespaces, accessors, null, null);
#pragma warning restore EF1001
        var generated = string.Join(Environment.NewLine, namespaces.Select(name => "using " + name + ";"))
            + " static class Generated { public static object Read(IRelationalTypeMappingSource relationalTypeMappingSource) => "
            + csharp + "; " + string.Join(Environment.NewLine, accessors) + " }";

        var reconstructed = CompileAndReconstruct(CSharpSyntaxTree.ParseText(generated),
            context.GetService<IRelationalTypeMappingSource>());

        Assert.NotNull(reconstructed.FindAnnotation("Doka:Optional"));
        Assert.Equal(value, reconstructed.FindAnnotation("Doka:Optional")!.Value);
        Assert.Equal(context.GetService<IQuerySqlGeneratorFactory>().Create().GetCommand(expression.Source).CommandText,
            context.GetService<IQuerySqlGeneratorFactory>().Create().GetCommand(reconstructed.Source).CommandText);
    }

    /// <summary>Semantically compiles and executes the actual generated expression and unsafe accessors.</summary>
    private static MySqlMaterializedSourceExpression CompileAndReconstruct(SyntaxTree syntax, IRelationalTypeMappingSource mappings)
    {
        // WHY: The provider already grants its real test assembly access to internal SQL nodes.
        // Use that exact friend identity in a collectible context rather than weakening node visibility.
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length != 0)
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(location => MetadataReference.CreateFromFile(location));
        var compilation = CSharpCompilation.Create("Doka.EntityFrameworkCore.MySql.Tests", [syntax], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var binary = new MemoryStream();
        var emitted = compilation.Emit(binary);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        binary.Position = 0;
        var load = new AssemblyLoadContext("straight-join-quoted-command", isCollectible: true);

        try
        {
            var assembly = load.LoadFromStream(binary);
            var read = assembly.GetType("Generated", throwOnError: true)!
                .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!;

            return Assert.IsType<MySqlMaterializedSourceExpression>(read.Invoke(null, [mappings]));
        }
        finally
        {
            load.Unload();
        }
    }

    /// <summary>Builds a minimal immutable source without relying on model metadata or a connection.</summary>
    private static SelectExpression Source(string alias, List<TableExpressionBase>? tables = null)
    {
#pragma warning disable EF1001 // Exact immutable SQL-node fixture construction on the supported EF patch matrix.
        return SelectExpression.CreateImmutable(alias, tables ?? [],
            [new ProjectionExpression(new SqlConstantExpression(1, new IntTypeMapping("int")), "Id")], new SqlAliasManager());
#pragma warning restore EF1001
    }

    /// <summary>Creates only the provider's configured mapping and generator services.</summary>
    private static DbContext CreateContext() => new(new DbContextOptionsBuilder()
        .UseMySql("Server=127.0.0.1;Database=unused;User ID=unused;", MySqlServerVersion.MySql(new Version(8, 4, 11))).Options);

    /// <summary>Exercises inherited JoinExpressionBase.Clone through its real child visitor dispatch.</summary>
    private sealed class ReplaceSource(TableExpressionBase original, TableExpressionBase replacement) : ExpressionVisitor
    {
        /// <inheritdoc />
        public override Expression? Visit(Expression? node) => ReferenceEquals(node, original) ? replacement : base.Visit(node);
    }

    /// <summary>Binds the same placeholder consumed by EF's generated command reconstruction.</summary>
    private sealed class BindMappings(IRelationalTypeMappingSource source) : ExpressionVisitor
    {
        /// <inheritdoc />
        protected override Expression VisitParameter(ParameterExpression node)
            => node.Name == "relationalTypeMappingSource" ? Expression.Constant(source, node.Type) : base.VisitParameter(node);
    }
}
