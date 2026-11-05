using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Doka.EntityFrameworkCore.MySql.Examples.MigrationsWorkflow;

/// <summary>
/// Executes the runtime migration and readback paths used by the example and
/// migration-safety gates.
/// </summary>
internal static class MigrationWorkflowCommand
{
    private static readonly string[] s_expectedMigrations =
    [
        "20260728100854_InitialMigration",
        "20260820120000_AddTemporalDefaults",
        "20260820121000_UpdateTemporalDefaults",
    ];

    /// <summary>
    /// Executes one supported command.
    /// </summary>
    /// <param name="args">Command-line arguments. The default command is <c>migrate</c>.</param>
    /// <param name="cancellationToken">The token to observe while executing the selected command.</param>
    /// <returns>Zero when the selected operation succeeds.</returns>
    public static async Task<int> RunAsync(
        string[] args,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(args);
        cancellationToken.ThrowIfCancellationRequested();

        var command = args.Length == 0
            ? "migrate"
            : args[0];

        return command switch
        {
            "migrate" => await MigrateAsync(cancellationToken).ConfigureAwait(false),
            "migrate-direct" => await MigrateDirectAsync(cancellationToken).ConfigureAwait(false),
            "verify-latest" => await VerifyLatestAsync(cancellationToken).ConfigureAwait(false),
            "verify-rolled-back" => await VerifyRolledBackAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException(
                $"Unsupported migration workflow command '{command}'. "
                + "Supported commands are: migrate, migrate-direct, verify-latest, verify-rolled-back.",
                nameof(args)),
        };
    }

