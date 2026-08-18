namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Query;

/// <summary>
/// Verifies temporal query composition across navigation and set-operation boundaries.
/// </summary>
public sealed class MySqlTemporalQueryCompositionTests
{
    private static readonly DateTime s_firstPoint =
        new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private static readonly DateTime s_secondPoint =
        new(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    /// <summary>
    /// TemporalAsOf carries one database instant across every separately stored
    /// temporal entity reached by navigation expansion.
    /// </summary>
    [Fact]
    public void TemporalAsOf_propagates_to_temporal_include()
    {
        using var context = CreateContext<AllTemporalConfiguration>();

        var sql = context
            .Parents.TemporalAsOf(s_firstPoint)
            .Include(parent => parent.Children)
            .ToQueryString();

        Assert.Equal(2, CountOccurrences(sql, "FOR SYSTEM_TIME AS OF"));
    }

    /// <summary>
    /// A current-only table cannot participate in a historical graph because its
    /// rows do not describe the requested database instant.
    /// </summary>
    [Fact]
    public void TemporalAsOf_rejects_current_only_include()
    {
        using var context = CreateContext<CurrentChildrenConfiguration>();

        var exception = Assert.Throws<InvalidOperationException>(() => context
            .Parents.TemporalAsOf(s_firstPoint)
            .Include(parent => parent.Children)
            .ToQueryString());

        Assert.Contains("reached non-temporal entity", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// EF Core implicitly expands owned navigations during entity materialization, so a
    /// separately stored current-only collection cannot be mixed with historical owners.
    /// </summary>
    [Fact]
    public void TemporalAsOf_rejects_implicit_current_only_owned_collection()
    {
        using var context = CreateContext<CurrentOwnedCollectionConfiguration>();

        var exception = Assert.Throws<InvalidOperationException>(() => context
            .Parents
            .TemporalAsOf(s_firstPoint)
            .ToQueryString());

        Assert.Contains("separately stored, non-temporal owned entity", exception.Message, StringComparison.Ordinal);
        Assert.Contains("even when Include is omitted", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "Mixing historical owner rows with current owned rows",
            exception.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// IgnoreAutoIncludes does not suppress EF Core's implicit owned-navigation expansion.
    /// </summary>
    [Fact]
    public void IgnoreAutoIncludes_does_not_mix_current_owned_rows_into_temporal_query()
    {
        using var context = CreateContext<CurrentOwnedCollectionConfiguration>();

        var exception = Assert.Throws<InvalidOperationException>(() => context
            .Parents
            .TemporalAsOf(s_firstPoint)
            .IgnoreAutoIncludes()
            .ToQueryString());

        Assert.Contains("IgnoreAutoIncludes is used", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scalar projection remains valid because it does not materialize the separately
    /// stored current-only owned collection.
    /// </summary>
    [Fact]
    public void TemporalAsOf_scalar_projection_excludes_current_owned_collection()
    {
        using var context = CreateContext<CurrentOwnedCollectionConfiguration>();

        var sql = context
            .Parents
            .TemporalAsOf(s_firstPoint)
            .Select(parent => new
            {
                parent.Id,
                parent.Profile.DisplayName,
            })
            .ToQueryString();

        Assert.Contains("FOR SYSTEM_TIME AS OF", sql, StringComparison.Ordinal);
        Assert.Contains("DisplayName", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("TemporalParentNotes", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Multi-version roots cannot define one consistent instant for a separately
    /// stored related collection and are rejected before SQL generation.
    /// </summary>
    [Fact]
    public void TemporalAll_rejects_navigation_across_separate_temporal_table()
    {
        using var context = CreateContext<AllTemporalConfiguration>();

        var exception = Assert.Throws<InvalidOperationException>(() => context
            .Parents.TemporalAll()
            .Include(parent => parent.Children)
            .ToQueryString());

        Assert.Contains("only for TemporalAsOf", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Equivalent temporal roots can participate in set operations without losing
    /// the temporal query-root metadata.
    /// </summary>
    [Fact]
    public void Matching_temporal_roots_support_set_operations()
    {
        using var context = CreateContext<AllTemporalConfiguration>();

        var first = context
            .Parents.TemporalAsOf(s_firstPoint)
            .Where(parent => parent.Id < 10);

        var second = context
            .Parents.TemporalAsOf(s_firstPoint)
            .Where(parent => parent.Id >= 10);

        var sql = first
            .Union(second)
            .ToQueryString();

        Assert.Contains("UNION", sql, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(sql, "FOR SYSTEM_TIME AS OF"));
    }

    /// <summary>
    /// Different temporal boundaries cannot be merged into one set operation because
    /// the resulting query would not describe one temporal contract.
    /// </summary>
    [Fact]
    public void Mismatched_temporal_roots_reject_set_operations()
    {
        using var context = CreateContext<AllTemporalConfiguration>();

        var first = context.Parents.TemporalAsOf(s_firstPoint);
        var second = context.Parents.TemporalAsOf(s_secondPoint);

        var exception = Assert.Throws<InvalidOperationException>(() => first
            .Union(second)
            .ToQueryString());

        Assert.Contains("identical UTC boundaries", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Current and historical roots cannot be combined as entity roots because their
    /// identity and change-tracking semantics are intentionally different.
    /// </summary>
    [Fact]
    public void Temporal_and_current_roots_reject_set_operations()
    {
        using var context = CreateContext<AllTemporalConfiguration>();

        var exception = Assert.Throws<InvalidOperationException>(() => context
            .Parents.TemporalAll()
            .Union(context.Parents)
            .ToQueryString());

        Assert.Contains("matching temporal operators", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Aggregates remain server-side operations over the complete native history source.
    /// </summary>
    [Fact]
    public void Temporal_root_supports_grouped_aggregates()
    {
        using var context = CreateContext<AllTemporalConfiguration>();

        var sql = context
            .Parents.TemporalAll()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.Count(),
                MinimumId = group.Min(parent => parent.Id),
                MaximumId = group.Max(parent => parent.Id),
            })
            .ToQueryString();

        Assert.Contains("COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("MIN(", sql, StringComparison.Ordinal);
        Assert.Contains("MAX(", sql, StringComparison.Ordinal);
        Assert.Contains("FOR SYSTEM_TIME ALL", sql, StringComparison.Ordinal);
    }

    private static TemporalCompositionContext<TConfiguration> CreateContext<TConfiguration>()
        where TConfiguration : ICompositionConfiguration, new()
    {
        var options = new DbContextOptionsBuilder<TemporalCompositionContext<TConfiguration>>().UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=password;",
                MySqlServerVersion.MariaDb(new Version(11, 4, 0)))
            .Options;

        return new TemporalCompositionContext<TConfiguration>(options);
    }

    private static int CountOccurrences(
        string value,
        string fragment
    )
    {
        var count = 0;
        var index = 0;

        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }

    private interface ICompositionConfiguration
    {
        bool ChildrenAreTemporal { get; }

        bool ConfigureCurrentOwnedCollection => false;
    }

    private sealed class AllTemporalConfiguration : ICompositionConfiguration
    {
        public bool ChildrenAreTemporal => true;
    }

    private sealed class CurrentChildrenConfiguration : ICompositionConfiguration
    {
        public bool ChildrenAreTemporal => false;
    }

    private sealed class CurrentOwnedCollectionConfiguration : ICompositionConfiguration
    {
        public bool ChildrenAreTemporal => true;

        public bool ConfigureCurrentOwnedCollection => true;
    }

    private sealed class TemporalCompositionContext<TConfiguration> : DbContext
        where TConfiguration : ICompositionConfiguration, new()
    {
        public TemporalCompositionContext(
            DbContextOptions<TemporalCompositionContext<TConfiguration>> options
        ) : base(options) { }

        public DbSet<TemporalParent> Parents => Set<TemporalParent>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var configuration = new TConfiguration();

            modelBuilder.Entity<TemporalParent>(entity =>
            {
                entity.ToTable("TemporalParents", table => table.IsTemporal());
                entity.HasKey(parent => parent.Id);
                entity
                    .HasMany(parent => parent.Children)
                    .WithOne(child => child.Parent)
                    .HasForeignKey(child => child.ParentId)
                    .OnDelete(DeleteBehavior.Restrict);

                if (configuration.ConfigureCurrentOwnedCollection)
                {
                    entity.OwnsOne(parent => parent.Profile);
                    entity.OwnsMany(
                        parent => parent.Notes,
                        owned =>
                        {
                            owned.ToTable("TemporalParentNotes");
                            owned.WithOwner().HasForeignKey("ParentId");
                            owned.Property<int>("Id");
                            owned.HasKey("ParentId", "Id");
                        });
                }
                else
                {
                    entity.Ignore(parent => parent.Profile);
                    entity.Ignore(parent => parent.Notes);
                }
            });

            modelBuilder.Entity<TemporalChild>(entity =>
            {
                entity.ToTable("TemporalChildren", table => table.IsTemporal(configuration.ChildrenAreTemporal));
                entity.HasKey(child => child.Id);
            });
        }
    }

    private sealed class TemporalParent
    {
        public int Id { get; set; }

        public ICollection<TemporalChild> Children { get; } = [];

        public TemporalProfile Profile { get; set; } = new();

        public ICollection<TemporalNote> Notes { get; } = [];
    }

    private sealed class TemporalChild
    {
        public int Id { get; set; }

        public int ParentId { get; set; }

        public TemporalParent Parent { get; set; } = null!;
    }

    private sealed class TemporalProfile
    {
        public string DisplayName { get; set; } = null!;
    }

    private sealed class TemporalNote
    {
        public string Text { get; set; } = null!;
    }
}
