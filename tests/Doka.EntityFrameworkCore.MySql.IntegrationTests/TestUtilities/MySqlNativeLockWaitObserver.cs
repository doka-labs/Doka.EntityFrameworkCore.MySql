namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Observes a real InnoDB lock wait without treating elapsed time or pending tasks as proof.</summary>
internal static class MySqlNativeLockWaitObserver
{
    /// <summary>Waits until the server reports that the exact fresh physical worker session is blocked.</summary>
    /// <param name="mariaDb">Whether the observer uses MariaDB's cache-aware transaction catalog.</param>
    /// <param name="observer">The independently opened administrative observation connection.</param>
    /// <param name="worker">The already opened, fresh nonpooled physical command connection.</param>
    /// <param name="pending">The command whose actual native wait must be observed.</param>
    /// <param name="cancellationToken">The required hang-bounding token; elapsed time is never a success criterion.</param>
    internal static async Task ObserveAsync(
        bool mariaDb,
        MySqlConnection observer,
        MySqlConnection worker,
        Task pending,
        CancellationToken cancellationToken
    )
    {
        // WHY: MySQL refreshes INNODB_TRX only after 100 ms without reads; fast polling
        // can freeze an empty snapshot. Its direct PFS wait graph has no such idle-cache rule.
        // Source: mysql-8.4.13/storage/innobase/trx/trx0i_s.cc, can_cache_be_updated.
        var sql = mariaDb
            ? "SELECT COUNT(*) FROM information_schema.INNODB_TRX WHERE trx_mysql_thread_id = @worker AND trx_state = 'LOCK WAIT';"
            : "SELECT COUNT(*) FROM performance_schema.data_lock_waits AS waits "
            + "JOIN performance_schema.threads AS threads ON threads.THREAD_ID = waits.REQUESTING_THREAD_ID "
            + "WHERE threads.PROCESSLIST_ID = @worker AND waits.ENGINE = 'INNODB';";

        await using var command = new MySqlCommand(sql, observer);

        command.Parameters.AddWithValue("@worker", worker.ServerThread);

        while (!pending.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Convert.ToInt64(await command
                        .ExecuteScalarAsync(cancellationToken)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture)
                > 0)
            {
                return;
            }

            // WHY: MariaDB also has a 100 ms metadata-cache idle rule; 200 ms permits refresh.
            // Native wait data alone proves readiness, never the polling interval or elapsed time.
            // Fresh nonpooled workers exclude a prior command's wait on a reused session.
            await Task
                .Delay(mariaDb
                        ? 200
                        : 25,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await pending.ConfigureAwait(false);

        throw new XunitException("The command completed before its native InnoDB lock wait was observed.");
    }
}
