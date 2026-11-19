namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>CTE-N23 retries a new native statement after a discarded first reader instead of retaining its partial bound data.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Controlled_read_retry_discards_the_first_native_row_and_reexecutes_the_complete_cte(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        await using var services = new ServiceCollection()
            .AddEntityFrameworkDokaMySql()
            .BuildServiceProvider();

        var commands = new GraphCommands();
        var retry = new CteFirstReaderFailure(new IOException("Fixture-owned retryable read transport failure."));

        // WHY: Retrying options have their own immutable provider snapshot; sharing the
        // ordinary fixture provider would fail before this intended reader/retry boundary.
        var options = IntegrationTestDbContextOptions
            .Create<GraphContext>()
            .UseMySql(store.ConnectionString,
                store.Version,
                provider => provider.EnableRetryOnFailure(1, TimeSpan.FromMilliseconds(1)))
            .UseInternalServiceProvider(services)
            .AddInterceptors(commands, retry)
            .Options;

        await using var context = new GraphContext(options);
        var query = context
            .Vertices
            .Where(row => row.Id <= 2)
            .Select(row => row.Id)
            .WithCte(rows => rows
                .Concat(rows)
                .OrderBy(id => id));

        // Act
        var actual = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 1, 2, 2], actual);
        Assert.Equal(1, retry.FirstValue);
        Assert.Equal(2, retry.NativeReaders);
        Assert.Equal(2, commands.Reads.Count);
        Assert.All(commands.Reads, command => Assert.Equal(safety.Token, command.Token));
        Assert.Empty(commands.Writes);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A command timeout remains terminal even when retrying read options are enabled, and the next explicit enumeration is healthy.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Terminal_timeout_after_a_native_cte_row_is_not_implicitly_retried(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        await using var services = new ServiceCollection()
            .AddEntityFrameworkDokaMySql()
            .BuildServiceProvider();

        var commands = new GraphCommands();
        var expected = new TimeoutException("Fixture-owned terminal timeout after the first CTE row.");
        var retry = new CteFirstReaderFailure(expected);
        var options = IntegrationTestDbContextOptions
            .Create<GraphContext>()
            .UseMySql(store.ConnectionString,
                store.Version,
                provider => provider.EnableRetryOnFailure(1, TimeSpan.FromMilliseconds(1)))
            .UseInternalServiceProvider(services)
            .AddInterceptors(commands, retry)
            .Options;

        await using var context = new GraphContext(options);
        var query = context
            .Vertices
            .Where(row => row.Id <= 2)
            .Select(row => row.Id)
            .WithCte(rows => rows
                .Concat(rows)
                .OrderBy(id => id));

        // Act
        var failure = await Record.ExceptionAsync(() => query.ToArrayAsync(safety.Token));
        var attemptsBeforeExplicitReuse = retry.NativeReaders;
        var after = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Same(expected, failure);
        Assert.Equal(1, attemptsBeforeExplicitReuse);
        Assert.Equal([1, 1, 2, 2], after);
        Assert.Equal(2, retry.NativeReaders);
        Assert.Equal(2, commands.Reads.Count);
        Assert.Empty(commands.Writes);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Injects the selected failure after the first actual native row; the production classifier decides whether retry is legal.</summary>
    private sealed class CteFirstReaderFailure : DbCommandInterceptor
    {
        private readonly Exception _failure;

        /// <summary>Initializes the test helper with its supplied dependencies and values.</summary>
        public CteFirstReaderFailure(
            Exception failure
        )
        {
            _failure = failure;
        }

        /// <summary>Gets the actual reader-completion count, independently of query construction.</summary>
        public int NativeReaders { get; private set; }

        /// <summary>Gets the discarded first native payload value.</summary>
        public int? FirstValue { get; private set; }

        /// <inheritdoc />
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            NativeReaders++;

            if (NativeReaders == 1)
            {
                if (!await result.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("The retry fixture must observe its first actual native row.");
                }

                FirstValue = result.GetInt32(0);

                // WHY: RelationalCommand owns disposal of this failed reader. Do not close
                // or replace it here, and never inject this read retry into a write boundary.
                throw _failure;
            }

            return result;
        }
    }
}
