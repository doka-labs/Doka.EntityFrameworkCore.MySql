namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlRecursiveDataReaderTests
{
    private const string PacketBudgetDiagnostic =
        "The proved recursive text capacity exceeds the active server packet limit.";

    /// <summary>The distinct server packet-budget signal fails before a visible row or payload getter.</summary>
    /// <param name="asynchronous">Whether priming uses the asynchronous path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_packet_budget_is_rejected_before_payload_access(
        bool asynchronous
    )
    {
        // Arrange
        using var raw = GuardRow(typeof(int), 7, 1L, 2L);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => reader.PrimeAsync(cancellationToken))
            : Record.Exception(reader.Prime);

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(PacketBudgetDiagnostic, failure.Message);
        Assert.Equal(1, raw.ReadCalls);
        Assert.Equal(0, raw.PayloadReads);
    }

    /// <summary>Every cached execution rechecks the current statement guard, before existing buffering can read payload.</summary>
    /// <param name="asynchronous">Whether the failing execution is asynchronous.</param>
    /// <param name="buffered">Whether the caller requested EF retry buffering.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Cached_reader_rechecks_packet_budget_and_releases_owned_resources(
        bool asynchronous,
        bool buffered
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var policy = Policy([]);
        var command = scope.Command(policy);
        var parameters = scope.Parameters(null,
            buffered
                ? IntColumns()
                : null);

        using (command.ExecuteReader(parameters)) { }

        scope.Database.ReaderFactory = static () => GuardRow(typeof(int), 7, 1L, 2L);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(parameters));

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(PacketBudgetDiagnostic, failure.Message);
        Assert.Same(policy, command.RecursiveQueryPolicy);
        Assert.NotNull(scope.Database.LastReader);
        Assert.Equal(1, scope.Database.LastReader.ReadCalls);
        Assert.Equal(0, scope.Database.LastReader.PayloadReads);
        Assert.True(scope.Database.LastReader.IsClosed);
        Assert.Equal(2, scope.Database.CommandsDisposed);
        Assert.Equal(2, scope.Database.CloseCalls);
        Assert.Equal(ConnectionState.Closed, scope.Database.State);
    }

    /// <summary>Packet rejection retains a simultaneous reader-cleanup failure and still attempts command/connection cleanup.</summary>
    /// <param name="asynchronous">Whether execution and cleanup use asynchronous methods.</param>
    /// <param name="buffered">Whether EF's existing buffer owns the guarded input.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Packet_budget_and_reader_cleanup_failures_are_both_preserved(
        bool asynchronous,
        bool buffered
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var cleanup = new IOException("packet-reader-cleanup-canary");
        scope.Database.ReaderFactory = () =>
        {
            var raw = GuardRow(typeof(int), 7, 1L, 2L);
            raw.DisposeFailure = cleanup;
            raw.FailDisposeOnce = true;

            return raw;
        };

        var command = scope.Command(Policy([]));
        var parameters = scope.Parameters(null,
            buffered
                ? IntColumns()
                : null);

        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(parameters));

        // Assert
        var failures = Assert
            .IsType<AggregateException>(exception)
            .Flatten()
            .InnerExceptions;

        Assert.Contains(failures,
            failure => failure is InvalidOperationException && failure.Message == PacketBudgetDiagnostic);

        Assert.Contains(cleanup, failures);
        Assert.NotNull(scope.Database.LastReader);
        Assert.Equal(0, scope.Database.LastReader.PayloadReads);
        Assert.Equal(1, scope.Database.CommandsDisposed);
        Assert.Equal(1, scope.Database.CloseCalls);
        Assert.Equal(ConnectionState.Closed, scope.Database.State);
    }
}
