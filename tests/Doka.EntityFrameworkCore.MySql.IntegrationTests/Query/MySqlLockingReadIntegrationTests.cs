namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Qualifies explicit locking reads against coordinated native InnoDB sessions on the pinned engine matrix.</summary>
[IntegrationDatabaseTest]
[Trait("VerificationLane", "FullIntegration")]
public sealed partial class MySqlLockingReadIntegrationTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Retains native dialect and isolation observations alongside each independently reported case.</summary>
    public MySqlLockingReadIntegrationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Reports exactly the selected supported pinned targets.</summary>
    public static TheoryData<string> SelectedTargets => new(IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => IntegrationTestEnvironment
            .GetSupportedTargets()
            .Contains(target))
        .Select(target => target.ToString()));

    /// <summary>Reports page, First, Single and their default-returning variants independently.</summary>
    public static TheoryData<string, string> TerminalCases
    {
        get
        {
            var cases = new TheoryData<string, string>();

            foreach (var target in SelectedTargets)
            {
                cases.Add(target, "page");
                cases.Add(target, "first");
                cases.Add(target, "single");
                cases.Add(target, "first-default");
                cases.Add(target, "single-default");
            }

            return cases;
        }
    }

    /// <summary>Reports the four locking modes with tracking enabled and disabled independently.</summary>
    public static TheoryData<string, string, bool> ModeCases()
    {
        var cases = new TheoryData<string, string, bool>();

        foreach (var target in SelectedTargets)
        {
            foreach (var mode in new[] { "update", "share", "nowait", "skip" })
            {
                cases.Add(target, mode, false);
                cases.Add(target, mode, true);
            }
        }

        return cases;
    }

    /// <summary>Every mode preserves filtered data and tracking while actually holding a native row lock.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(ModeCases))]
    public async Task Each_mode_preserves_results_tracking_and_real_pk_lock(
        string target,
        string mode,
        bool noTracking
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        var commands = new LockCommands();
        await using var context = store.CreateContext(commands);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        var minimumId = 1;
        var query = context
            .Set<LockRow>()
            .Where(row => row.Id >= minimumId)
            .OrderBy(row => row.Id)
            .AsQueryable();

        if (noTracking)
        {
            query = query.AsNoTracking();
        }

        // Act
        var rows = await Mark(query, mode)
            .ToArrayAsync(safety.Token);

        var conflict = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);

        // Assert
        Assert.Equal([1, 2, 3], rows.Select(row => row.Id));
        Assert.Equal(noTracking
                ? 0
                : 3,
            context
                .ChangeTracker
                .Entries<LockRow>()
                .Count());

        AssertNativeConflict(store, conflict);
        var sql = Assert.Single(commands.Statements);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(ExpectedClause(store, mode),
            sql
                .Trim()
                .TrimEnd(';'),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Paging and normal cardinality terminals preserve deterministic results and lock the selected row.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TerminalCases))]
    public async Task Paging_first_and_single_preserve_the_selected_pk_lock(
        string target,
        string terminal
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        var query = context
            .Set<LockRow>()
            .Where(row => row.Id > 0)
            .OrderBy(row => row.Id)
            .Skip(1)
            .Take(1)
            .ForUpdate();

        // Act
        var id = terminal switch
        {
            "page" => (await query.ToArrayAsync(safety.Token)).Single()
                .Id,
            "first" => (await query.FirstAsync(safety.Token)).Id,
            "single" => (await query.SingleAsync(safety.Token)).Id,
            "first-default" => (await query.FirstOrDefaultAsync(safety.Token))?.Id ?? 0,
            "single-default" => (await query.SingleOrDefaultAsync(safety.Token))?.Id ?? 0,
            _ => throw new ArgumentOutOfRangeException(nameof(terminal)),
        };

        var conflict = await ProbePkNoWaitAsync(store, RowsTable, 2, safety.Token);

        // Assert
        Assert.Equal(2, id);
        AssertNativeConflict(store, conflict);
    }

    /// <summary>A borrowed transaction and its connection remain owned by the caller after the context is disposed.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Borrowed_transaction_remains_active_after_context_disposal_until_caller_commit(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        await using var connection = await store.OpenNativeAsync(safety.Token);
        await using var transaction = await connection.BeginTransactionAsync(safety.Token);
        var commands = new LockCommands();
        await using var context = store.CreateContext(commands, connection: connection);
        await context.Database.UseTransactionAsync(transaction, safety.Token);

        // Act
        var row = await context
            .Set<LockRow>()
            .Where(row => row.Id == 1)
            .ForUpdate()
            .SingleAsync(safety.Token);

        await context.DisposeAsync();
        var held = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);
        var callerStillOwnsConnection = ReferenceEquals(transaction.Connection, connection)
            && connection.State == ConnectionState.Open;

        await transaction.CommitAsync(safety.Token);
        var released = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);

        // Assert
        Assert.Equal(1, row.Id);
        Assert.True(callerStillOwnsConnection);
        Assert.Same(connection, Assert.Single(commands.Connections));
        AssertNativeConflict(store, held);
        Assert.Equal(0, released);
    }

    /// <summary>A native NOWAIT probe proves row-lock ownership without relying on elapsed execution time.</summary>
    private static async Task<int> ProbePkNoWaitAsync(
        LockStore store,
        string table,
        int id,
        CancellationToken cancellationToken
    )
    {
        await using var connection = await store
            .OpenNativeAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = new MySqlCommand($"SELECT `Id` FROM `{table}` WHERE `Id` = @id FOR UPDATE NOWAIT;",
            connection,
            transaction);

        command.Parameters.AddWithValue("@id", id);

        try
        {
            _ = await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);

            return 0;
        }
        catch (MySqlException exception)
        {
            return exception.Number;
        }
    }

    /// <summary>Asserts the qualified native conflict code without classifying ordinary timeout errors globally.</summary>
    private static void AssertNativeConflict(
        LockStore store,
        int code
    ) => Assert.Equal(store.Version.IsMariaDb
            ? 1205
            : 3572,
        code);

    /// <summary>Returns the exact suffix for the configured engine and public locking mode.</summary>
    private static string ExpectedClause(
        LockStore store,
        string mode
    ) => mode switch
    {
        "update" => "FOR UPDATE",
        "share" => store.Version.IsMariaDb
            ? "LOCK IN SHARE MODE"
            : "FOR SHARE",
        "nowait" => "FOR UPDATE NOWAIT",
        "skip" => "FOR UPDATE SKIP LOCKED",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
