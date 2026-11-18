namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Exercises only native non-XA caller work shared by the three public API regression families.</summary>
internal static class MySqlEmulatedAmbientProbe
{
    /// <summary>Separates enlistment routes and surviving/fatal native outcomes on selected pinned profiles.</summary>
    internal static IEnumerable<TheoryDataRow<string, bool, string, bool>> Cases
    {
        get
        {
            foreach (var target in IntegrationTestEnvironment
                .GetSelectedTargets()
                .Where(target => IntegrationTestEnvironment
                    .GetSupportedTargets()
                    .Contains(target)))
            {
                foreach (var route in Enumerable.Range(0, 2))
                {
                    var automatic = route == 1;
                    yield return new TheoryDataRow<string, bool, string, bool>(target.ToString(),
                        automatic,
                        "valid",
                        false);

                    yield return new TheoryDataRow<string, bool, string, bool>(target.ToString(),
                        automatic,
                        "deadlock",
                        false);

                    yield return new TheoryDataRow<string, bool, string, bool>(target.ToString(),
                        automatic,
                        "timeout",
                        false);

                    if (target is IntegrationDatabaseTarget.MySql84 or IntegrationDatabaseTarget.MariaDb123)
                    {
                        yield return new TheoryDataRow<string, bool, string, bool>(target.ToString(),
                            automatic,
                            "timeout",
                            true);
                    }
                }
            }
        }
    }

    /// <summary>Uses a unique fixture database as the pool identity and explicitly opts into native transaction emulation.</summary>
    internal static string GetConnectionString(
        string connectionString,
        bool automatic
    ) => new MySqlConnectionStringBuilder(connectionString)
    {
        UseXaTransactions = false,
        AutoEnlist = automatic,
        Pooling = true,
        MaximumPoolSize = 1,
        ConnectionReset = true,
    }.ConnectionString;

    /// <summary>Creates independent control tables so a native failure cannot be confused with feature data or mapping.</summary>
    internal static Task<int> PrepareAsync(
        MySqlConnection observer,
        CancellationToken cancellationToken
    ) => ExecuteAsync(observer,
        "CREATE TABLE EmulatedAmbientProbe (`Id` INT PRIMARY KEY,`Value` INT NOT NULL) ENGINE=InnoDB;"
        + "INSERT INTO EmulatedAmbientProbe VALUES (1,10),(2,20);"
        + "CREATE TABLE EmulatedAmbientWeight (`Id` INT PRIMARY KEY) ENGINE=InnoDB;",
        cancellationToken);

    /// <summary>Establishes real prior work and, when requested, a native statement-only timeout or transaction-ending deadlock.</summary>
    internal static async Task<int?> PrepareCallerStateAsync(
        MySqlConnection observer,
        MySqlConnection worker,
        string connectionString,
        bool mariaDb,
        string mode,
        CancellationToken cancellationToken
    )
    {
        await ExecuteAsync(worker, "INSERT INTO EmulatedAmbientProbe VALUES (100,100);", cancellationToken)
            .ConfigureAwait(false);

        if (mode == "valid")
        {
            return null;
        }

        using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // WHY: An opened connection redacts credentials; the fixture retains the original private endpoint for its independent actor.
        var holderOptions = new MySqlConnectionStringBuilder(connectionString)
        {
            AutoEnlist = false,
            Pooling = false,
        };

        await using var holder = new MySqlConnection(holderOptions.ConnectionString);
        await holder
            .OpenAsync(probe.Token)
            .ConfigureAwait(false);

        await using var heldTransaction = await holder
            .BeginTransactionAsync(probe.Token)
            .ConfigureAwait(false);

        await ExecuteAsync(holder,
                "UPDATE EmulatedAmbientProbe SET `Value`=99 WHERE `Id`=2;",
                probe.Token,
                heldTransaction)
            .ConfigureAwait(false);

        var pending = Task.FromResult(0);
        Exception? primary = null;
        MySqlException? nativeFailure;

        try
        {
            if (mode == "deadlock")
            {
                // WHY: Separate undo weight makes the worker the native victim without range-locking its independent PK.
                var weight = string.Join(',',
                    Enumerable
                        .Range(1, 2000)
                        .Select(id => "(" + id.ToString(CultureInfo.InvariantCulture) + ")"));

                await ExecuteAsync(holder,
                        "INSERT INTO EmulatedAmbientWeight VALUES " + weight + ";",
                        probe.Token,
                        heldTransaction)
                    .ConfigureAwait(false);

                await ExecuteAsync(worker, "UPDATE EmulatedAmbientProbe SET `Value`=88 WHERE `Id`=1;", probe.Token)
                    .ConfigureAwait(false);

                pending = ExecuteAsync(worker, "UPDATE EmulatedAmbientProbe SET `Value`=77 WHERE `Id`=2;", probe.Token);
                await MySqlNativeLockWaitObserver
                    .ObserveAsync(mariaDb, observer, worker, pending, probe.Token)
                    .ConfigureAwait(false);

                await ExecuteAsync(holder,
                        "UPDATE EmulatedAmbientProbe SET `Value`=66 WHERE `Id`=1;",
                        probe.Token,
                        heldTransaction)
                    .ConfigureAwait(false);
            }
            else if (mode == "timeout")
            {
                // WHY: Native error 1205, not elapsed time, qualifies the startup rollback policy of this owned session.
                await ExecuteAsync(worker, "SET SESSION innodb_lock_wait_timeout=1;", probe.Token)
                    .ConfigureAwait(false);

                pending = ExecuteAsync(worker, "UPDATE EmulatedAmbientProbe SET `Value`=77 WHERE `Id`=2;", probe.Token);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            nativeFailure = Assert.IsType<MySqlException>(await Record
                .ExceptionAsync(async () => _ = await pending.ConfigureAwait(false))
                .ConfigureAwait(false));

            primary = nativeFailure;
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            // WHY: Only the holder is rolled back; the misleading ambient worker association must survive until the public API guard.
            await DatabaseLockProbeCleanup
                .CompleteLockProbeAsync(probe,
                    () => heldTransaction.RollbackAsync(CancellationToken.None),
                    pending,
                    primary,
                    null)
                .ConfigureAwait(false);
        }

        return nativeFailure.Number;
    }

    /// <summary>Reads an independent scalar without implicitly enlisting the observer or creating a local transaction.</summary>
    internal static async Task<int> CountAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = new MySqlCommand(sql, connection);

        return Convert.ToInt32(await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    /// <summary>Requires the native missing-marker proof instead of accepting an arbitrary translation or ambient error.</summary>
    internal static void AssertMissingReceipt(
        Exception? exception
    )
    {
        Assert.NotNull(exception);
        var causes = exception is AggregateException aggregate
            ? aggregate
                .Flatten()
                .InnerExceptions
                .ToArray()
            : [exception];

        Assert.NotEmpty(causes);
        Assert.All(causes,
            cause => Assert.Equal(1305,
                Assert.IsType<MySqlException>(cause)
                    .Number));
    }

    /// <summary>Executes native fixture work without inventing an explicit transaction for an enlisted ambient session.</summary>
    private static async Task<int> ExecuteAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        MySqlTransaction? transaction = null
    )
    {
        await using var command = new MySqlCommand(sql, connection, transaction);

        return await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
