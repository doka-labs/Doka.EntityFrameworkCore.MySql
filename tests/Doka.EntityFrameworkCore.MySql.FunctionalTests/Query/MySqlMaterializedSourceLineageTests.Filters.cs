using System.Text.RegularExpressions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMaterializedSourceLineageTests
{
    /// <summary>Independent source and outer filters keep their distinct context-method parameter values.</summary>
    [Theory]
    [InlineData(false, 11, 22, 33)]
    [InlineData(true, 11, 22, 33)]
    [InlineData(false, 14, 25, 36)]
    [InlineData(true, 14, 25, 36)]
    public void Context_filter_methods_keep_distinct_statement_parameters(
        bool mariaDb,
        int firstTenant,
        int secondTenant,
        int outerTenant
    )
    {
        // Arrange
        using var context = CreateFilterContext(mariaDb);
        context.FirstTenantId = firstTenant;
        context.SecondTenantId = secondTenant;
        context.OuterTenantId = outerTenant;
        var first = context.First.WithMaterializedSource(shared => shared);
        var second = context.Second.WithMaterializedSource(shared => shared);

        // Act
        var query = first.Join(second, row => row.Id, row => row.Id, (left, _) => left.Id)
            .Join(context.Outer, id => id, row => row.Id, (id, _) => id);

        var sql = query.ToQueryString();

        var declarations = Regex.Matches(sql, @"(?m)^(?:SET |-- )(?<name>@\w+)\s*=\s*'?(?<value>\d+)'?");
        var expectedValues = new[] { firstTenant, secondTenant, outerTenant }
            .OrderBy(value => value)
            .ToArray();

        var actualValues = declarations.Select(match => int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture))
            .OrderBy(value => value)
            .ToArray();

        // Assert
        Assert.True(expectedValues.SequenceEqual(actualValues), sql);
        Assert.Equal(3, declarations.Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(1, CountOccurrences(sql, "FROM `LineageFilterFirst`"));
        Assert.Equal(1, CountOccurrences(sql, "FROM `LineageFilterSecond`"));
        Assert.Equal(1, CountOccurrences(sql, "FROM `LineageFilterOuter`"));
    }

    /// <summary>A completed random page applies its volatile model filter once, before references reuse it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Volatile_root_filter_is_not_reapplied_after_random_paging(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateFilterContext(mariaDb);

        // Act
        var query = context.Volatile.OrderBy(_ => EF.Functions.Random()).Skip(1).Take(3)
            .WithMaterializedSource(shared => shared.Concat(shared));

        var sql = query.ToQueryString();

        // WHY: Paging may project its random ordering expression too; only the selection predicate must occur once.

        // Assert
        Assert.Single(Regex.Matches(sql, @"RAND\(\)\s*>\s*0\.5"));
        Assert.Equal(1, CountOccurrences(sql, "FROM `LineageVolatile`"));
        Assert.Equal(1, CountOccurrences(sql, "LIMIT"));
        Assert.Equal(1, CountOccurrences(sql, "OFFSET"));
    }

    /// <summary>A bound root does not suppress the filter of a navigated entity type.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Navigated_target_filter_remains_after_binding(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateFilterContext(mariaDb);

        // Act
        var query = context.NavigationSources.WithMaterializedSource(shared =>
            shared.Select(row => row.Target.Name));

        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(sql, "FROM `LineageNavigationSources`"));
        Assert.Equal(1, CountOccurrences(sql, "`LineageNavigationTargets`"));
        // WHY: Name alone is projected; IsVisible can occur only when the target filter survives.
        Assert.Equal(1, CountOccurrences(sql, "`IsVisible`"));
    }

    /// <summary>An explicit filter opt-out applies to the navigated target as well.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ignoring_query_filters_removes_navigated_target_filter(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateFilterContext(mariaDb);

        // Act
        var query = context.NavigationSources.WithMaterializedSource(shared =>
                shared.Select(row => row.Target.Name))
            .IgnoreQueryFilters();

        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(sql, "`LineageNavigationTargets`"));
        Assert.DoesNotContain("`IsVisible`", sql, StringComparison.Ordinal);
    }

    /// <summary>Creates a context with separate filtered roots and one explicitly volatile filter.</summary>
    private static FilterContext CreateFilterContext(
        bool mariaDb
    )
    {
        var version = mariaDb
            ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

        var options = MySqlFunctionalTestOptions.CreateTransientBuilder<FilterContext>()
            .UseMySql("Server=localhost;Database=lineage;User ID=unused;", version)
            .Options;

        return new FilterContext(options);
    }

    /// <summary>Exercises EF's statement-wide context-filter parameter namespace.</summary>
    private sealed class FilterContext : DbContext
    {
        /// <summary>Initializes the filter model.</summary>
        public FilterContext(DbContextOptions<FilterContext> options) : base(options) { }

        /// <summary>Gets or sets the first source filter value.</summary>
        public int FirstTenantId { get; set; } = 11;

        /// <summary>Gets or sets the second source filter value.</summary>
        public int SecondTenantId { get; set; } = 22;

        /// <summary>Gets or sets the outer query filter value.</summary>
        public int OuterTenantId { get; set; } = 33;

        /// <summary>Gets the first independently filtered source.</summary>
        public DbSet<FirstFiltered> First => Set<FirstFiltered>();

        /// <summary>Gets the second independently filtered source.</summary>
        public DbSet<SecondFiltered> Second => Set<SecondFiltered>();

        /// <summary>Gets the ordinary filtered outer query root.</summary>
        public DbSet<OuterFiltered> Outer => Set<OuterFiltered>();

        /// <summary>Gets the source with a volatile model filter.</summary>
        public DbSet<VolatileFiltered> Volatile => Set<VolatileFiltered>();

        /// <summary>Gets filtered sources with a separately filtered reference target.</summary>
        public DbSet<NavigationFilteredSource> NavigationSources => Set<NavigationFilteredSource>();

        /// <summary>Supplies a distinct method-valued context filter for the first entity.</summary>
        public int FirstTenant() => FirstTenantId;

        /// <summary>Supplies a distinct method-valued context filter for the second entity.</summary>
        public int SecondTenant() => SecondTenantId;

        /// <summary>Supplies a distinct method-valued context filter for the outer entity.</summary>
        public int OuterTenant() => OuterTenantId;

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<FirstFiltered>().ToTable("LineageFilterFirst")
                .HasQueryFilter(row => row.TenantId == FirstTenant());
            modelBuilder.Entity<SecondFiltered>().ToTable("LineageFilterSecond")
                .HasQueryFilter(row => row.TenantId == SecondTenant());
            modelBuilder.Entity<OuterFiltered>().ToTable("LineageFilterOuter")
                .HasQueryFilter(row => row.TenantId == OuterTenant());
            modelBuilder.Entity<VolatileFiltered>().ToTable("LineageVolatile")
                .HasQueryFilter(_ => EF.Functions.Random() > 0.5);

            modelBuilder.Entity<NavigationFilteredSource>().ToTable("LineageNavigationSources")
                .HasQueryFilter(row => row.TenantId == FirstTenant());
            modelBuilder.Entity<NavigationFilteredSource>()
                .HasOne(row => row.Target)
                .WithMany()
                .HasForeignKey(row => row.TargetId);

            modelBuilder.Entity<NavigationFilteredTarget>().ToTable("LineageNavigationTargets")
                .HasQueryFilter(row => row.IsVisible);
        }
    }

    /// <summary>Provides the first independently parameterized filtered row type.</summary>
    private sealed class FirstFiltered
    {
        /// <summary>Gets or sets the row key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the filtered tenant value.</summary>
        public int TenantId { get; set; }
    }

    /// <summary>Provides the second independently parameterized filtered row type.</summary>
    private sealed class SecondFiltered
    {
        /// <summary>Gets or sets the row key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the filtered tenant value.</summary>
        public int TenantId { get; set; }
    }

    /// <summary>Provides an ordinary filtered root outside both materialized sources.</summary>
    private sealed class OuterFiltered
    {
        /// <summary>Gets or sets the row key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the filtered tenant value.</summary>
        public int TenantId { get; set; }
    }

    /// <summary>Provides rows whose selection changes if a volatile filter is evaluated again.</summary>
    private sealed class VolatileFiltered
    {
        /// <summary>Gets or sets the row key.</summary>
        public int Id { get; set; }
    }

    /// <summary>Provides the bound root for a filtered reference-navigation regression.</summary>
    private sealed class NavigationFilteredSource
    {
        /// <summary>Gets or sets the row key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the source tenant value.</summary>
        public int TenantId { get; set; }

        /// <summary>Gets or sets the target key.</summary>
        public int TargetId { get; set; }

        /// <summary>Gets or sets the required to be filtered target.</summary>
        public NavigationFilteredTarget Target { get; set; } = null!;
    }

    /// <summary>Provides a navigation target whose filter must survive source binding.</summary>
    private sealed class NavigationFilteredTarget
    {
        /// <summary>Gets or sets the row key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the projected name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets whether the target may appear in a query.</summary>
        public bool IsVisible { get; set; }
    }
}
