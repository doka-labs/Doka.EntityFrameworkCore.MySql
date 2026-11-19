namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlLockingReadIntegrationTests
{
    /// <summary>Reports direct joins, reference includes and directed join edges with both read-lock strengths.</summary>
    public static IEnumerable<TheoryDataRow<string, string, bool>> DirectJoinCases()
    {
        foreach (var target in SelectedTargets)
        {
            foreach (var shape in new[] { "join", "left", "reference", "straight" })
            {
                yield return new TheoryDataRow<string, string, bool>(target.Data, shape, false);
                yield return new TheoryDataRow<string, string, bool>(target.Data, shape, true);
            }
        }
    }

    /// <summary>Direct joins and reference includes preserve results while locking every directly joined table.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(DirectJoinCases))]
    public async Task Direct_join_reference_include_and_straight_join_lock_both_tables(
        string target,
        string shape,
        bool shared
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        var commands = new LockCommands();
        await using var context = store.CreateContext(commands);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);

        // Act
        var result = await ReadDirectJoinAsync(context,
            shape,
            shared
                ? "share"
                : "update",
            safety.Token);

        var rowConflict = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);
        var parentConflict = await ProbePkNoWaitAsync(store, ParentsTable, 1, safety.Token);

        // Assert
        Assert.Equal((1, 1), result);
        AssertNativeConflict(store, rowConflict);
        AssertNativeConflict(store, parentConflict);
        var sql = Assert.Single(commands.Statements);
        Assert.Contains(RowsTable, sql, StringComparison.Ordinal);
        Assert.Contains(ParentsTable, sql, StringComparison.Ordinal);

        if (shape == "straight")
        {
            Assert.Contains("STRAIGHT_JOIN", sql, StringComparison.OrdinalIgnoreCase);
        }

        if (shape == "left")
        {
            Assert.Contains("LEFT JOIN", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>An unmatched direct left join preserves native null extension and locks its returned root row.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Unmatched_left_join_preserves_native_result_and_root_pk_lock(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        await using var native = await store.OpenNativeAsync(safety.Token);
        await using var control = new MySqlCommand(
            $"SELECT CONCAT(r.`Id`, ':', COALESCE(p.`Id`, 0)) FROM `{RowsTable}` AS r "
            + $"LEFT JOIN `{ParentsTable}` AS p ON r.`ParentId` = p.`Id` AND p.`Id` = 99 WHERE r.`Id` = 1;",
            native);

        var commands = new LockCommands();
        await using var context = store.CreateContext(commands);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        var query = context
            .Set<LockRow>()
            .Where(row => row.Id == 1)
            .GroupJoin(context.Set<LockParent>(),
                row => new
                {
                    Id = row.ParentId,
                    Match = true,
                },
                parent => new
                {
                    parent.Id,
                    Match = parent.Id == 99,
                },
                (row, parents) => new
                {
                    Row = row,
                    Parents = parents,
                })
            .SelectMany(pair => pair.Parents.DefaultIfEmpty(),
                (pair, parent) => new JoinedLockResult
                {
                    Id = pair.Row.Id,
                    ParentId = parent == null
                        ? 0
                        : parent.Id,
                })
            .ForUpdate();

        // Act
        var expected = Convert.ToString(await control.ExecuteScalarAsync(safety.Token), CultureInfo.InvariantCulture);
        var actual = await query.SingleAsync(safety.Token);
        var rootLock = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);

        // Assert
        Assert.Equal("1:0", expected);
        Assert.Equal(expected,
            actual.Id.ToString(CultureInfo.InvariantCulture)
            + ":"
            + actual.ParentId.ToString(CultureInfo.InvariantCulture));

        AssertNativeConflict(store, rootLock);
        Assert.Contains("LEFT JOIN", Assert.Single(commands.Statements), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An unmarked scalar subquery follows its independently qualified native scope rather than acquiring a recursive marker.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Ordinary_scalar_subquery_preserves_native_independent_lock_scope(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        await using var native = await store.OpenNativeAsync(safety.Token);
        await using var nativeTransaction = await native.BeginTransactionAsync(safety.Token);
        await using var control = new MySqlCommand(
            $"SELECT r.`Id`, (SELECT p.`Name` FROM `{ParentsTable}` AS p WHERE p.`Id` = r.`ParentId` LIMIT 1) "
            + $"FROM `{RowsTable}` AS r WHERE r.`Id` = 1 FOR UPDATE;",
            native,
            nativeTransaction);

        // Act
        _ = await control.ExecuteScalarAsync(safety.Token);
        var nativeParentScope = await ProbePkNoWaitAsync(store, ParentsTable, 1, safety.Token);
        await nativeTransaction.RollbackAsync(safety.Token);
        var commands = new LockCommands();
        await using var context = store.CreateContext(commands);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        var actual = await context
            .Set<LockRow>()
            .Where(row => row.Id == 1)
            .Select(row => new ScalarLockResult
            {
                Id = row.Id,
                ParentName = context
                    .Set<LockParent>()
                    .Where(parent => parent.Id == row.ParentId)
                    .Select(parent => parent.Name)
                    .FirstOrDefault(),
            })
            .ForUpdate()
            .SingleAsync(safety.Token);

        var providerParentScope = await ProbePkNoWaitAsync(store, ParentsTable, 1, safety.Token);
        var outerScope = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);
        _output.WriteLine(
            $"Scalar subquery: {store.Version}, native parent conflict={nativeParentScope}, provider={providerParentScope}.");

        // Assert
        Assert.Equal(1, actual.Id);
        Assert.Equal("first", actual.ParentName);
        Assert.Equal(nativeParentScope, providerParentScope);
        AssertNativeConflict(store, outerScope);
        Assert.Equal(1,
            Assert
                .Single(commands.Statements)
                .Split("FOR UPDATE")
                .Length
            - 1);

        if (!store.Version.IsMariaDb)
        {
            Assert.Equal(0, nativeParentScope);
        }
    }

    /// <summary>Covering-index shared projections preserve the native isolation/access-path boundary without claiming entity-wide immutability.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BooleanCases))]
    public async Task Shared_covering_projection_matches_native_pk_scope_for_selected_isolation(
        string target,
        bool readCommitted
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        var isolation = readCommitted
            ? IsolationLevel.ReadCommitted
            : IsolationLevel.RepeatableRead;

        await using var native = await store.OpenNativeAsync(safety.Token);
        await using var nativeTransaction = await native.BeginTransactionAsync(isolation, safety.Token);
        await using var control = new MySqlCommand(
            $"SELECT `Value` FROM `{RowsTable}` WHERE `Value` = 10 {ExpectedClause(store, "share")};",
            native,
            nativeTransaction);

        // Act
        var expectedValue =
            Convert.ToInt32(await control.ExecuteScalarAsync(safety.Token), CultureInfo.InvariantCulture);

        var nativePkScope = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);
        await nativeTransaction.RollbackAsync(safety.Token);
        await using var context = store.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(isolation, safety.Token);
        var actualValue = await context
            .Set<LockRow>()
            .Where(row => row.Value == 10)
            .ForShare()
            .Select(row => row.Value)
            .SingleAsync(safety.Token);

        var providerPkScope = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);
        _output.WriteLine(
            $"Covering shared projection: {store.Version}, {isolation}, native primary conflict={nativePkScope}, provider={providerPkScope}.");

        // Assert
        Assert.Equal(10, expectedValue);
        Assert.Equal(expectedValue, actualValue);
        Assert.Equal(nativePkScope, providerPkScope);
        Assert.True(nativePkScope is 0 or 1205 or 3572);
    }

    /// <summary>Materializes only supported direct join shapes without introducing a derived relation.</summary>
    private static async Task<(int Row, int Parent)> ReadDirectJoinAsync(
        LockContext context,
        string shape,
        string mode,
        CancellationToken cancellationToken
    )
    {
        var rows = context
            .Set<LockRow>()
            .Where(row => row.Id == 1);

        if (shape == "reference")
        {
            var row = await Mark(rows.Include(row => row.Parent), mode)
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);

            return (row.Id, row.Parent.Id);
        }

        var query = shape switch
        {
            "join" => rows.Join(context.Set<LockParent>(),
                row => row.ParentId,
                parent => parent.Id,
                (row, parent) => new JoinedLockResult
                {
                    Id = row.Id,
                    ParentId = parent.Id,
                }),
            "left" => rows
                .GroupJoin(context.Set<LockParent>(),
                    row => row.ParentId,
                    parent => parent.Id,
                    (row, parents) => new
                    {
                        Row = row,
                        Parents = parents,
                    })
                .SelectMany(pair => pair.Parents.DefaultIfEmpty(),
                    (pair, parent) => new JoinedLockResult
                    {
                        Id = pair.Row.Id,
                        ParentId = parent == null
                            ? 0
                            : parent.Id,
                    }),
            "straight" => rows.StraightJoin(context.Set<LockParent>(),
                row => row.ParentId,
                parent => parent.Id,
                (row, parent) => new JoinedLockResult
                {
                    Id = row.Id,
                    ParentId = parent.Id,
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var result = await Mark(query, mode)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        return (result.Id, result.ParentId);
    }

    private sealed class JoinedLockResult
    {
        public int Id { get; init; }

        public int ParentId { get; init; }
    }

    private sealed class ScalarLockResult
    {
        public int Id { get; init; }

        public string? ParentName { get; init; }
    }
}
