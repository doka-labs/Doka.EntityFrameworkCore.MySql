namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Pins INSERT expression identity and quotation independently of database execution.</summary>
public sealed class MySqlInsertSelectExpressionTests
{
    /// <summary>DISTINCT changes occurrence multiplicity even when EF's underlying SELECT comparer omits that flag.</summary>
    [Fact]
    public void Distinct_and_bag_sources_have_different_statement_identity()
    {
        // Arrange
        using var context = CreateContext();
        var bag = CreateSql(context);
        var distinctSource = CreateSource(context, 1);
        distinctSource.IsDistinct = true;
        var distinct = bag.Update(distinctSource);

        // Act
        var actual = bag.Equals(distinct);

        // Assert
        Assert.False(actual);
        Assert.False(distinct.Equals(bag));
        Assert.False(bag.Source.IsDistinct);
        Assert.True(distinct.Source.IsDistinct);
    }

    /// <summary>Equal independently reconstructed SELECTs keep equal statement hashes through normal visitors.</summary>
    [Fact]
    public void Visitor_reconstruction_preserves_the_structural_equality_hash_contract()
    {
        // Arrange
        using var context = CreateContext();
        var original = CreateSql(context);
        var equivalent = CreateSource(context, 1);

        // Act
        var actual = ReplacingExpressionVisitor.Replace(original.Source, equivalent, original);

        // Assert
        var rebuilt = Assert.IsType<MySqlInsertSelectExpression>(actual);
        Assert.NotSame(original, rebuilt);
        Assert.NotSame(original.Source, rebuilt.Source);
        Assert.Equal(original, rebuilt);
        Assert.Equal(original.GetHashCode(), rebuilt.GetHashCode());
    }

    /// <summary>A positional INSERT projection stays live without an external SQL column reference.</summary>
    [Fact]
    public void Statement_pruning_preserves_the_assigned_source_projection()
    {
        // Arrange
        using var context = CreateContext();
        var original = CreateSql(context);

        // Act
        var actual = new MySqlMaterializedSourcePruner(pruneStatementColumns: true).Prune(original);

        // Assert
        var insert = Assert.IsType<MySqlInsertSelectExpression>(actual);
        Assert.Equal(original.Columns.Count, insert.Source.Projection.Count);
        Assert.Equal("Value",
            Assert.Single(insert.Source.Projection)
                .Alias);

        Assert.Equal("Value", Assert.Single(insert.Columns));
    }

    /// <summary>Assigned CTE output remains demanded while an unrelated ordinary CTE column can be removed.</summary>
    [Fact]
    public void Statement_pruning_retains_cte_columns_consumed_by_insert_assignments()
    {
        // Arrange
        using var context = CreateContext();
        var leaf = CreateSource(context, 1);
        var mapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(int))!;

        var complete = leaf.Update(leaf.Tables,
            leaf.Predicate,
            leaf.GroupBy,
            leaf.Having,
            [leaf.Projection[0], new ProjectionExpression(new SqlConstantExpression(99, mapping), "Unused")],
            leaf.Orderings,
            leaf.Offset,
            leaf.Limit);

        var binding = new MySqlMaterializedSourceExpression("c",
            17,
            new MySqlMaterializedSourceDefinition(complete, MySqlSourceBindingKind.Ordinary),
            annotations: null);

#pragma warning disable EF1001 // Exact immutable relational-node fixture for the provider's positional pruning seam.
        var source = SelectExpression.CreateImmutable("insertSource",
            [binding],
            [
                new ProjectionExpression(new ColumnExpression("Value", "c", typeof(int), mapping, nullable: false),
                    "Value"),
            ],
            new SqlAliasManager());
#pragma warning restore EF1001
        var original = CreateSql(context)
            .Update(source);

        // Act
        var actual = new MySqlMaterializedSourcePruner(pruneStatementColumns: true).Prune(original);

        // Assert
        var insert = Assert.IsType<MySqlInsertSelectExpression>(actual);
        Assert.Equal("Value",
            Assert.Single(insert.Source.Projection)
                .Alias);

