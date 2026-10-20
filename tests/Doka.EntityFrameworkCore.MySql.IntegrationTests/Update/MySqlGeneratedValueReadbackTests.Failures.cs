namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGeneratedValueReadbackTests
{
    /// <summary>Preserves the explicit EF opt-out from automatic transactions.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task NeverWithoutTransactionUsesPerRowFallback()
    {
        var capture = new ReadbackCapture();
        await using var context = CreateContext<int, StandardMode>(IntegrationDatabaseTarget.MySql84, capture);
        await RecreateTableAsync(context);
        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        var rows = CreateIntegerRows(8);
        AddRows(context, rows);
        capture.Enabled = true;

        await context.SaveChangesAsync(CancellationToken.None);

        Assert.All(rows, AssertGenerated);
        Assert.Equal(8, capture.Commands.Sum(sql => CountOccurrences(sql, "INSERT INTO")));
        Assert.DoesNotContain(capture.Commands, sql => sql.Contains("UNION ALL", StringComparison.Ordinal));
    }

    /// <summary>Uses an existing transaction even when automatic transactions are disabled.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task NeverWithCallerTransactionCanBatchReadback()
    {
        var capture = new ReadbackCapture();
        await using var context = CreateContext<int, StandardMode>(IntegrationDatabaseTarget.MySql84, capture);
        await RecreateTableAsync(context);
        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        await using var transaction = await context.Database.BeginTransactionAsync(CancellationToken.None);
        var rows = CreateIntegerRows(8);
        AddRows(context, rows);
        capture.Enabled = true;

        await context.SaveChangesAsync(CancellationToken.None);

        Assert.All(rows, AssertGenerated);
        Assert.Equal(1, capture.Commands.Sum(sql => CountOccurrences(sql, "INSERT INTO")));
        Assert.Equal(7, capture.Commands.Sum(sql => CountOccurrences(sql, "UNION ALL")));
    }

    /// <summary>Leaves generated identity keys on the established per-row path.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task TemporaryIdentityKeysKeepPerRowFallback()
    {
        var capture = new ReadbackCapture();
        await using var context = CreateContext<int, IdentityMode>(IntegrationDatabaseTarget.MySql84, capture);
        await RecreateTableAsync(context);
        var rows = Enumerable.Range(1, 8).Select(value => new GeneratedRow<int> { Input = value }).ToArray();

        AddRows(context, rows);
        Assert.All(rows, row => Assert.True(context.Entry(row).Property(item => item.Id).IsTemporary));
        capture.Enabled = true;

        await context.SaveChangesAsync(CancellationToken.None);

        Assert.All(rows, row => Assert.True(row.Id > 0));
        Assert.All(rows, AssertGenerated);
        Assert.Equal(8, rows.Select(row => row.Id).Distinct().Count());
        Assert.Equal(8, capture.Commands.Sum(sql => CountOccurrences(sql, "INSERT INTO")));
        Assert.DoesNotContain(capture.Commands, sql => sql.Contains("UNION ALL", StringComparison.Ordinal));
    }

    /// <summary>Does not treat a written zero on AUTO_INCREMENT as a stable correlation key.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task WrittenAutoIncrementZeroWithCustomSentinelUsesSafeFallback()
    {
        var capture = new ReadbackCapture();
        await using var context = CreateContext<int, IdentityZeroMode>(IntegrationDatabaseTarget.MySql84, capture);
        await RecreateTableAsync(context);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync(
            "SET SESSION sql_mode = REPLACE(@@SESSION.sql_mode, 'NO_AUTO_VALUE_ON_ZERO', '');",
            CancellationToken.None);
        var rows = new[]
        {
            new GeneratedRow<int> { Id = 0, Input = 1 },
            new GeneratedRow<int> { Id = 5, Input = 2 },
        };

        AddRows(context, rows);
        Assert.False(context.Entry(rows[0]).Property(row => row.Id).IsTemporary);
        capture.Enabled = true;

        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        capture.Enabled = false;
        Assert.IsAssignableFrom<DbUpdateException>(exception);
        Assert.Equal(0, await context.Rows.AsNoTracking().CountAsync(CancellationToken.None));
        Assert.DoesNotContain(capture.Commands, sql => sql.Contains("UNION ALL", StringComparison.Ordinal));
    }

    /// <summary>Splits stable and temporary key groups without losing result alignment.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MixedKnownAndTemporaryIdentityGroupsKeepAllGeneratedValues()
    {
        var capture = new ReadbackCapture();
        await using var context = CreateContext<int, IdentityMode>(IntegrationDatabaseTarget.MySql84, capture);
        await RecreateTableAsync(context);
        // WHY: Temporary keys may sort first; disjoint supplied keys prevent an
        // auto-increment allocation from colliding with the arranged explicit rows.
        var rows = Enumerable.Range(1, 6)
            .Select(value => new GeneratedRow<int> { Id = value + 100, Input = value })
            .ToArray();

        var temporary = new[]
        {
            new GeneratedRow<int> { Input = 7 },
            new GeneratedRow<int> { Input = 8 },
        };

        AddRows(context, rows);
        AddRows(context, temporary);
        capture.Enabled = true;

        await context.SaveChangesAsync(CancellationToken.None);

        Assert.All(rows.Concat(temporary), AssertGenerated);
        Assert.Equal(8, rows.Concat(temporary).Select(row => row.Id).Distinct().Count());
        Assert.Contains(capture.Commands, sql => sql.Contains("UNION ALL", StringComparison.Ordinal));
        Assert.Equal(3, capture.Commands.Sum(sql => CountOccurrences(sql, "INSERT INTO")));
    }

    /// <summary>Reads trigger-modified non-key outputs through the safe declared-trigger fallback.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task DeclaredNonKeyTriggerKeepsPerRowFallback()
    {
        var capture = new ReadbackCapture();
        await using var context = CreateContext<int, DeclaredTriggerMode>(IntegrationDatabaseTarget.MySql84, capture);
        await RecreateTableAsync(context);
        await context.Database.ExecuteSqlRawAsync(
            $"CREATE TRIGGER `ReadbackBeforeInsert` BEFORE INSERT ON `{TableName}` "
            + "FOR EACH ROW SET NEW.`Input` = NEW.`Input` + 100;", CancellationToken.None);
        var rows = CreateIntegerRows(8);
        AddRows(context, rows);
        capture.Enabled = true;

        await context.SaveChangesAsync(CancellationToken.None);

        Assert.All(rows, row => Assert.Equal(((row.Input + 100) * 17L) + 3L, row.Stamp));
        Assert.Equal(8, capture.Commands.Sum(sql => CountOccurrences(sql, "INSERT INTO")));
        Assert.DoesNotContain(capture.Commands, sql => sql.Contains("UNION ALL", StringComparison.Ordinal));
    }

    /// <summary>Fails and rolls back instead of correlating a trigger-rewritten stable key incorrectly.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task DeclaredKeyRewriteFailsWithoutCommittingRows()
    {
        var capture = new ReadbackCapture();
        await using var context = CreateContext<int, DeclaredTriggerMode>(IntegrationDatabaseTarget.MySql84, capture);
        await RecreateTableAsync(context);
        await context.Database.ExecuteSqlRawAsync(
            $"CREATE TRIGGER `ReadbackBeforeInsert` BEFORE INSERT ON `{TableName}` "
            + "FOR EACH ROW SET NEW.`Id` = NEW.`Id` + 100;", CancellationToken.None);
        AddRows(context, CreateIntegerRows(8));
        capture.Enabled = true;

        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        capture.Enabled = false;
        Assert.IsAssignableFrom<DbUpdateException>(exception);
        Assert.Equal(0, await context.Rows.AsNoTracking().CountAsync(CancellationToken.None));
        Assert.DoesNotContain(capture.Commands, sql => sql.Contains("UNION ALL", StringComparison.Ordinal));
    }

    /// <summary>Preserves native duplicate detection instead of deduplicating requested identities.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task NativeEqualCompositeKeysRejectAndRollBack()
    {
        await using var context = CreateContext<long, ShadowCompositeMode>(IntegrationDatabaseTarget.MySql84);
        await RecreateTableAsync(context);
        var first = new GeneratedRow<long> { Id = 41, Input = 1 };
        var second = new GeneratedRow<long> { Id = 41, Input = 2 };
        AddRows(context, [first, second]);

        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        var update = Assert.IsType<DbUpdateException>(exception);
        var native = Assert.IsType<MySqlException>(update.InnerException);
        Assert.Equal(1062, native.Number);
        Assert.Equal(0, await context.Rows.AsNoTracking().CountAsync(CancellationToken.None));
    }

    /// <summary>Rolls back an earlier successful insert group when the next group violates a constraint.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task LaterBatchFailureRollsBackEarlierGeneratedValues()
    {
        await using var context = CreateContext<int, StandardMode>(IntegrationDatabaseTarget.MySql84);
        await RecreateTableAsync(context);
        var rows = CreateIntegerRows(1001);
        rows[^1].Input = -1;
        AddRows(context, rows);

        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        Assert.IsType<DbUpdateException>(exception);
        Assert.Equal(0, await context.Rows.AsNoTracking().CountAsync(CancellationToken.None));
        Assert.All(rows, row => Assert.Equal(0, row.Stamp));
        Assert.All(rows, row => Assert.Equal(EntityState.Added, context.Entry(row).State));
    }

    /// <summary>Allows a corrected save after rollback discarded earlier generated-value sidecars.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task LaterBatchFailureCanBeCorrectedAndRetried()
    {
        await using var context = CreateContext<int, StandardMode>(IntegrationDatabaseTarget.MySql84);
        await RecreateTableAsync(context);
        var rows = CreateIntegerRows(1001);
        rows[^1].Input = -1;
        AddRows(context, rows);
        await FailInitialSaveAsync(context);
        rows[^1].Input = 1001;

        var written = await context.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(1001, written);
        Assert.All(rows, AssertGenerated);
        Assert.Equal(1001, await context.Rows.AsNoTracking().CountAsync(CancellationToken.None));
    }

    /// <summary>Retains writes made by the caller before EF's savepoint on a late failed group.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task LaterBatchFailurePreservesCallerTransactionWrite()
    {
        await using var context = CreateContext<int, StandardMode>(IntegrationDatabaseTarget.MySql84);
        await RecreateTableAsync(context);
        await using var transaction = await context.Database.BeginTransactionAsync(CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO `{TableName}` (`Id`, `Input`) VALUES (9000, 9000);", CancellationToken.None);
        var rows = CreateIntegerRows(1001);
        rows[^1].Input = -1;
        AddRows(context, rows);

        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        Assert.IsType<DbUpdateException>(exception);
        Assert.Equal(9000, await context.Rows.AsNoTracking().Select(row => row.Id)
            .SingleAsync(CancellationToken.None));
        Assert.All(rows, row => Assert.Equal(0, row.Stamp));
    }

    private static GeneratedRow<int>[] CreateIntegerRows(int count) => Enumerable.Range(1, count)
        .Select(value => new GeneratedRow<int> { Id = value, Input = value })
        .ToArray();

    private static async Task FailInitialSaveAsync(DbContext context)
    {
        try
        {
            await context.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException)
        {
            return;
        }

        throw new InvalidOperationException("The arranged late insert failure did not occur.");
    }
}
