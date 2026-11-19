namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlWindowFunctionIntegrationTests
{
    /// <summary>Concurrent compilations use independent contexts while sharing only immutable provider services.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Parallel_window_compilations_keep_current_parameters_and_contexts_separate(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var thresholds = new[]
        {
            0,
            1,
            2,
            3,
            0,
            1,
            2,
            3,
        };

        // Act
        var results = await Task.WhenAll(thresholds.Select(async threshold =>
        {
            await using var context = store.CreateContext();

            return await context
                .Rows
                .Where(row => row.Id > threshold)
                .OrderBy(row => row.Id)
                .Select(row => new
                {
                    row.Id,
                    Position = EF
                        .Functions
                        .Window()
                        .OrderBy(row.Id)
                        .RowNumber(),
                })
                .ToArrayAsync(safety.Token);
        }));

        // Assert
        for (var index = 0; index < thresholds.Length; index++)
        {
            Assert.Equal(Enumerable.Range(thresholds[index] + 1, 6 - thresholds[index]),
                results[index]
                    .Select(row => row.Id));

            Assert.Equal(Enumerable
                    .Range(1, 6 - thresholds[index])
                    .Select(value => (long)value),
                results[index]
                    .Select(row => row.Position));
        }
    }

    /// <summary>A context-pool lease cannot retain a prior invocation's parameter values or tracked entities.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Reused_context_pool_keeps_window_parameters_current_and_ordinary_sql_unchanged(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var factory = new PooledDbContextFactory<WindowContext>(store.CreateOptions(null, true), poolSize: 1);
        WindowContext first;
        await using (first = await factory.CreateDbContextAsync(safety.Token))
        {
            // WHY: The next lease must exercise a real previously used context, not a new context with an empty cache.
            _ = await first
                .Rows
                .Where(row => row.Id > 3)
                .Select(row => new
                {
                    Entity = row,
                    Position = EF
                        .Functions
                        .Window()
                        .OrderBy(row.Id)
                        .RowNumber(),
                    Sum = EF
                        .Functions
                        .Window()
                        .OrderBy(row.Id)
                        .RowsBetween(MySqlWindowFrameBound.Preceding(1), MySqlWindowFrameBound.CurrentRow)
                        .Sum(row.Amount),
                })
                .ToArrayAsync(safety.Token);
        }

        await using var context = await factory.CreateDbContextAsync(safety.Token);
        var threshold = 1;
        var ordinary = context
            .Rows
            .Where(row => row.Id > threshold)
            .Select(row => row.Id)
            .ToQueryString();

        // Act
        var rows = await context
            .Rows
            .Where(row => row.Id > threshold)
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                Position = EF
                    .Functions
                    .Window()
                    .OrderBy(row.Id)
                    .RowNumber(),
                Sum = EF
                    .Functions
                    .Window()
                    .OrderBy(row.Id)
                    .RowsBetween(MySqlWindowFrameBound.Preceding(2), MySqlWindowFrameBound.CurrentRow)
                    .Sum(row.Amount),
            })
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Same(first, context);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal([2, 3, 4, 5, 6], rows.Select(row => row.Id));
        Assert.Equal([1L, 2L, 3L, 4L, 5L], rows.Select(row => row.Position));
        Assert.Equal([20m, 50m, 55m, 50m, 20m], rows.Select(row => row.Sum));
        Assert.DoesNotContain("OVER", ordinary, StringComparison.Ordinal);
    }

    /// <summary>Disposing a partially consumed window reader returns the connection for the next pooled context lease.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Early_window_reader_disposal_allows_the_next_pooled_query(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var factory = new PooledDbContextFactory<WindowContext>(store.CreateOptions(null, true), poolSize: 1);
        WindowContext first;
        bool moved;
        long firstValue;
        await using (first = await factory.CreateDbContextAsync(safety.Token))
        {
            var query = first
                .Rows
                .OrderBy(row => row.Id)
                .Select(row => EF
                    .Functions
                    .Window()
                    .OrderBy(row.Id)
                    .RowNumber())
                .AsAsyncEnumerable();

            await using var reader = query.GetAsyncEnumerator(safety.Token);

            // WHY: Leave the native reader intentionally unfinished before disposal and the next context lease.
            moved = await reader.MoveNextAsync();
            firstValue = reader.Current;
        }

        await using var context = await factory.CreateDbContextAsync(safety.Token);

        // Act
        var rows = await context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.True(moved);
        Assert.Equal(1L, firstValue);
        Assert.Same(first, context);
        Assert.Equal([1, 2, 3, 4, 5, 6], rows);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Cancellation triggered at the actual reader boundary remains visible and leaves the next lease usable.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Cancelled_window_reader_preserves_the_token_and_allows_pool_recovery(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(safety.Token);
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var observer = new CancellingWindowReader(cancellation);
        var factory = new PooledDbContextFactory<WindowContext>(store.CreateOptions(observer, true), poolSize: 1);
        OperationCanceledException? cancelled;
        WindowContext first;
        await using (first = await factory.CreateDbContextAsync(safety.Token))
        {
            // WHY: Cancellation is injected at a deterministic boundary, without waiting for server speed or elapsed time.
            cancelled = await Record.ExceptionAsync(async () => await first
                .Rows
                .Select(row => EF
                    .Functions
                    .Window()
                    .OrderBy(row.Id)
                    .RowNumber())
                .ToArrayAsync(cancellation.Token)) as OperationCanceledException;
        }

        observer.CancelNext = false;
        await using var context = await factory.CreateDbContextAsync(safety.Token);

        // Act
        var rows = await context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.NotNull(cancelled);
        Assert.Same(first, context);
        Assert.Equal(cancellation.Token, observer.CancelledToken);
        Assert.Equal([1, 2, 3, 4, 5, 6], rows);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Different actual models sharing one service graph retain their own filters and window input rows.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Different_models_sharing_services_keep_their_window_query_cache_shapes_separate(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var first = store.CreateContext();
        await using var second = new AlternateWindowContext(store.CreateOptions(null, false));
        var threshold = 1;
        var firstQuery = first
            .Rows
            .Where(row => row.Id > threshold)
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                Position = EF
                    .Functions
                    .Window()
                    .OrderBy(row.Id)
                    .RowNumber(),
            });

        var secondQuery = second
            .Rows
            .Where(row => row.Id > threshold)
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                Position = EF
                    .Functions
                    .Window()
                    .OrderBy(row.Id)
                    .RowNumber(),
            });

        // Act
        var results = await Task.WhenAll(firstQuery.ToArrayAsync(safety.Token), secondQuery.ToArrayAsync(safety.Token));

        // Assert
        Assert.NotSame(first.Model, second.Model);
        Assert.Equal([2, 3, 4, 5, 6],
            results[0]
                .Select(row => row.Id));

        Assert.Equal([1L, 2L, 3L, 4L, 5L],
            results[0]
                .Select(row => row.Position));

        Assert.Equal([4, 5, 6],
            results[1]
                .Select(row => row.Id));

        Assert.Equal([1L, 2L, 3L],
            results[1]
                .Select(row => row.Position));
    }

    /// <summary>An actual native command error remains visible and does not poison the next pooled lease.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Native_window_command_failure_remains_visible_and_allows_pool_recovery(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var factory = new PooledDbContextFactory<WindowContext>(store.CreateOptions(null, true), poolSize: 1);
        Exception? failed;
        WindowContext first;
        await using (first = await factory.CreateDbContextAsync(safety.Token))
        {
            var query = first.Rows.Select(row => EF
                .Functions
                .Window()
                .OrderBy(row.Id)
                .RowNumber());

            // WHY: A previously executed cached command must fail natively, then recover
            // after the fixture restores its table; no production retry or SQL replacement is used.
            _ = await query.ToArrayAsync(safety.Token);
            await first.Database.ExecuteSqlRawAsync("RENAME TABLE `Window rows` TO `Window unavailable`;",
                safety.Token);

            failed = await Record.ExceptionAsync(async () => await query.ToArrayAsync(safety.Token));
            await first.Database.ExecuteSqlRawAsync("RENAME TABLE `Window unavailable` TO `Window rows`;",
                safety.Token);
        }

        await using var context = await factory.CreateDbContextAsync(safety.Token);

        // Act
        var rows = await context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => EF
                .Functions
                .Window()
                .OrderBy(row.Id)
                .RowNumber())
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(1146,
            Assert.IsType<MySqlException>(failed)
                .Number);

        Assert.Same(first, context);
        Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L], rows);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Cancels exactly one observed native reader without replacing SQL or suppressing database exceptions.</summary>
    private sealed class CancellingWindowReader : DbCommandInterceptor
    {
        private readonly CancellationTokenSource _cancellation;

        /// <summary>Initializes the reader with the configured values and observation callbacks.</summary>
        public CancellingWindowReader(
            CancellationTokenSource cancellation
        )
        {
            _cancellation = cancellation;
        }

        internal bool CancelNext { get; set; } = true;
        internal CancellationToken CancelledToken { get; private set; }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (CancelNext)
            {
                CancelledToken = cancellationToken;
                _cancellation.Cancel();
            }

            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Uses a different context/model identity while preserving the same mapped source and shared services.</summary>
    private sealed class AlternateWindowContext : WindowContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public AlternateWindowContext(
            DbContextOptions<WindowContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<WindowRow>()
                .HasQueryFilter(row => row.SourceVisible && row.Id > 3);
        }
    }
}
