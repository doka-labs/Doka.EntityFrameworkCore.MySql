namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Guards the deferred CTE construction boundary independently of SQL execution.</summary>
public sealed partial class MySqlCteTests
{
    private static readonly int[] s_values = [1];

    /// <summary>A missing source fails before a provider or composition can be inspected.</summary>
    [Fact]
    public void Null_source_is_rejected()
    {
        // Arrange
        static void act() => MySqlQueryableExtensions.WithCte<int, int>(null!, rows => rows);

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.Equal("source",
            Assert.IsType<ArgumentNullException>(exception)
                .ParamName);
    }

    /// <summary>A missing composition fails before the source provider is invoked.</summary>
    [Fact]
    public void Null_composition_is_rejected()
    {
        // Arrange
        var source = s_values.AsQueryable();

        void act() => source.WithCte<int, int>(null!);

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.Equal("compose",
            Assert.IsType<ArgumentNullException>(exception)
                .ParamName);
    }

    /// <summary>LINQ-to-Objects cannot silently replace the server-side relation with a local query.</summary>
    [Fact]
    public void In_memory_source_is_rejected()
    {
        // Arrange
        var source = s_values.AsQueryable();

        void act() => source.WithCte(rows => rows);

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.Contains("provider",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The CTE marker retains the provider and a stable ordinary expression-tree identity.</summary>
    [Fact]
    public void Construction_retains_the_deferred_provider_and_expression_shape()
    {
        // Arrange
        using var context = new CteGuardContext();
        var source = context
            .Set<CteGuardRow>()
            .Where(row => row.Id > 1);

        var previous = source.WithCte(rows => rows.Select(row => row.Id));

        // Act
        var actual = source.WithCte(rows => rows.Select(row => row.Id));

        // Assert
        Assert.Same(source.Provider, actual.Provider);
        Assert.True(ExpressionEqualityComparer.Instance.Equals(previous.Expression, actual.Expression));
        Assert.Equal(typeof(IQueryable<int>), actual.Expression.Type);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Common-table-expression identity never comes from an unquoted public alias string.</summary>
    [Fact]
    public void Public_ordinary_cte_operator_exposes_no_alias_or_sql_fragment_parameter()
    {
        // Arrange
        var method = MySqlQueryableExtensions.WithCteMethod;

        // Act
        var parameters = method.GetParameters();

        // Assert
        Assert.Equal(2, parameters.Length);
        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(string));
    }

    /// <summary>Hosts a mapped query without a database connection or command.</summary>
    private sealed class CteGuardContext : DbContext
    {
        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        ) => optionsBuilder.UseMySql(
            "Server=localhost;Database=cte_guard;User ID=unused;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder
            .Entity<CteGuardRow>()
            .HasKey(row => row.Id);
    }

    /// <summary>Provides the smallest keyed source needed by deferred API construction.</summary>
    private sealed class CteGuardRow
    {
        /// <summary>Gets or sets the mapped key.</summary>
        public int Id { get; set; }
    }
}
