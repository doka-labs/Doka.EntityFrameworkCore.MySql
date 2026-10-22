namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies provider-specific relational query-compilation behavior.
/// </summary>
public sealed class MySqlQueryCompilationContextTests
{
    /// <summary>
    /// Verifies that the provider replaces EF Core's default relational compilation-context factory.
    /// </summary>
    [Fact]
    public void Provider_registers_its_query_compilation_context_factory()
    {
        // Arrange
        using var context = CreateContext(QuerySplittingBehavior.SingleQuery);

        // Act
        var factory = context.GetService<
            Microsoft.EntityFrameworkCore.Query.IQueryCompilationContextFactory>();

        // Assert
        Assert.IsType<MySqlQueryCompilationContextFactory>(factory);
    }

    /// <summary>
    /// Verifies that single-query execution remains streaming for synchronous and asynchronous queries.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Single_query_does_not_force_buffering(
        bool async
    )
    {
        // Arrange
        using var context = CreateContext(QuerySplittingBehavior.SingleQuery);

        // Act
        var compilationContext = CreateCompilationContext(context, async);

        // Assert
        Assert.False(compilationContext.IsBuffering);
    }

    /// <summary>
    /// Verifies that split-query execution buffers before reusing the MySqlConnector connection.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Split_query_forces_buffering(
        bool async
    )
    {
        // Arrange
        using var context = CreateContext(QuerySplittingBehavior.SplitQuery);

        // Act
        var compilationContext = CreateCompilationContext(context, async);

        // Assert
        Assert.True(compilationContext.IsBuffering);
    }

    /// <summary>
    /// Verifies that precompiled split queries preserve the same buffering contract.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [System.Diagnostics.CodeAnalysis.Experimental("EF9100")]
    public void Precompiled_split_query_forces_buffering(
        bool async
    )
    {
        // Arrange
        using var context = CreateContext(QuerySplittingBehavior.SplitQuery);
        var factory = context.GetService<
            Microsoft.EntityFrameworkCore.Query.IQueryCompilationContextFactory>();

        // Act
        var actual = factory.CreatePrecompiled(async);

        // Assert
        var compilationContext = Assert.IsType<MySqlQueryCompilationContext>(
            actual);

        Assert.True(compilationContext.SupportsPrecompiledQuery);
        Assert.True(compilationContext.IsBuffering);
    }

    private static MySqlQueryCompilationContext CreateCompilationContext(
        DbContext context,
        bool async
    )
    {
        var factory = context.GetService<
            Microsoft.EntityFrameworkCore.Query.IQueryCompilationContextFactory>();

        return Assert.IsType<MySqlQueryCompilationContext>(factory.Create(async));
    }

    private static QueryCompilationContextTestContext CreateContext(
        QuerySplittingBehavior querySplittingBehavior
    )
    {
        var optionsBuilder = new DbContextOptionsBuilder<QueryCompilationContextTestContext>();

        optionsBuilder.UseMySql(
            "Server=localhost;Database=doka;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.UseQuerySplittingBehavior(querySplittingBehavior));

        return new QueryCompilationContextTestContext(optionsBuilder.Options);
    }

    private sealed class QueryCompilationContextTestContext : DbContext
    {
        public QueryCompilationContextTestContext(
            DbContextOptions<QueryCompilationContextTestContext> options
        ) : base(options) { }
    }
}
