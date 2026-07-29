using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Doka.EntityFrameworkCore.MySql.Examples.MigrationsWorkflow;

/// <summary>
/// Creates migration workflow contexts for EF tooling.
/// </summary>
public sealed class MigrationWorkflowDesignTimeFactory
    : IDesignTimeDbContextFactory<MigrationWorkflowContext>
{
    /// <inheritdoc />
    public MigrationWorkflowContext CreateDbContext(
        string[] args
    ) => MigrationWorkflowContextFactory.Create(enableMigrationPause: false);
}

/// <summary>
/// Centralizes runtime and design-time provider configuration for the migration
/// workflow.
/// </summary>
internal static class MigrationWorkflowContextFactory
{
    private const string ConnectionStringEnvironmentVariable = "DOKA_MIGRATION_CONNECTION_STRING";
    private const string ServerVersionEnvironmentVariable = "DOKA_MIGRATION_SERVER_VERSION";
    private const string PauseFileEnvironmentVariable = "DOKA_MIGRATION_PAUSE_FILE";
    private const string DefaultConnectionString =
        "Server=127.0.0.1;Port=33068;Database=doka_migration_workflow;"
        + "User ID=root;Password=root_password;";
    private const string DefaultServerVersion = "mysql:8.4";

    /// <summary>
    /// Creates a context from the documented migration-workflow environment.
    /// </summary>
    /// <param name="enableMigrationPause">
    /// Whether the integration-only process-abort pause may be enabled.
    /// </param>
    /// <returns>A configured migration workflow context.</returns>
    public static MigrationWorkflowContext Create(
        bool enableMigrationPause
    )
    {
        var optionsBuilder = new DbContextOptionsBuilder<MigrationWorkflowContext>();
        optionsBuilder.UseMySql(
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
            ?? DefaultConnectionString,
            MySqlServerVersion.AutoDetect(
                Environment.GetEnvironmentVariable(ServerVersionEnvironmentVariable)
                ?? DefaultServerVersion));

        var pauseFile = Environment.GetEnvironmentVariable(PauseFileEnvironmentVariable);
        if (enableMigrationPause
            && !string.IsNullOrWhiteSpace(pauseFile))
        {
            optionsBuilder.AddInterceptors(new MigrationWorkflowPauseInterceptor(pauseFile));
        }

        return new MigrationWorkflowContext(optionsBuilder.Options);
    }
}
