namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies immutable scalar window structure and its actual reconstruction contracts.</summary>
public sealed partial class MySqlWindowExpressionTests : IDisposable
{
    // WHY: These cases test expressions, not global provider reuse. Owning the graph
    // keeps them independent of EF's process-wide provider-count diagnostic.
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddEntityFrameworkDokaMySql()
        .BuildServiceProvider();

    /// <inheritdoc />
    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Unchanged children reuse the original node instead of cloning its schema.</summary>
    [Fact]
    public void Unchanged_visit_preserves_expression_identity()
    {
        // Arrange
        var window = CreateWindow();

        // Act
        var visited = new IdentityVisitor().Visit(window);

        // Assert
        Assert.Same(window, visited);
    }

    /// <summary>A rewrite visits function, partition, ordering and frame-offset children together.</summary>
    [Fact]
    public void Changed_visit_updates_every_window_child_without_mutating_the_original()
    {
        // Arrange
        var window = CreateWindow();

        // Act
        var visited = Assert.IsType<MySqlWindowExpression>(new IncrementVisitor().Visit(window));

        // Assert
        Assert.NotSame(window, visited);
        Assert.Equal(11,
            Assert.IsType<SqlConstantExpression>(visited.Arguments[0])
                .Value);

        Assert.Equal(12,
            Assert.IsType<SqlConstantExpression>(visited.Partitions[0])
                .Value);

        Assert.Equal(13,
            Assert.IsType<SqlConstantExpression>(visited.Orderings[0].Expression)
                .Value);

        Assert.Equal(14,
            Assert.IsType<SqlConstantExpression>(visited.Frame!.StartOffset)
                .Value);

        Assert.Equal(1,
            Assert.IsType<SqlConstantExpression>(window.Arguments[0])
                .Value);

        Assert.Equal(4,
            Assert.IsType<SqlConstantExpression>(window.Frame!.StartOffset)
                .Value);
    }

    /// <summary>Changing one frame facet changes expression identity, equality and cache shape.</summary>
    [Fact]
    public void Frame_change_is_part_of_expression_equality()
    {
        // Arrange
        var original = CreateWindow();
        var frame = original.Frame! with { Rows = false };

        // Act
        var changed = original.Update(original.Arguments, original.Partitions, original.Orderings, frame);

        // Assert
        Assert.NotEqual(original, changed);
        Assert.True(original.Frame!.Rows);
        Assert.False(changed.Frame!.Rows);
    }

    /// <summary>Input collection mutation cannot alter an already-created window node.</summary>
    [Fact]
    public void Constructor_owns_its_child_collection_snapshots()
    {
        // Arrange
        SqlExpression[] arguments = [Constant(1)];
        var window = new MySqlWindowExpression(MySqlWindowFunction.Sum,
            arguments,
            [],
            [],
            null,
            typeof(int),
            IntTypeMapping.Default,
            nullable: true);

        // Act
        arguments[0] = Constant(99);

        // Assert
        Assert.Equal(1,
            Assert.IsType<SqlConstantExpression>(window.Arguments[0])
                .Value);
    }

    /// <summary>Quotation reconstructs function, mapping, ordering and real frame parameter children.</summary>
    [Fact]
    [Experimental("EF9100")]
    public void Quote_round_trip_preserves_the_complete_window()
    {
        // Arrange
        using var context = CreateContext();
        var mappings = context.GetService<IRelationalTypeMappingSource>();
        var window = CreateWindow(mappings.FindMapping(typeof(int))!);
        var quoted = new TypeMappingBindingVisitor(mappings).Visit(window.Quote());

        // Act
        var reconstructed = Expression
            .Lambda<Func<MySqlWindowExpression>>(quoted)
            .Compile()();

        // Assert
        Assert.Equal(window, reconstructed);
        Assert.Equal(window.GetHashCode(), reconstructed.GetHashCode());
        Assert.NotSame(window, reconstructed);
        Assert.True(reconstructed.IsNullable);
        Assert.Equal(MySqlWindowFunction.Sum, reconstructed.Function);
        Assert.Equal(window.Frame, reconstructed.Frame);
    }

    /// <summary>The typed public endpoint rejects negative offsets instead of carrying invalid SQL fragments.</summary>
    /// <param name="following">Whether the endpoint follows rather than precedes.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Endpoint_factories_reject_negative_values(
        bool following
    )
    {
        // Arrange
        const decimal offset = -1;

        // Act
        var exception = Record.Exception(() => following
            ? MySqlWindowFrameBound.Following(offset)
            : MySqlWindowFrameBound.Preceding(offset));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>Public window markers fail clearly when executed outside translation.</summary>
    [Fact]
    public void Window_entry_has_no_client_implementation()
    {
        // Arrange
        var functions = EF.Functions;

        // Act
        var exception = Record.Exception(() => functions.Window());

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>The receiver remains a translation contract even when nonpublic construction is attempted.</summary>
    [Fact]
    public void Window_definition_has_no_constructible_runtime_instance()
    {
        // Arrange
        var type = typeof(MySqlWindowDefinition);

        // Act
        var exception = Record.Exception(() => Activator.CreateInstance(type, nonPublic: true));

        // Assert
        Assert.IsAssignableFrom<MemberAccessException>(exception);
    }

    private static MySqlWindowExpression CreateWindow() => CreateWindow(IntTypeMapping.Default);

    private static MySqlWindowExpression CreateWindow(
        RelationalTypeMapping mapping
    ) => new(
        MySqlWindowFunction.Sum,
        [new SqlConstantExpression(1, mapping)],
        [new SqlConstantExpression(2, mapping)],
        [new OrderingExpression(new SqlConstantExpression(3, mapping), ascending: true)],
        new MySqlWindowFrame(true,
            MySqlWindowBoundKind.Preceding,
            new SqlConstantExpression(4, mapping),
            MySqlWindowBoundKind.CurrentRow,
            null),
        typeof(int),
        mapping,
        nullable: true);

    private static SqlConstantExpression Constant(
        int value
    ) => new(value, IntTypeMapping.Default);

    private WindowContext CreateContext() => new(new DbContextOptionsBuilder<WindowContext>()

        // WHY: These expression tests do not test global provider caching. Each xUnit
        // case owns its graph so its distinct profile cannot affect unrelated tests.
        .UseInternalServiceProvider(_services)
        .UseMySql("Server=localhost;Database=window_quote;User ID=unused;", MySqlServerVersion.MySql(new Version(8, 4)))
        .Options);

    private sealed class WindowContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public WindowContext(
            DbContextOptions<WindowContext> options
        ) : base(options) { }
    }

    private sealed class IdentityVisitor : ExpressionVisitor { }

    private sealed class IncrementVisitor : ExpressionVisitor
    {
        protected override Expression VisitExtension(
            Expression node
        ) => node is SqlConstantExpression { Value: int value }
            ? Constant(value + 10)
            : base.VisitExtension(node);
    }

    private sealed class TypeMappingBindingVisitor : ExpressionVisitor
    {
        private readonly IRelationalTypeMappingSource _source;

        /// <summary>Initializes the visitor with the mappings and expressions used by this test.</summary>
        public TypeMappingBindingVisitor(
            IRelationalTypeMappingSource source
        )
        {
            _source = source;
        }

        protected override Expression VisitParameter(
            ParameterExpression node
        ) => node.Name == "relationalTypeMappingSource"
            ? Expression.Constant(_source, node.Type)
            : base.VisitParameter(node);
    }
}
