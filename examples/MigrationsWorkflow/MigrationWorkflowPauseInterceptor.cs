using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.MySql.Examples.MigrationsWorkflow;

/// <summary>
/// Pauses the integration harness immediately before its first application-table
/// DDL command so a parent test can terminate the migrator process while EF owns
/// the migration lock.
/// </summary>
internal sealed class MigrationWorkflowPauseInterceptor : DbCommandInterceptor
{
    private const string MigrationCommandMarker =
        "CREATE TABLE `" + MigrationWorkflowOperationHandlerExtensions.EvidenceTableName + "`";
    private readonly string _pauseFile;
    private int _pauseSignaled;

    /// <summary>
    /// Creates an interceptor that signals through the supplied marker file.
    /// </summary>
    /// <param name="pauseFile">Marker file written immediately before the pause.</param>
    public MigrationWorkflowPauseInterceptor(
        string pauseFile
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pauseFile);

        _pauseFile = pauseFile;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (command.CommandText.Contains(MigrationCommandMarker, StringComparison.Ordinal)
            && Interlocked.Exchange(ref _pauseSignaled, 1) == 0)
        {
            // MySQL DDL implicitly commits. Pausing before the first operation
            // in Up prevents an aborted process from leaving schema changes
            // that have no corresponding migration-history record.
            await File
                .WriteAllTextAsync(
                    _pauseFile,
                    Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                    cancellationToken)
                .ConfigureAwait(false);

            // Cooperative cancellation would only test normal disposal. The
            // recovery gate must terminate the operating-system process while
            // the database session still owns the migration advisory lock.
            await Task
                .Delay(Timeout.InfiniteTimeSpan, CancellationToken.None)
                .ConfigureAwait(false);
        }

        return result;
    }
}
