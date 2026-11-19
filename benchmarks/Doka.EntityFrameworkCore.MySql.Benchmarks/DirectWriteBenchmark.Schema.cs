namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

public partial class DirectWriteBenchmark
{
    private async Task PrepareSchemaAsync(
        CancellationToken cancellationToken
    )
    {
        _database = "doka_directwrite_"
            + Guid
                .NewGuid()
                .ToString("N", CultureInfo.InvariantCulture);

        var server =
            new MySqlConnectionStringBuilder(BenchmarkEnvironment.CreateConnectionString(_database, pooling: false))
            {
                Database = string.Empty,
                AutoEnlist = false,
            };

        _serverConnectionString = server.ConnectionString;
        _observer = new MySqlConnection(_serverConnectionString);
        await _observer
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        // WHY: The unique name belongs to this fixture even if CREATE's response is lost;
        // cleanup may safely attempt DROP IF EXISTS without touching the shared benchmark database.
        _ownsDatabase = true;
        await ExecuteNativeAsync(Observer,
                "CREATE DATABASE `" + _database + "` CHARACTER SET utf8mb4;",
                cancellationToken)
            .ConfigureAwait(false);

        await Observer
            .ChangeDatabaseAsync(_database, cancellationToken)
            .ConfigureAwait(false);

        _connectionString = BenchmarkEnvironment.CreateConnectionString(_database);
        _services = new ServiceCollection()
            .AddEntityFrameworkDokaMySql()
            .BuildServiceProvider();

        PrepareContext(new DirectWriteContext(CreateOptions()));

        // WHY: Pool keys include provider-normalized driver settings. Capture the effective
        // connection string before opening can redact credentials, without ever logging it.
        _connectionString = Context.Database.GetDbConnection()
            .ConnectionString;

        await Context
            .Database
            .EnsureCreatedAsync(cancellationToken)
            .ConfigureAwait(false);

        await SeedSourceAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private void PrepareUpsertInput()
    {
        // WHY: INSERT SELECT has only its native source. Retaining an unused 14,000-row
        // UPSERT input there would distort its fixture heap even outside measured allocations.
        if (_width == 3)
        {
            _narrowInput = Enumerable
                .Range(1, _rowCount)
                .Select(id => new NarrowTarget
                {
                    Id = id,
                    Value1 = checked(id * 100 + 1),
                    Value2 = checked(id * 100 + 2),
                })
                // WHY: Keep this benchmark's explicit eager materialization and allocation path.
                // ReSharper disable once UseCollectionExpression
                .ToArray();
        }
        else
        {
            _wideInput = Enumerable
                .Range(1, _rowCount)
                .Select(NewWideInput)
                // WHY: Keep this benchmark's explicit eager materialization and allocation path.
                // ReSharper disable once UseCollectionExpression
                .ToArray();
        }
    }

    private DbContextOptions<DirectWriteContext> CreateOptions(
        WriteObservation? observation = null,
        TransactionObservation? transactions = null
    )
    {
        var builder = new DbContextOptionsBuilder<DirectWriteContext>()
            .UseInternalServiceProvider(_services
                ?? throw new InvalidOperationException("The owned provider graph was not prepared."))
            .UseMySql(_connectionString, BenchmarkEnvironment.ServerVersionValue);

        if (observation is not null)
        {
            builder.AddInterceptors(observation);
        }

        if (transactions is not null)
        {
            builder.AddInterceptors(transactions);
        }

        return builder.Options;
    }

    private async Task SeedSourceAsync(
        CancellationToken cancellationToken
    )
    {
        var fields = Enumerable
            .Range(0, _width)
            .Select(FieldName)
            .ToArray();

        var columns = string.Join(", ", fields.Select(name => "`" + name + "`"));
        var values = string.Join(", ",
            Enumerable
                .Range(0, _width)
                .Select(index => index == 0
                    ? "number"
                    : "number * 100 + " + index.ToString(CultureInfo.InvariantCulture)));

        // WHY: Generate the INSERT SELECT source on the server so no artificial managed
        // source population is retained or attributed to its provider allocation measurement.
        var sql = "CREATE TABLE `DirectWriteDigits` (`Digit` int NOT NULL PRIMARY KEY);"
            + "INSERT INTO `DirectWriteDigits` VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9);"
            + "INSERT INTO `"
            + SourceTable
            + "` ("
            + columns
            + ") SELECT "
            + values
            + " FROM (SELECT 1 + a.`Digit` + 10*b.`Digit` + 100*c.`Digit` + 1000*d.`Digit` + 10000*e.`Digit` AS number"
            + " FROM `DirectWriteDigits` a CROSS JOIN `DirectWriteDigits` b CROSS JOIN `DirectWriteDigits` c"
            + " CROSS JOIN `DirectWriteDigits` d CROSS JOIN `DirectWriteDigits` e) numbers WHERE number <= "
            + _rowCount.ToString(CultureInfo.InvariantCulture)
            + ";";

        await ExecuteNativeAsync(Observer, sql, cancellationToken)
            .ConfigureAwait(false);
    }

    private string FieldName(
        int index
    ) => index == 0
        ? "Id"
        : _width == 3
            ? "Value" + index.ToString(CultureInfo.InvariantCulture)
            : "C" + index.ToString("D2", CultureInfo.InvariantCulture);

    private Task<int> ResetTargetAsync(
        CancellationToken cancellationToken
    ) => ExecuteNativeAsync(Observer, "TRUNCATE TABLE `" + TargetTable + "`;", cancellationToken);

    private static async Task<int> ExecuteNativeAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        var command = new MySqlCommand(sql, connection);
        Exception? failure = null;
        var result = 0;

        try
        {
            result = await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            // WHY: Cleanup completes independently of the fixture's forward cancellation.
            await command
                .DisposeAsync()
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = CombineCleanupFailure(failure, exception);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }

        return result;
    }

