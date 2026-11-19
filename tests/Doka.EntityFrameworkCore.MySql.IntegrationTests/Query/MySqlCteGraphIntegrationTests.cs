namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Qualifies typed CTE graph, mapping, lineage and lifecycle contracts with independent native fixtures.</summary>
[IntegrationDatabaseTest]
[Trait("VerificationLane", "FullIntegration")]
public sealed partial class MySqlCteGraphIntegrationTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Uses the current test's runner-owned diagnostic output.</summary>
    /// <param name="output">The output helper associated with this test case.</param>
    public MySqlCteGraphIntegrationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Reports every selected supported pinned engine independently.</summary>
    public static TheoryData<string> SelectedTargets => new(IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => IntegrationTestEnvironment
            .GetSupportedTargets()
            .Contains(target))
        .Select(target => target.ToString()));

    /// <summary>Bounds hung work; no success assertion depends on elapsed execution time.</summary>
    private static CancellationTokenSource CreateSafetyCancellation() => new(TimeSpan.FromMinutes(3));

    /// <summary>Materializes an explicit compiled stream using its caller's required token.</summary>
    private static async Task<T[]> ReadCompiledAsync<T>(
        IAsyncEnumerable<T> query,
        CancellationToken cancellationToken
    )
    {
        var values = new List<T>();

        await foreach (var value in query
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            values.Add(value);
        }

        return values.ToArray();
    }

    /// <summary>Captures actual reader boundaries without replacing commands or suppressing native execution.</summary>
    private sealed class GraphCommands : DbCommandInterceptor
    {
        /// <summary>Gets commands and tokens in observed execution order.</summary>
        public ConcurrentQueue<(string Sql, CancellationToken Token)> Reads { get; } = new();

        /// <summary>Gets actual native nonquery command boundaries independently of reader observations.</summary>
        public ConcurrentQueue<(string Sql, CancellationToken Token)> Writes { get; } = new();

        /// <summary>Gets a test-owned failure injected only after a native UPDATE has returned.</summary>
        public Exception? FailureAfterWrite { get; init; }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Reads.Enqueue((command.CommandText, cancellationToken));

            return ValueTask.FromResult(result);
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Writes.Enqueue((command.CommandText, cancellationToken));

            return ValueTask.FromResult(result);
        }

        /// <inheritdoc />
        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (FailureAfterWrite is { } failure
                && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                throw failure;
            }

            return ValueTask.FromResult(result);
        }
    }
}
