namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Exercises correlation and failure boundaries independently of SQL result ordering.
/// </summary>
public sealed partial class MySqlGeneratedValueReadbackTests
{
    /// <summary>
    /// Maps shuffled rows to their original commands rather than their arrival order.
    /// </summary>
    /// <param name="asynchronous">Whether to consume results through the asynchronous reader path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShuffledSlotsPreserveCommandIdentity(
        bool asynchronous
    )
    {
        // Arrange
        using var scenario = Scenario.Create(
            [3],
            [new ResultRows([[303, 2], [101, 0], [202, 1]])]);

        // Act
        await scenario.ConsumeAsync(asynchronous, CancellationToken.None);

        var actual = scenario.Commands.Select(command => command.GeneratedValue).ToArray();

        // Assert
        Assert.Equal([101, 202, 303], actual);
        Assert.All(scenario.Commands, command => Assert.Equal(1, command.PropagationCount));
        Assert.Equal(4, scenario.Reader.ReadCount);
    }

    /// <summary>
    /// Correlates every slot across bitmap-word boundaries and the provider's maximum group size.
    /// </summary>
    /// <param name="asynchronous">Whether to consume results through the asynchronous reader path.</param>
    /// <param name="count">The number of commands or rows used to exercise the boundary.</param>
    [Theory]
    [InlineData(false, 65)]
    [InlineData(true, 65)]
    [InlineData(false, 1000)]
    [InlineData(true, 1000)]
    public async Task WideShuffledGroupsPreserveEverySlot(
        bool asynchronous,
        int count
    )
    {
        // Arrange
        var rows = Enumerable.Range(0, count)
            .Reverse()
            .Select(slot => new object?[] { slot + 100, slot })
            .ToArray();

        using var scenario = Scenario.Create([count], [new ResultRows(rows)]);

        // Act
        await scenario.ConsumeAsync(asynchronous, CancellationToken.None);

        var actual = scenario.Commands.Select(command => command.GeneratedValue).ToArray();

        // Assert
        Assert.Equal(
            Enumerable.Range(100, count).Cast<object>(),
            actual);
        Assert.All(scenario.Commands, command => Assert.Equal(1, command.PropagationCount));
        Assert.Equal(count + 1, scenario.Reader.ReadCount);
    }

    /// <summary>
    /// Rejects duplicate slots in additional bitmap words, including the last supported slot.
    /// </summary>
    /// <param name="asynchronous">Whether to consume results through the asynchronous reader path.</param>
    /// <param name="count">The number of commands or rows used to exercise the boundary.</param>
    /// <param name="repeatedSlot">The command slot duplicated in the result set.</param>
    [Theory]
    [InlineData(false, 65, 64)]
    [InlineData(true, 65, 64)]
    [InlineData(false, 1000, 999)]
    [InlineData(true, 1000, 999)]
    public async Task WideGroupsRejectRepeatedBoundarySlots(
        bool asynchronous,
        int count,
        int repeatedSlot
    )
    {
        // Arrange
        var rows = Enumerable.Range(0, count)
            .Reverse()
            .Select(slot => new object?[] { slot + 100, slot })
            .ToArray();

        rows[^1][1] = repeatedSlot;
        using var scenario = Scenario.Create([count], [new ResultRows(rows)]);

        // Act
        var exception = await Record.ExceptionAsync(() => scenario.ConsumeAsync(asynchronous, CancellationToken.None));

        // Assert
        var updateException = Assert.IsType<DbUpdateException>(exception);
        Assert.IsType<InvalidOperationException>(updateException.InnerException);
        Assert.Contains("more than once", updateException.InnerException.Message, StringComparison.Ordinal);
        Assert.Equal(1, scenario.Commands[repeatedSlot].PropagationCount);
    }

    /// <summary>
    /// Accepts an integral provider ordinal representation without boxing generated output.
    /// </summary>
    /// <param name="asynchronous">Whether to consume results through the asynchronous reader path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignedWideOrdinalRemainsRangeChecked(
        bool asynchronous
    )
    {
        // Arrange
        using var scenario = Scenario.Create([2], [new ResultRows([[202, 1L], [101, 0L]], typeof(long))]);

        // Act
        await scenario.ConsumeAsync(asynchronous, CancellationToken.None);

        var actual = scenario.Commands.Select(command => command.GeneratedValue).ToArray();

        // Assert
        Assert.Equal([101, 202], actual);
        Assert.All(scenario.Commands, command => Assert.Equal(1, command.PropagationCount));
    }

    /// <summary>
    /// Keeps relative slots inside their own result set after an ordinary prefix command.
    /// </summary>
    /// <param name="asynchronous">Whether to consume results through the asynchronous reader path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GroupOffsetsDoNotLeakIntoAdjacentGroups(
        bool asynchronous
    )
    {
        // Arrange
        using var scenario = Scenario.Create(
            [2, 2],
            [new ResultRows([[102, 1], [101, 0]]), new ResultRows([[202, 1], [201, 0]])],
            prefixCommands: 1);

        // Act
        await scenario.ConsumeAsync(asynchronous, CancellationToken.None);

        var actual = scenario.Commands.Select(command => command.GeneratedValue).ToArray();

        // Assert
        Assert.Equal([null, 101, 102, 201, 202], actual);
        Assert.Equal(0, scenario.Commands[0].PropagationCount);
        Assert.All(scenario.Commands.Skip(1), command => Assert.Equal(1, command.PropagationCount));
    }

    /// <summary>
    /// Leaves ordinary sequential result sets on EF's existing consumption path.
    /// </summary>
    /// <param name="asynchronous">Whether to consume results through the asynchronous reader path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryResultsDoNotRequireOrdinalColumns(
        bool asynchronous
    )
    {
        // Arrange
        using var scenario = Scenario.Create(
            [2],
            [new ResultRows([[101], [202]], ordinalType: null)],
            positional: false);

        // Act
        await scenario.ConsumeAsync(asynchronous, CancellationToken.None);

        var actual = scenario.Commands.Select(command => command.GeneratedValue).ToArray();

        // Assert
        Assert.Equal([101, 202], actual);
        Assert.All(scenario.Commands, command => Assert.Equal(1, command.PropagationCount));
    }

    /// <summary>
    /// Rejects every malformed result shape instead of silently accepting partial output.
    /// </summary>
    /// <param name="asynchronous">Whether to consume results through the asynchronous reader path.</param>
    /// <param name="failure">The malformed result or unsafe command shape.</param>
    [Theory]
    [MemberData(nameof(MalformedResults))]
    public async Task MalformedPositionalResultsFailClosed(
        bool asynchronous,
        string failure
    )
    {
        // Arrange
        using var scenario = Scenario.Create([3], [MalformedRows(failure)]);

        // Act
        var exception = await Record.ExceptionAsync(() => scenario.ConsumeAsync(asynchronous, CancellationToken.None));

        // Assert
        var updateException = Assert.IsType<DbUpdateException>(exception);
        Assert.IsNotType<DbUpdateConcurrencyException>(updateException);
        Assert.IsType<InvalidOperationException>(updateException.InnerException);
        Assert.Contains("generated-value", updateException.InnerException.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Attributes a malformed row to its entire command group while excluding preceding commands.
    /// </summary>
    /// <param name="asynchronous">Whether to consume results through the asynchronous reader path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidResultReportsEveryAffectedGroupEntry(
        bool asynchronous
    )
    {
        // Arrange
        using var scenario = Scenario.Create(
            [3],
            [new ResultRows([[101, 0], [202, 0], [303, 2]])],
            prefixCommands: 1,
            trackEntries: true);

        var expected = scenario.Commands.Skip(1)
            .SelectMany(command => command.Entries)
            .Select(entry => entry.ToEntityEntry().Entity)
            .ToArray();

        // Act
        var exception = await Record.ExceptionAsync(() => scenario.ConsumeAsync(asynchronous, CancellationToken.None));

        // Assert
        var updateException = Assert.IsType<DbUpdateException>(exception);
        Assert.Equal(expected, updateException.Entries.Select(entry => entry.Entity));
        Assert.DoesNotContain(
            scenario.Commands[0].Entries[0].ToEntityEntry().Entity,
            updateException.Entries.Select(entry => entry.Entity));
    }

    /// <summary>
    /// Reports the whole affected group when its entire result set is missing after another completed group.
    /// </summary>
    /// <param name="asynchronous">Whether to consume results through the asynchronous reader path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingResultSetReportsOnlyItsAffectedGroupEntries(
        bool asynchronous
    )
    {
        // Arrange
        using var scenario = Scenario.Create(
            [2, 3],
            [new ResultRows([[101, 0], [202, 1]])],
            trackEntries: true);

        var expected = scenario.Commands.Skip(2)
            .SelectMany(command => command.Entries)
            .Select(entry => entry.ToEntityEntry().Entity)
            .ToArray();

        // Act
        var exception = await Record.ExceptionAsync(() => scenario.ConsumeAsync(asynchronous, CancellationToken.None));

        // Assert
        var updateException = Assert.IsType<DbUpdateException>(exception);
        Assert.IsType<InvalidOperationException>(updateException.InnerException);
        Assert.Equal(expected, updateException.Entries.Select(entry => entry.Entity));
        Assert.All(scenario.Commands.Take(2), command => Assert.Equal(1, command.PropagationCount));
        Assert.All(scenario.Commands.Skip(2), command => Assert.Equal(0, command.PropagationCount));
    }

    /// <summary>
    /// Lists a shared table entry once even when it participates in multiple commands in the rejected group.
    /// </summary>
    /// <param name="asynchronous">Whether to consume results through the asynchronous reader path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidResultDeduplicatesSharedEntries(
        bool asynchronous
    )
    {
        // Arrange
        using var scenario = Scenario.Create(
            [3],
            [new ResultRows([[101, 0], [202, 0], [303, 2]])],
            trackEntries: true,
            shareEntry: true);

        // Act
        var exception = await Record.ExceptionAsync(() => scenario.ConsumeAsync(asynchronous, CancellationToken.None));

        // Assert
        var updateException = Assert.IsType<DbUpdateException>(exception);
        Assert.Single(updateException.Entries);
        Assert.Same(scenario.Commands[0].Entries[0].ToEntityEntry().Entity, updateException.Entries[0].Entity);
    }

    /// <summary>
    /// Treats missing generated output as a broken readback contract even when concurrency errors are suppressed.
    /// </summary>
    /// <param name="asynchronous">Whether to consume results through the asynchronous reader path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOutputCannotBeSuppressedAsConcurrency(
        bool asynchronous
    )
    {
        // Arrange
        var interceptor = new ConcurrencySuppressor();
        using var scenario = Scenario.Create([3], [new ResultRows([[101, 0], [303, 2]])], interceptor: interceptor);

        // Act
        var exception = await Record.ExceptionAsync(() => scenario.ConsumeAsync(asynchronous, CancellationToken.None));

        // Assert
        var updateException = Assert.IsType<DbUpdateException>(exception);
        Assert.IsType<InvalidOperationException>(updateException.InnerException);
        Assert.Equal(0, interceptor.Calls);
    }

    /// <summary>
    /// Cancels before the first row, between rows, and during the mandatory final EOF read.
    /// </summary>
    /// <param name="cancelOnRead">The read at which cancellation is requested.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task CancellationReachesEveryReadIncludingFinalEof(
        int cancelOnRead
    )
    {
        // Arrange
        using var stop = new CancellationTokenSource();
        using var scenario = Scenario.Create([3], [new ResultRows([[101, 0], [202, 1], [303, 2]])]);
        scenario.Reader.BeforeRead = count =>
        {
            if (count == cancelOnRead)
            {
                stop.Cancel();
            }
        };

        // Act
        var exception = await Record.ExceptionAsync(() => scenario.ConsumeAsync(true, stop.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancelOnRead, scenario.Reader.ReadCount);
        Assert.All(scenario.Reader.ObservedTokens, token => Assert.Equal(stop.Token, token));
    }

    /// <summary>
    /// Preserves nullable generated values without confusing them with a missing slot.
    /// </summary>
    /// <param name="asynchronous">Whether to consume results through the asynchronous reader path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedNullValueStillBelongsToItsSlot(
        bool asynchronous
    )
    {
        // Arrange
        using var scenario = Scenario.Create([2], [new ResultRows([[null, 1], [101, 0]])]);

        // Act
        await scenario.ConsumeAsync(asynchronous, CancellationToken.None);

        var actual = scenario.Commands.Select(command => command.GeneratedValue).ToArray();

        // Assert
        Assert.Equal([101, null], actual);
        Assert.All(scenario.Commands, command => Assert.Equal(1, command.PropagationCount));
    }

    /// <summary>
    /// Supplies each malformed contract in both consumption modes.
    /// </summary>
    public static IEnumerable<object[]> MalformedResults()
    {
        string[] failures =
        [
            "duplicate", "negative", "outside", "null", "string", "fractional", "overflow",
            "missing-all", "missing-first", "missing-middle", "missing-last", "extra", "missing-column", "extra-column",
        ];

        foreach (var failure in failures)
        {
            yield return [false, failure];
            yield return [true, failure];
        }
    }

    private static ResultRows MalformedRows(
        string failure
    ) => failure switch
    {
        "duplicate" => new ResultRows([[101, 0], [202, 0], [303, 2]]),
        "negative" => new ResultRows([[101, -1], [202, 1], [303, 2]]),
        "outside" => new ResultRows([[101, 3], [202, 1], [303, 2]]),
        "null" => new ResultRows([[101, null], [202, 1], [303, 2]]),
        "string" => new ResultRows([[101, "0"], [202, "1"], [303, "2"]], typeof(string)),
        "fractional" => new ResultRows([[101, 0.5], [202, 1.0], [303, 2.0]], typeof(double)),
        "overflow" => new ResultRows([[101, (long)int.MaxValue + 1], [202, 1L], [303, 2L]], typeof(long)),
        "missing-all" => new ResultRows([]),
        "missing-first" => new ResultRows([[202, 1], [303, 2]]),
        "missing-middle" => new ResultRows([[101, 0], [303, 2]]),
        "missing-last" => new ResultRows([[101, 0], [202, 1]]),
        "extra" => new ResultRows([[101, 0], [202, 1], [303, 2], [404, 0]]),
        "missing-column" => new ResultRows([[101], [202], [303]], ordinalType: null),
        "extra-column" => new ResultRows([[101, 17, 0], [202, 17, 1], [303, 17, 2]], fieldCount: 3),
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };
}