    /// <summary>Attempts every fixture-owned cleanup stage without discarding an earlier failure.</summary>
    private async Task CleanupAsync()
    {
        // WHY: BenchmarkDotNet cleanup has no request token, and a failed workload must
        // still dispose its context/pool/observer and attempt removal of its unique schema.
        Exception? failure = null;
        var context = _context;
        _context = null;
        _narrowSource = null;
        _wideSource = null;

        if (context is not null)
        {
            try
            {
                await context
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        if (_connectionString.Length != 0)
        {
            try
            {
                // WHY: Each unique fixture has its own pool key. Clear only that key,
                // rather than retaining idle connections to a database about to be removed.
                await using var pool = new MySqlConnection(_connectionString);
                await MySqlConnection
                    .ClearPoolAsync(pool, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = CombineCleanupFailure(failure, exception);
            }
        }

        var observer = _observer;
        _observer = null;

        if (observer is not null)
        {
            try
            {
                await observer
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = CombineCleanupFailure(failure, exception);
            }
        }

        if (_ownsDatabase)
        {
            try
            {
                await using var connection = new MySqlConnection(_serverConnectionString);
                await connection
                    .OpenAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                await ExecuteNativeAsync(connection,
                        "DROP DATABASE IF EXISTS `" + _database + "`;",
                        CancellationToken.None)
                    .ConfigureAwait(false);

                _ownsDatabase = false;
            }
            catch (Exception exception)
            {
                failure = CombineCleanupFailure(failure, exception);
            }
        }

        var services = _services;
        _services = null;

        if (services is not null)
        {
            try
            {
                await services
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = CombineCleanupFailure(failure, exception);
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    private static Exception CombineCleanupFailure(
        Exception? primary,
        Exception cleanup
    ) => primary is null
        ? cleanup
        : new AggregateException(primary, cleanup);
}