    /// <summary>
    /// Applies pending migrations through the database facade.
    /// </summary>
    /// <param name="cancellationToken">The token to observe while applying migrations.</param>
    /// <returns>Zero after the database reaches the current model.</returns>
    private static async Task<int> MigrateAsync(
        CancellationToken cancellationToken
    )
    {
        await using var context = MigrationWorkflowContextFactory.Create(
            enableMigrationPause: true);

        await context.Database
            .MigrateAsync(cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine("Migration workflow database is up to date.");

        return 0;
    }

    /// <summary>
    /// Applies pending migrations through the provider's migrator service.
    /// </summary>
    /// <param name="cancellationToken">The token to observe while applying migrations.</param>
    /// <returns>Zero after the database reaches the current model.</returns>
    private static async Task<int> MigrateDirectAsync(
        CancellationToken cancellationToken
    )
    {
        await using var context = MigrationWorkflowContextFactory.Create(
            enableMigrationPause: false);

        // Database.MigrateAsync delegates to IMigrator. Exercising the service
        // directly keeps its public provider contract independently visible.
        await context
            .GetService<IMigrator>()
            .MigrateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine("Direct IMigrator workflow is up to date.");

        return 0;
    }

    /// <summary>
    /// Verifies migration history, seeded values and handler evidence after migration.
    /// </summary>
    /// <param name="cancellationToken">The token to observe while reading the database state.</param>
    /// <returns>Zero when the complete latest-state contract is observed.</returns>
    private static async Task<int> VerifyLatestAsync(
        CancellationToken cancellationToken
    )
    {
        await using var context = MigrationWorkflowContextFactory.Create(
            enableMigrationPause: false);

        var appliedMigrations = (await context.Database
            .GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToArray();

        var pendingMigrations = (await context.Database
            .GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToArray();

        var items = await context.Items
            .OrderBy(item => item.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var handlerEvidenceTableCount = await CountApplicationTablesAsync(
                context,
                MigrationWorkflowOperationHandlerExtensions.EvidenceTableName,
                MigrationWorkflowOperationHandlerExtensions.EvidenceTableName,
                cancellationToken)
            .ConfigureAwait(false);

        if (!appliedMigrations.SequenceEqual(s_expectedMigrations, StringComparer.Ordinal)
            || pendingMigrations.Length != 0
            || !HasExpectedDefaultedRows(items)
            || handlerEvidenceTableCount != 1)
        {
            throw new InvalidOperationException(
                "Migration workflow readback did not observe the expected latest state. "
                + $"Applied migrations: [{string.Join(", ", appliedMigrations)}]; "
                + $"pending migrations: [{string.Join(", ", pendingMigrations)}]; "
                + $"defaulted rows valid: {HasExpectedDefaultedRows(items)}; "
                + $"handler evidence tables: {handlerEvidenceTableCount}.");
        }

        Console.WriteLine("Migration workflow readback verified the latest schema.");

        return 0;
    }

    private static bool HasExpectedDefaultedRows(
        MigrationWorkflowItem[] items
    )
    {
        if (items.Length != 3)
        {
            return false;
        }

        var initialDate = new DateOnly(2026, 8, 17);
        var initialTime = new TimeOnly(12, 34, 56, 123, 456);
        var updatedDate = new DateOnly(2028, 2, 3);
        var updatedTime = new TimeOnly(4, 5, 6, 654, 321);
        var repairedTimestamp = new DateTime(2026, 8, 21, 12, 34, 56);

        return items[0].Id == 1
            && items[0].Name == "migration-safety-readback"
            && items[0].EffectiveDate == initialDate
            && items[0].EffectiveTime == initialTime
            && items[0].OccurredAt == repairedTimestamp
            && items[1].Id == 2
            && items[1].Name == "added-default-readback"
            && items[1].EffectiveDate == initialDate
            && items[1].EffectiveTime == initialTime
            && items[1].OccurredAt == repairedTimestamp
            && items[2].Id == 3
            && items[2].Name == "altered-default-readback"
            && items[2].EffectiveDate == updatedDate
            && items[2].EffectiveTime == updatedTime
            && items[2].OccurredAt == repairedTimestamp;
    }

    /// <summary>
    /// Verifies rollback removed both migration history and example-owned tables.
    /// </summary>
    /// <param name="cancellationToken">The token to observe while reading the rolled-back state.</param>
    /// <returns>Zero when no applied migration or example-owned table remains.</returns>
    private static async Task<int> VerifyRolledBackAsync(
        CancellationToken cancellationToken
    )
    {
        await using var context = MigrationWorkflowContextFactory.Create(
            enableMigrationPause: false);

        var appliedMigrations = await context.Database
            .GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        var applicationTableCount = await CountApplicationTablesAsync(
                context,
                "MigrationWorkflowItems",
                MigrationWorkflowOperationHandlerExtensions.EvidenceTableName,
                cancellationToken)
            .ConfigureAwait(false);

        if (appliedMigrations.Any()
            || applicationTableCount != 0)
        {
            throw new InvalidOperationException(
                "Migration workflow rollback left an applied migration or application table behind.");
        }

        Console.WriteLine("Migration workflow readback verified the rolled-back schema.");

        return 0;
    }

    /// <summary>
    /// Counts the named application tables independently of provider SQL generation.
    /// </summary>
    /// <param name="context">The context selecting the database to inspect.</param>
    /// <param name="firstTableName">The first expected application table.</param>
    /// <param name="secondTableName">The second expected application table.</param>
    /// <param name="cancellationToken">The token to observe while reading the table metadata.</param>
    /// <returns>The number of matching tables in the selected database.</returns>
    private static Task<long> CountApplicationTablesAsync(
        MigrationWorkflowContext context,
        string firstTableName,
        string secondTableName,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstTableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(secondTableName);

        // The names are repository-owned constants. Keeping this readback
        // query separate from provider SQL generation makes it an independent
        // observation that the custom handler actually ran.
        return context.Database
            .SqlQueryRaw<long>(
                "SELECT COUNT(*) AS `Value` "
                + "FROM `information_schema`.`tables` "
                + "WHERE `table_schema` = DATABASE() "
                + "AND `table_name` IN ({0}, {1})",
                firstTableName,
                secondTableName)
            .SingleAsync(cancellationToken);
    }
}
