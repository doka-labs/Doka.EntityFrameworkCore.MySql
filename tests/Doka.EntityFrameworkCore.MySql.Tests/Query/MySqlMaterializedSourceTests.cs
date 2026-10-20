namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the public construction boundary independently of database execution.
/// </summary>
public sealed class MySqlMaterializedSourceTests
{
    private static readonly int[] s_values = [1];

    /// <summary>A null source fails at the public boundary.</summary>
    [Fact]
    public void Null_source_is_rejected()
    {
        var error = Assert.Throws<ArgumentNullException>(() =>
            MySqlQueryableExtensions.WithMaterializedSource<int, int>(null!, source => source));

        Assert.Equal("source", error.ParamName);
    }

    /// <summary>A missing composition expression is rejected before provider access.</summary>
    [Fact]
    public void Null_composition_is_rejected()
    {
        var source = s_values.AsQueryable();

        var error = Assert.Throws<ArgumentNullException>(() => source.WithMaterializedSource<int, int>(null!));

        Assert.Equal("compose", error.ParamName);
    }

    /// <summary>LINQ-to-Objects cannot accidentally execute a SQL source-binding callback.</summary>
    [Fact]
    public void In_memory_source_is_rejected()
    {
        var source = s_values.AsQueryable();

        var error = Assert.Throws<InvalidOperationException>(() => source.WithMaterializedSource(rows => rows));

        Assert.Contains("provider", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Equivalent public constructions preserve a stable ordinary EF expression shape.</summary>
    [Fact]
    public void Construction_keeps_expression_shape_and_provider()
    {
        using var context = new GuardContext();
        var source = context.Set<GuardItem>().Where(item => item.Id > 1);
        var previous = source.WithMaterializedSource(rows => rows.Select(item => item.Id));

        var actual = source.WithMaterializedSource(rows => rows.Select(item => item.Id));

        Assert.Same(source.Provider, actual.Provider);
        Assert.True(ExpressionEqualityComparer.Instance.Equals(previous.Expression, actual.Expression));
        Assert.Equal(typeof(IQueryable<int>), actual.Expression.Type);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Nested lexical references grow with their definitions rather than repeated dependency paths.</summary>
    [Fact]
    public void Nested_source_construction_has_bounded_allocations()
    {
        using var context = new GuardContext();
        var leaf = context.Set<GuardItem>().Select(item => item.Id);
        _ = CreateNested(leaf, 1).ToQueryString();
        var query = CreateNested(leaf, 10);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var sql = query.ToQueryString();

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // WHY: The original dependency-path expansion allocated over 250 MB for 3 KB of SQL.
        // This generous structural budget permits normal EF/JIT variation without a timing gate.
        Assert.InRange(allocated, 0, 2_000_000);
        Assert.Equal(10, Regex.Count(sql, "WHERE 1 = 0"));
        Assert.InRange(sql.Length, 1, 10_000);
    }

    private static IQueryable<int> CreateNested(
        IQueryable<int> source,
        int depth
    )
    {
        var compose = ((Func<IQueryable<int>, Expression<Func<IQueryable<int>, IQueryable<int>>>,
            IQueryable<int>>)MySqlQueryableExtensions.WithMaterializedSource<int, int>).Method;
        var concat = ((Func<IQueryable<int>, IEnumerable<int>, IQueryable<int>>)Queryable.Concat<int>).Method;

        return source.Provider.CreateQuery<int>(Nest(source.Expression, depth));

        Expression Nest(Expression input, int remaining)
        {
            if (remaining == 0)
            {
                return input;
            }

            var parameter = Expression.Parameter(typeof(IQueryable<int>), "source");
            var body = Nest(Expression.Call(concat, parameter, parameter), remaining - 1);

            return Expression.Call(compose, input, Expression.Quote(
                Expression.Lambda<Func<IQueryable<int>, IQueryable<int>>>(body, parameter)));
        }
    }

    private sealed class GuardContext : DbContext
    {
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        ) => optionsBuilder.UseMySql(
            "Server=localhost;Database=unused;User ID=unused;Password=unused",
            MySqlServerVersion.MariaDb(new Version(11, 8, 8)));

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<GuardItem>().HasKey(item => item.Id);
    }

    private sealed class GuardItem
    {
        public int Id { get; set; }
    }
}
