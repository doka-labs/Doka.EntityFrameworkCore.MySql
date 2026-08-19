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
    /// <summary>
    /// Executes one supported command.
    /// </summary>
    /// <param name="args">Command-line arguments. The default command is <c>migrate</c>.</param>
    /// <returns>Zero when the selected operation succeeds.</returns>
    public static async Task<int> RunAsync(
        string[] args
    )
    {
        ArgumentNullException.ThrowIfNull(args);

        var command = args.Length == 0
            ? "migrate"
            : args[0];

        return command switch
        {
            "migrate" => await MigrateAsync().ConfigureAwait(false),
            "migrate-direct" => await MigrateDirectAsync().ConfigureAwait(false),
            "verify-latest" => await VerifyLatestAsync().ConfigureAwait(false),
            "verify-rolled-back" => await VerifyRolledBackAsync().ConfigureAwait(false),
            _ => throw new ArgumentException(
                $"Unsupported migration workflow command '{command}'. "
                + "Supported commands are: migrate, migrate-direct, verify-latest, verify-rolled-back.",
                nameof(args)),
        };
    }

    private static async Task<int> MigrateAsync()
    {
        await using var context = MigrationWorkflowContextFactory.Create(
            enableMigrationPause: true);

        await context.Database
            .MigrateAsync()
            .ConfigureAwait(false);

        Console.WriteLine("Migration workflow database is up to date.");
        return 0;
    }

    private static async Task<int> MigrateDirectAsync()
    {
        await using var context = MigrationWorkflowContextFactory.Create(
            enableMigrationPause: false);

        // Database.MigrateAsync delegates to IMigrator. Exercising the service
        // directly keeps its public provider contract independently visible.
        await context
            .GetService<IMigrator>()
            .MigrateAsync()
            .ConfigureAwait(false);

        Console.WriteLine("Direct IMigrator workflow is up to date.");
        return 0;
    }

    private static async Task<int> VerifyLatestAsync()
    {
        await using var context = MigrationWorkflowContextFactory.Create(
            enableMigrationPause: false);

        var appliedMigrations = (await context.Database
            .GetAppliedMigrationsAsync()
            .ConfigureAwait(false))
            .ToArray();

        var pendingMigrations = (await context.Database
            .GetPendingMigrationsAsync()
            .ConfigureAwait(false))
            .ToArray();

        var seedRowExists = await context.Items
            .AnyAsync()
            .ConfigureAwait(false);

        var handlerEvidenceTableCount = await CountApplicationTablesAsync(
                context,
                MigrationWorkflowOperationHandlerExtensions.EvidenceTableName,
                MigrationWorkflowOperationHandlerExtensions.EvidenceTableName)
            .ConfigureAwait(false);

        if (appliedMigrations.Length != 1
            || pendingMigrations.Length != 0
            || !seedRowExists
            || handlerEvidenceTableCount != 1)
        {
            throw new InvalidOperationException(
                "Migration workflow readback did not observe the expected latest state. "
                + $"Applied migrations: [{string.Join(", ", appliedMigrations)}]; "
                + $"pending migrations: [{string.Join(", ", pendingMigrations)}]; "
                + $"seed row exists: {seedRowExists}; "
                + $"handler evidence tables: {handlerEvidenceTableCount}.");
        }

        Console.WriteLine("Migration workflow readback verified the latest schema.");
        return 0;
    }

    private static async Task<int> VerifyRolledBackAsync()
    {
        await using var context = MigrationWorkflowContextFactory.Create(
            enableMigrationPause: false);

        var appliedMigrations = await context.Database
            .GetAppliedMigrationsAsync()
            .ConfigureAwait(false);

        var applicationTableCount = await CountApplicationTablesAsync(
                context,
                "MigrationWorkflowItems",
                MigrationWorkflowOperationHandlerExtensions.EvidenceTableName)
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

    private static Task<long> CountApplicationTablesAsync(
        MigrationWorkflowContext context,
        string firstTableName,
        string secondTableName
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
            .SingleAsync();
    }
}
