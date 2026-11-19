namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlWindowFunctionIntegrationTests
{
    private const int WideRowCount = 14000;

    private static readonly string[] s_wideShadowProperties = Enumerable
        .Range(0, 39)
        .Select(index => "Payload" + index.ToString(CultureInfo.InvariantCulture))
        .ToArray();

    /// <summary>Supplies large, many-small and skewed native partitions as independent full-materialization cases.</summary>
    public static TheoryData<string, string> WideTargets
    {
        get
        {
            var cases = new TheoryData<string, string>();

            foreach (var target in SelectedTargets)
            {
                cases.Add(target, "single");
                cases.Add(target, "many-small");
                cases.Add(target, "skewed");
            }

            return cases;
        }
    }

    /// <summary>Fifty-column native sources preserve every value and report ungated session work for each partition shape.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(WideTargets))]
    public async Task Fifty_column_partition_distributions_materialize_complete_results(
        string target,
        string distribution
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await store.SeedWideRowsAsync(distribution, safety.Token);
        var commands = new WindowParameterCommands();
        await using var context = new WindowContext(store.CreateOptions(commands, false));
        var sql = BuildWideQuery(context)
            .ToQueryString();

        await context.Database.OpenConnectionAsync(safety.Token);
        var connection = (MySqlConnection)context.Database.GetDbConnection();
        var originalThread = connection.ServerThread;
        var control = await ReadWideSessionCountersAsync(connection, safety.Token);
        var before = await ReadWideSessionCountersAsync(connection, safety.Token);

        // Act
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var rows = await ExecuteWideQueryAsync(context, safety.Token);
        var processAllocationDelta = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var after = await ReadWideSessionCountersAsync(connection, safety.Token);
        var retainedThread = connection.ServerThread;
        var plan = await store.ExplainAsync(sql, safety.Token);
        await context.Database.CloseConnectionAsync();

        // Assert
        AssertWideRows(context, rows, distribution);
        Assert.Equal(originalThread, retainedThread);
        Assert.Equal(1, commands.Readers);
        Assert.False(string.IsNullOrWhiteSpace(plan));
        Assert.Equal(ConnectionState.Closed, connection.State);
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            target,
            distribution,
            rows = rows.Count,
            physicalFields = 50,
            dataReaderCommands = commands.Readers,
            originalThread,
            retainedThread,
            statusInspectionControl = before.Since(control),
            dataAndStatusInspection = after.Since(before),
            processAllocationDelta,
            allocationScope =
                "Process-wide diagnostic around complete execution/materialization; includes EF, connector, result/tracking and concurrent work. Not provider-only bytes or a gate.",
            nativeWorkScope =
                "Same-session deltas include one status inspection; adjacent inspections provide its observed control. Counters do not describe all engine temp storage.",
            nativePlan = plan,
        }));
    }

    /// <summary>Builds the same typed fifty-column query for correctness and separately bounded allocation diagnostics.</summary>
    private static IQueryable<WindowWideResult> BuildWideQuery(
        WindowContext context
    ) => context
        .Rows
        .Select(row => new WindowWideResult
        {
            Entity = row,
            Position = EF
                .Functions
                .Window()
                .PartitionBy(row.Group)
                .OrderBy(row.Id)
                .RowNumber(),
            Running = EF
                .Functions
                .Window()
                .PartitionBy(row.Group)
                .OrderBy(row.Id)
                .Sum(row.Amount),
            Count = EF
                .Functions
                .Window()
                .PartitionBy(row.Group)
                .LongCount(),
        })
        .OrderBy(row => row.Entity.Id);

    /// <summary>Executes complete tracked materialization without fixture seeding, assertions, status queries or serialization.</summary>
    private static Task<List<WindowWideResult>> ExecuteWideQueryAsync(
        WindowContext context,
        CancellationToken cancellationToken
    ) => BuildWideQuery(context)
        .ToListAsync(cancellationToken);

    /// <summary>Checks every mapped field and window result outside any allocation-measurement window.</summary>
    private static void AssertWideRows(
        WindowContext context,
        List<WindowWideResult> rows,
        string distribution
    )
    {
        Assert.Equal(WideRowCount, rows.Count);
        Assert.Equal(50,
            context.Model.FindEntityType(typeof(WindowRow))!
                .GetProperties()
                .Count());

        Assert.Equal(ExpectedWideRows(distribution),
            rows.Select(row =>
                (row.Entity.Id, row.Entity.Group, row.Position, row.Running, row.Count)));

        Assert.Equal(WideRowCount,
            context
                .ChangeTracker
                .Entries()
                .Count());

        Assert.All(rows,
            row =>
            {
                var entity = row.Entity;
                Assert.True(entity.SourceVisible);
                Assert.Equal(entity.Id, entity.Sort);
                Assert.Equal(entity.Id % 17, entity.Amount);
                Assert.Equal(distribution == "skewed" && entity.Id > 13900
                        ? WindowGroup.Second
                        : WindowGroup.First,
                    entity.ConvertedGroup);

                var expectedName = entity.Id switch
                {
                    1 => "alpha",
                    2 => null,
                    3 => "gamma",
                    4 => "delta",
                    5 => "epsilon",
                    6 => "zeta",
                    _ => "wide",
                };

                Guid? expectedGuid = entity.Id <= 6 && entity.Id != 2
                    ? Guid.Parse("00112233-4455-6677-8899-" + entity.Id.ToString("D12", CultureInfo.InvariantCulture))
                    : null;

                DateTime? expectedTimestamp = entity.Id <= 6
                    ? new DateTime(2026,
                        1,
                        1,
                        0,
                        0,
                        0,
                        DateTimeKind.Unspecified).AddTicks(entity.Id * 10L)
                    : null;

                Assert.Equal(expectedName, entity.Name);
                Assert.Equal(expectedGuid, entity.CharGuid);
                Assert.Equal(expectedGuid, entity.BinaryGuid);
                Assert.Equal(expectedGuid, entity.NativeGuid);
                Assert.Equal(expectedTimestamp, entity.Timestamp);
                var entry = context.Entry(entity);

                for (var index = 0; index < s_wideShadowProperties.Length; index++)
                {
                    Assert.Equal(entity.Id + index,
                        entry.Property<int>(s_wideShadowProperties[index])
                            .CurrentValue);
                }
            });
    }

    /// <summary>Computes complete independent values in one pass rather than a quadratic prefix sum.</summary>
    private static IEnumerable<(int Id, int Group, long Position, decimal? Running, long Count)> ExpectedWideRows(
        string distribution
    )
    {
        decimal running = 0;
        var previousGroup = 0;

        for (var id = 1; id <= WideRowCount; id++)
        {
            var partition = WidePartition(id, distribution);

            if (partition.Group != previousGroup)
            {
                previousGroup = partition.Group;
                running = 0;
            }

            running += id % 17;

            yield return (id, partition.Group, id - partition.Start + 1L, running, partition.Count);
        }
    }

    /// <summary>Defines the three fixed workload shapes without changing the measured query or column model.</summary>
    private static (int Group, int Start, long Count) WidePartition(
        int id,
        string distribution
    ) => distribution switch
    {
        "single" => (1, 1, WideRowCount),
        "many-small" => ((id + 1) / 2, (id - 1) / 2 * 2 + 1, 2),
        "skewed" => id <= 13900
            ? (1, 1, 13900)
            : (2, 13901, 100),
        _ => throw new ArgumentOutOfRangeException(nameof(distribution)),
    };

    /// <summary>Retains the real entity, all shadow state and typed native calculations without a duplicate fifty-property model.</summary>
    private sealed class WindowWideResult
    {
        public required WindowRow Entity { get; init; }
        public long Position { get; init; }
        public decimal? Running { get; init; }
        public long Count { get; init; }
    }
}
