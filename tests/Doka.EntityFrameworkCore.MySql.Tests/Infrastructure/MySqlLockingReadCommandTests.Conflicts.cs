namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlLockingReadCommandTests
{
    /// <summary>Supplies direct/wrapped sync/async conflicts and ordinary-mode/deadlock controls.</summary>
    public static TheoryData<int, int, bool, bool, bool> NativeConflicts
    {
        get
        {
            var cases = new TheoryData<int, int, bool, bool, bool>();

            var conflicts = new[]
            {
                ((int)MySqlLockingReadMode.NoWait, 1205, true),
                ((int)MySqlLockingReadMode.NoWait, 3572, true),
                ((int)MySqlLockingReadMode.NoWait, 1213, false),
                ((int)MySqlLockingReadMode.Update, 1205, false),
                ((int)MySqlLockingReadMode.None, 1205, false),
            };

            foreach (var (mode, error, expectedMarked) in conflicts)
            {
                foreach (var asynchronous in new[] { false, true })
                {
                    foreach (var wrapped in new[] { false, true })
                    {
                        cases.Add(
                            mode,
                            error,
                            expectedMarked,
                            asynchronous,
                            wrapped);
                    }
                }
            }

            return cases;
        }
    }

    /// <summary>NOWAIT conflicts are terminal through bounded cause chains while ordinary failures keep their classification.</summary>
    /// <param name="modeValue">The internal locking policy represented by its numeric value.</param>
    /// <param name="errorNumber">The original native server error number.</param>
    /// <param name="expectedMarked">Whether the conflict must prevent automatic retry.</param>
    /// <param name="asynchronous">Whether to execute the asynchronous reader path.</param>
    /// <param name="wrapped">Whether the native failure is wrapped by another exception.</param>
    [Theory]
    [MemberData(nameof(NativeConflicts))]
    public async Task Native_conflict_marker_is_limited_to_nowait(
        int modeValue,
        int errorNumber,
        bool expectedMarked,
        bool asynchronous,
        bool wrapped
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = scope.Context.Database.BeginTransaction();
        var nativeFailure = CreateNativeFailure(errorNumber);
        var failure = wrapped
            ? new InvalidOperationException("wrapper-canary", nativeFailure)
            : (Exception)nativeFailure;

        scope.Database.ReaderFailure = failure;
        var detector = scope.Context.GetService<IMySqlTransientExceptionDetector>();
        var originalRetryDecision = detector.ShouldRetryOn(failure);
        var command = scope.Command((MySqlLockingReadMode)modeValue);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(scope.Parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(scope.Parameters));

        // Assert
        Assert.NotNull(exception);
        Assert.Same(failure, exception);
        Assert.Equal(expectedMarked, MySqlLockingReadMetadata.IsNoWaitConflict(nativeFailure));
        Assert.Equal(expectedMarked
                ? false
                : originalRetryDecision,
            detector.ShouldRetryOn(exception));

        Assert.Equal(1, scope.Database.ReaderCount);
        Assert.Same(transaction, scope.Connection.CurrentTransaction);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
    }

    /// <summary>The native cause is examined through exactly the detector's bounded chain depth.</summary>
    /// <param name="wrapperCount">The number of wrappers preceding the native conflict.</param>
    /// <param name="expectedMarked">Whether the native conflict lies within the supported inspection bound.</param>
    [Theory]
    [InlineData(19, true)]
    [InlineData(20, false)]
    public void Native_conflict_inspection_has_a_fixed_depth_bound(
        int wrapperCount,
        bool expectedMarked
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = scope.Context.Database.BeginTransaction();
        var nativeFailure = CreateNativeFailure(1205);
        Exception failure = nativeFailure;

        for (var index = 0; index < wrapperCount; index++)
        {
            failure = new InvalidOperationException("bounded-wrapper-canary", failure);
        }

        scope.Database.ReaderFailure = failure;
        var command = scope.Command(MySqlLockingReadMode.NoWait);

        // Act
        var exception = Record.Exception(() => command.ExecuteReader(scope.Parameters));

        // Assert
        Assert.Same(failure, exception);
        Assert.Equal(expectedMarked, MySqlLockingReadMetadata.IsNoWaitConflict(nativeFailure));
        Assert.Equal(1, scope.Database.ReaderCount);
    }

    private static MySqlException CreateNativeFailure(
        int number
    )
    {
        // WHY: MySqlConnector does not expose a public error-number constructor;
        // match the established detector tests without manufacturing a server failure.
        var constructor = typeof(MySqlException).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: [typeof(MySqlErrorCode), typeof(string), typeof(Exception)],
            modifiers: null);

        Assert.NotNull(constructor);

        return (MySqlException)constructor.Invoke([(MySqlErrorCode)number, "native-lock-canary", null]);
    }
}
