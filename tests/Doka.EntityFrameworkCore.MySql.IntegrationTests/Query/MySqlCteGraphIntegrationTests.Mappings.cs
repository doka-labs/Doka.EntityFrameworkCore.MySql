namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>Reports ordinary and real recursive frontier mapping paths independently.</summary>
    public static TheoryData<string, bool> BindingCases
    {
        get
        {
            var cases = new TheoryData<string, bool>();

            foreach (var target in SelectedTargets)
            {
                cases.Add(target, false);
                cases.Add(target, true);
            }

            return cases;
        }
    }

    /// <summary>Reports EF's three runtime entity identity policies independently.</summary>
    public static TheoryData<string, string> TrackingCases
    {
        get
        {
            var cases = new TheoryData<string, string>();

            foreach (var target in SelectedTargets)
            {
                cases.Add(target, "tracking");
                cases.Add(target, "no-tracking");
                cases.Add(target, "identity-resolution");
            }

            return cases;
        }
    }

    /// <summary>CTE-P10/P11/P21 read exact native GUID, NULL, decimal, temporal and converted values through both binding kinds.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BindingCases))]
    public async Task Native_facet_values_and_converters_survive_definition_frontier_and_materialization(
        string target,
        bool recursive
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);
        var source = context.Facets.AsNoTracking();
        var query = recursive
            ? source.WithRecursiveCte(frontier => frontier, rows => rows.OrderBy(row => row.Id), 1)
            : source.WithCte(rows => rows.OrderBy(row => row.Id));

        // Act
        var actual = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(recursive
                ? 4
                : 2,
            actual.Length);

        var firstRows = actual
            .Where(row => row.Id == 1)
            .ToArray();

        var secondRows = actual
            .Where(row => row.Id == 2)
            .ToArray();

        Assert.Equal(recursive
                ? 2
                : 1,
            firstRows.Length);

        Assert.Equal(recursive
                ? 2
                : 1,
            secondRows.Length);

        Assert.All(firstRows,
            row =>
            {
                Assert.Equal(s_firstGuid, row.BinaryId);
                Assert.Equal(s_firstGuid, row.CharId);
                Assert.Equal(s_firstGuid, row.NativeId);
                Assert.Null(row.OptionalId);
                Assert.Null(row.OptionalNumber);
                Assert.Equal(12.3456m, row.Amount);
                Assert.Equal(s_firstMoment, row.Moment);
                Assert.Equal(GraphState.Ready, row.State);
                Assert.Equal(new GraphCode("code-one"), row.Code);
                Assert.Equal("\u00e9\ud83d\ude00", row.Label);
            });

        Assert.All(secondRows,
            row =>
            {
                Assert.Equal(s_secondGuid, row.BinaryId);
                Assert.Equal(s_secondGuid, row.CharId);
                Assert.Equal(s_secondNativeGuid, row.NativeId);
                Assert.Equal(s_secondGuid, row.OptionalId);
                Assert.Equal(7, row.OptionalNumber);
                Assert.Null(row.Amount);
                Assert.Equal(s_firstMoment, row.Moment);
                Assert.Equal(GraphState.Done, row.State);
                Assert.Equal(new GraphCode("code-two"), row.Code);
            });

        Assert.Single(commands.Reads);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>CTE-P21/N06 require the recursive frontier's NULL facts to include later iterations, not just its anchor.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Nullable_frontier_condition_observes_values_created_by_the_previous_iteration(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);
        var query = context
            .Vertices
            .Where(row => row.Id == 1)
            .Select(row => (int?)row.Id)
            .WithRecursiveCte(
                frontier => frontier.Select(value => value == null
                    ? (int?)1
                    : null),
                rows => rows,
                2);

        // Act
        var actual = await query.ToArrayAsync(safety.Token);

        // Assert
        // WHY: Native recursion does not promise outer row order. The exact bag distinguishes
        // a real NULL-to-one transition from an IS NULL predicate incorrectly folded using anchor-only facts.
        Assert.Equal([null, 1, 1], actual.OrderBy(value => value));
        Assert.Single(commands.Reads);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>The recursive NULL-fact correction does not change the known nonnull values of an ordinary anchor.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Ordinary_nullable_clr_projection_keeps_its_actual_nonnull_anchor_contract(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();

        // Act
        var actual = await context
            .Vertices
            .Where(row => row.Id == 1)
            .Select(row => (int?)row.Id)
            .WithCte(rows => rows.Select(value => value == null
                ? 1
                : 0))
            .SingleAsync(safety.Token);

        // Assert
        Assert.Equal(0, actual);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>CTE-P10 retains field, shadow and discriminator projections rather than rebuilding an entity from its key.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Field_shadow_and_tph_values_remain_attached_to_the_exact_source_mapping(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var actual = await context
            .Vertices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .WithCte(rows => rows
                .OfType<SpecialVertex>()
                .OrderBy(row => row.Id)
                .Select(row => new
                {
                    row.Id,
                    row.Tag,
                    Note = EF.Property<string>(row, "_note"),
                    Shadow = EF.Property<int>(row, "ShadowNumber"),
                }))
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([(3, "derived-three", "field-3", 103), (4, "derived-four", "field-4", 104)],
            actual.Select(row => (row.Id, row.Tag, row.Note, row.Shadow)));

        Assert.Single(commands.Reads);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>CTE-P10 distinguishes tracked identity, independent no-tracking objects and temporary identity resolution.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TrackingCases))]
    public async Task Entity_identity_follows_the_requested_tracking_policy_after_enumeration(
        string target,
        string mode
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var source = context.Vertices.Where(row => row.Id == 1);
        source = mode switch
        {
            "tracking" => source.AsTracking(),
            "no-tracking" => source.AsNoTracking(),
            "identity-resolution" => source.AsNoTrackingWithIdentityResolution(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        // Act
        var actual = await source
            .WithCte(rows => rows.Concat(rows))
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 1], actual.Select(row => row.Id));
        Assert.Equal(mode != "no-tracking", ReferenceEquals(actual[0], actual[1]));
        Assert.Equal(mode == "tracking"
                ? 1
                : 0,
            context
                .ChangeTracker
                .Entries()
                .Count());
    }
}