        Assert.Equal("Value", Assert.Single(insert.Columns));
        var retained = Assert.IsType<MySqlMaterializedSourceExpression>(Assert.Single(insert.Source.Tables));
        Assert.Equal(MySqlSourceBindingKind.Ordinary, retained.Kind);
        Assert.Equal("Value",
            Assert.Single(retained.Source.Projection)
                .Alias);
    }

    /// <summary>Raw terminal identity includes its actual property order and primitive cancellation binding.</summary>
    [Fact]
    public void Raw_terminal_identity_preserves_assignment_and_token_bindings()
    {
        // Arrange
        using var context = CreateContext();
        var target = context.Model.FindEntityType(typeof(ExpressionTarget))!;
        var property = target.FindProperty(nameof(ExpressionTarget.Value))!;
        var source = Expression.Constant(1);
        var original = new MySqlInsertSelectQueryExpression(source, target, [property], "token");
        var equal = new MySqlInsertSelectQueryExpression(source, target, [property], "token");
        var changedToken = new MySqlInsertSelectQueryExpression(source, target, [property], "otherToken");

        // Act
        var unchanged = original.Update(source);
        var changed = original.Update(Expression.Constant(2));

        // Assert
        Assert.Same(original, unchanged);
        Assert.NotSame(original, changed);
        Assert.Equal(original, equal);
        Assert.Equal(original.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(original, changedToken);
        Assert.NotEqual(original, changed);
        Assert.Same(target, changed.Target);
        Assert.Same(property, Assert.Single(changed.Properties));
        Assert.Equal("token", changed.CancellationTokenParameter);
    }

    /// <summary>Updating SQL output never loses physical target, assignment order, tags or cancellation binding.</summary>
    [Fact]
    public void Sql_terminal_update_preserves_immutable_statement_metadata()
    {
        // Arrange
        using var context = CreateContext();
        var original = CreateSql(context);
        var replacement = CreateSource(context, 2);

        // Act
        var unchanged = original.Update(original.Source);
        var changed = original.Update(replacement);

        // Assert
        Assert.Same(original, unchanged);
        Assert.NotSame(original, changed);
        Assert.Same(original.Table, changed.Table);
        Assert.Same(replacement, changed.Source);
        Assert.Equal(original.Columns, changed.Columns);
        Assert.Equal(original.Tags, changed.Tags);
        Assert.Equal("token", changed.CancellationTokenParameter);
        Assert.NotEqual(original, changed);
    }

    /// <summary>Compiler tags create a new statement without mutating an existing shared SQL tree.</summary>
    [Fact]
    public void Compiler_tags_do_not_mutate_the_original_statement()
    {
        // Arrange
        using var context = CreateContext();
        var original = CreateSql(context);
        var tags = new HashSet<string>(StringComparer.Ordinal) { "current-tag" };

        // Act
        var changed = original.ApplyTags(tags);

        // Assert
        Assert.Equal("original-tag", Assert.Single(original.Tags));
        Assert.Equal("current-tag", Assert.Single(changed.Tags));
        Assert.Same(original.Source, changed.Source);
        Assert.Same(original.Table, changed.Table);
        Assert.NotEqual(original, changed);
    }

    /// <summary>EF's actual quotation contract reconstructs every SQL terminal field.</summary>
    [Fact]
    public void Sql_terminal_quote_round_trips_target_projection_tags_and_token()
    {
        // Arrange
        using var context = CreateContext();
        var original = CreateSql(context);

        // Act
#pragma warning disable EF9100 // Exercises the exact pinned EF10 quotation contract, not a new public experimental API.
        var quoted = new QuoteServices(context).Visit(original.Quote());
#pragma warning restore EF9100
        var rebuilt = Expression
            .Lambda<Func<MySqlInsertSelectExpression>>(quoted)
            .Compile(preferInterpretation: true)();

        // Assert
        Assert.NotSame(original, rebuilt);
        Assert.Equal(original, rebuilt);
        Assert.Equal(original.GetHashCode(), rebuilt.GetHashCode());
        Assert.Equal("token", rebuilt.CancellationTokenParameter);
        Assert.Equal("original-tag", Assert.Single(rebuilt.Tags));
        Assert.Equal("Value", Assert.Single(rebuilt.Columns));
    }

    /// <summary>Creates a cold offline model using the existing explicit transient-provider policy.</summary>
    private static ExpressionContext CreateContext() => new(new DbContextOptionsBuilder<ExpressionContext>()
        .EnableServiceProviderCaching(false)
        .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
        .UseMySql("Server=localhost;Database=insert_expression;User ID=unused;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)))
        .Options);

    /// <summary>Creates a real one-table SQL statement for copy and quotation controls.</summary>
    private static MySqlInsertSelectExpression CreateSql(
        ExpressionContext context
    ) => new(new TableExpression("target",
            context
                .Model
                .GetRelationalModel()
                .FindTable("ExpressionTargets", null)!),
        CreateSource(context, 1),
        ["Value"],
        ["original-tag"],
        "token");

    /// <summary>Uses EF's actual immutable SELECT factory with a real provider mapping.</summary>
    private static SelectExpression CreateSource(
        ExpressionContext context,
        int value
    )
    {
        var mapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(int))!;

#pragma warning disable EF1001 // Exact relational-node fixture construction, as used by the provider's existing SQL-node tests.
        return SelectExpression.CreateImmutable("insertSource",
            [],
            [new ProjectionExpression(new SqlConstantExpression(value, mapping), "Value")],
            new SqlAliasManager());
#pragma warning restore EF1001
    }

    /// <summary>Binds the same metadata and mapping services consumed by EF's quotation reconstruction.</summary>
    private sealed class QuoteServices : ExpressionVisitor
    {
        private readonly ExpressionContext _context;

        /// <summary>Initializes the test helper with its supplied dependencies and values.</summary>
        public QuoteServices(
            ExpressionContext context
        )
        {
            _context = context;
        }

        /// <inheritdoc />
        protected override Expression VisitParameter(
            ParameterExpression node
        )
        {
            // WHY: EF10 quotes concrete service types, not their interfaces. Bind its
            // exact named placeholders while preserving the quoted parameter's type.
            if (node.Name == "relationalTypeMappingSource")
            {
                return Expression.Constant(_context.GetService<IRelationalTypeMappingSource>(), node.Type);
            }

            if (node.Name == "relationalModel")
            {
                return Expression.Constant(_context.Model.GetRelationalModel(), node.Type);
            }

            return base.VisitParameter(node);
        }
    }

    private sealed class ExpressionContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public ExpressionContext(
            DbContextOptions<ExpressionContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder
            .Entity<ExpressionTarget>()
            .ToTable("ExpressionTargets");
    }

    private sealed class ExpressionTarget
    {
        public int Id { get; set; }
        public int Value { get; set; }
    }
}
