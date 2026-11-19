namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectIntegrationTests
{
    private static readonly string[] s_allNavigationRows =
        ["10|visible", "20|visible", "30|hidden", "40|visible", "50|visible"];

    private static readonly string[] s_filteredNavigationRows = ["10|visible", "20|visible"];
    private static readonly string[] s_cteRows = ["10|server-default|20", "20|server-default|40"];

    private static readonly string[] s_composedRows =
        ["1|server-default|2", "2|server-default|4", "3|server-default|6"];

    /// <summary>Reports each independently composable relational source shape.</summary>
    public static TheoryData<string, string> SourceCases
    {
        get
        {
            var data = new TheoryData<string, string>();

            foreach (var target in SelectedTargets)
            {
                data.Add(target, "join-bag");
                data.Add(target, "distinct");
                data.Add(target, "aggregate");
                data.Add(target, "concat");
                data.Add(target, "union");
                data.Add(target, "intersect");
                data.Add(target, "except");
                data.Add(target, "page");
                data.Add(target, "from-sql");
            }

            return data;
        }
    }

    /// <summary>Reports ordinary CTE, bounded recursion and window projections as independent source forms.</summary>
    public static TheoryData<string, string> ComposedSourceCases
    {
        get
        {
            var data = new TheoryData<string, string>();

            foreach (var target in SelectedTargets)
            {
                data.Add(target, "cte");
                data.Add(target, "recursive");
                data.Add(target, "window");
            }

            return data;
        }
    }

    /// <summary>Bag multiplicity and composed relational values survive without a source preload.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SourceCases))]
    public async Task Relational_source_shapes_persist_the_exact_occurrence_bag(
        string target,
        string shape
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        var source = context.Set<InsertSource>();
        var values = source.Select(row => row.Value);
        var expected = shape switch
        {
            "join-bag" => new[]
            {
                10,
                10,
                20,
                20,
                30,
            },
            "distinct" => [10, 20, 30],
            "aggregate" => [30, 30],
            "concat" => [10, 10, 20, 20, 30, 30],
            "union" => [10, 20, 30],
            "intersect" => [20],
            "except" => [10, 30],
            "page" => [20],
            "from-sql" => [20, 30],
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var query = shape switch
        {
            // WHY: Joining two repeated ParentId occurrences must preserve five SQL rows, not three distinct entities.
            "join-bag" => source.Join(source,
                left => left.ParentId,
                right => right.ParentId,
                (left, right) => left.Value),
            "distinct" => values
                .Concat(values)
                .Distinct(),
            "aggregate" => source
                .GroupBy(row => row.ParentId)
                .Select(group => group.Sum(row => row.Value)),
            "concat" => values.Concat(values),
            "union" => values.Union(values),
            "intersect" => values.Intersect(values.Where(value => value == 20)),
            "except" => values.Except(values.Where(value => value == 20)),
            "page" => source
                .OrderBy(row => row.Id)
                .Skip(1)
                .Take(1)
                .Select(row => row.Value),
            "from-sql" => source
                .FromSql($"SELECT * FROM `Insert select source` WHERE `Value` > {15}")
                .Select(row => row.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var count = await query.ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
            setters => setters
                .SetProperty(row => row.Value, value => value)
                .SetProperty(row => row.Code, value => "shape"),
            safety.Token);

        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT `Value` FROM `{TargetTable}` ORDER BY `Value`;",
            safety.Token);

        // Assert
        Assert.Equal(expected.Length, count);
        Assert.Equal(expected.Select(value => value.ToString(CultureInfo.InvariantCulture)), actual);
        Assert.Single(commands.Statements);
        Assert.Equal(0, commands.Readers);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Source and navigated filters remain normal EF filters; explicit opt-out is separate.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(EmptyCases))]
    public async Task Source_and_navigation_filters_remain_effective_with_explicit_opt_out(
        string target,
        bool ignoreFilters
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var source = context
            .Set<InsertSource>()
            .AsQueryable();

        if (ignoreFilters)
        {
            source = source.IgnoreQueryFilters();
        }

        // Act
        var count = await source
            .Select(row => new
            {
                row.Value,
                ParentVisible = row.Parent.Visible,
            })
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code,
                        row => row.ParentVisible
                            ? "visible"
                            : "hidden"),
                safety.Token);

        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT `Value`,`order` FROM `{TargetTable}` ORDER BY `Value`;",
            safety.Token);

        // Assert
        Assert.Equal(ignoreFilters
                ? 5
                : 2,
            count);

        Assert.Equal(ignoreFilters
                ? s_allNavigationRows
                : s_filteredNavigationRows,
            actual);
    }

    /// <summary>A target query filter is not an insert authorization or tenant-validation policy.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Target_filter_does_not_rewrite_or_authorize_an_explicit_tenant_assignment(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();

        // Act
        var count = await context
            .Set<InsertSource>()
            .Where(row => row.Id == 1)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => row.Label)
                    .SetProperty(row => row.TenantId, row => 2),
                safety.Token);

        var native = await ReadNativeAsync(store.Observer, $"SELECT `TenantId` FROM `{TargetTable}`;", safety.Token);
        var visible = await context
            .Set<InsertTarget>()
            .CountAsync(safety.Token);

        // Assert
        Assert.Equal(1, count);
        Assert.Equal(["2"], native);
        Assert.Equal(0, visible);
    }

    /// <summary>A legal top-level self-source copies the original selection once with no client staging.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Top_level_same_table_source_copies_only_the_original_rows(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await ExecuteNativeAsync(store.Observer,
            $"INSERT INTO `{TargetTable}` (`Value`,`order`,`TenantId`) VALUES (11,'first',1),(22,'second',1);",
            safety.Token);

        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var count = await context
            .Set<InsertTarget>()
            .Where(row => row.Value < 30)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value + 100)
                    .SetProperty(row => row.Code, row => row.Code),
                safety.Token);

        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT `Value` FROM `{TargetTable}` ORDER BY `Value`;",
            safety.Token);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(["11", "22", "111", "122"], actual);
        Assert.Single(commands.Statements);
        Assert.Equal(0, commands.Readers);
    }

    /// <summary>Qualified CTE/window sources use native INSERT WITH SELECT placement and generated-column omission.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(ComposedSourceCases))]
    public async Task Cte_recursive_and_window_sources_feed_one_native_insert(
        string target,
        string shape
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        var source = context.Set<InsertSource>();
        var query = shape switch
        {
            "cte" => source
                .Where(row => row.Id <= 2)
                .WithCte(rows => rows.Select(row => row.Value)),
            "recursive" => source
                .Where(row => row.Id == 1)
                .Select(row => row.Id)
                .WithRecursiveCte(frontier => frontier.SelectMany(id => source
                        .Where(row => row.Id == id + 1)
                        .Select(row => row.Id)),
                    rows => rows,
                    2),
            "window" => source.Select(row => (int)EF
                .Functions
                .Window()
                .OrderBy(row.Id)
                .RowNumber()),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var count = await query.ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
            setters => setters
                .SetProperty(row => row.Value, value => value)
                .SetProperty(row => row.Code, value => "composed"),
            safety.Token);

        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT `Value`,`Optional`,`Computed` FROM `{TargetTable}` ORDER BY `Value`;",
            safety.Token);

        // Assert
        Assert.Equal(shape == "cte"
                ? 2
                : 3,
            count);

        Assert.Equal(shape == "cte"
                ? s_cteRows
                : s_composedRows,
            actual);

        var sql = Assert.Single(commands.Statements);
        Assert.StartsWith("INSERT INTO", sql.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(shape == "window"
                ? "ROW_NUMBER() OVER"
                : "WITH",
            sql,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, commands.Readers);
    }
}
