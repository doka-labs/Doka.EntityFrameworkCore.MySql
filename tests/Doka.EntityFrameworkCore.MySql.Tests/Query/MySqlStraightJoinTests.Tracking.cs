using System.Reflection;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlStraightJoinTests
{
    /// <summary>Ordinary translations allocate no optional marker ownership set.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Marker_tracking_is_allocated_only_for_a_directed_join(bool directed)
    {
        // Arrange
        using var context = CreateContext();
        var query = Query(context, "scalar", directed);
        var compilation = context.GetService<IQueryCompilationContextFactory>().Create(async: false);
        var processed = context.GetService<IQueryTranslationPreprocessorFactory>().Create(compilation).Process(query.Expression);
        var visitor = context.GetService<IQueryableMethodTranslatingExpressionVisitorFactory>().Create(compilation);
        var field = typeof(MySqlQueryableMethodTranslatingExpressionVisitor)
            .GetField("_straightJoinInputs", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var beforeTranslation = field.GetValue(visitor);

        // Act
        var translated = visitor.Translate(processed);
        var ownership = field.GetValue(visitor);

        // Assert
        Assert.Null(beforeTranslation);
        Assert.NotNull(translated);
        if (directed)
        {
            Assert.Empty(Assert.IsType<HashSet<SelectExpression>>(ownership));
        }
        else
        {
            Assert.Null(ownership);
        }

        Assert.Equal(0, context.Blocker.Openings);
    }

    /// <summary>Neither projection order folds a directed scalar subquery into an ordinary one.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Mixed_scalar_subqueries_preserve_each_edge_kind(bool mariaDb, bool directedFirst)
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var ordinary = context.Nodes.Join(context.Nodes, left => left.Id, right => right.Id, (left, right) => left.Id);
        var directed = context.Nodes.StraightJoin(context.Nodes, left => left.Id, right => right.Id, (left, right) => left.Id);
        var query = directedFirst
            ? context.Nodes.Select(owner => new { First = directed.Count(value => value == owner.Id), Second = ordinary.Count(value => value == owner.Id) })
            : context.Nodes.Select(owner => new { First = ordinary.Count(value => value == owner.Id), Second = directed.Count(value => value == owner.Id) });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Single(Regex.Matches(sql, @"\bSTRAIGHT_JOIN\b"));
        Assert.Single(Regex.Matches(sql, @"\bINNER JOIN\b"));
        Assert.Equal(0, context.Blocker.Openings);
    }
}
