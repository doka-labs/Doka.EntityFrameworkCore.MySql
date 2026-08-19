using System.IO;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies that an operating-system-level migrator failure cannot strand the
/// provider migration lock or prevent a later deployment from converging.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MigrationProcessRecoveryTests
{
    private const string ConnectionStringEnvironmentVariable = "DOKA_MIGRATION_CONNECTION_STRING";
    private const string ServerVersionEnvironmentVariable = "DOKA_MIGRATION_SERVER_VERSION";
    private const string PauseFileEnvironmentVariable = "DOKA_MIGRATION_PAUSE_FILE";

    /// <summary>
    /// Verifies process-abort recovery against MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Killed_migrator_releases_lock_and_next_process_recovers_on_mysql84() =>
        VerifyProcessRecoveryAsync(IntegrationDatabaseTarget.MySql84);

    /// <summary>
    /// Verifies process-abort recovery against MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Killed_migrator_releases_lock_and_next_process_recovers_on_mysql97() =>
        VerifyProcessRecoveryAsync(IntegrationDatabaseTarget.MySql97);

    /// <summary>
    /// Verifies process-abort recovery against MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Killed_migrator_releases_lock_and_next_process_recovers_on_mariadb1011() =>
        VerifyProcessRecoveryAsync(IntegrationDatabaseTarget.MariaDb1011);

    /// <summary>
    /// Verifies process-abort recovery against MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Killed_migrator_releases_lock_and_next_process_recovers_on_mariadb114() =>
        VerifyProcessRecoveryAsync(IntegrationDatabaseTarget.MariaDb114);

    /// <summary>
    /// Verifies process-abort recovery against MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Killed_migrator_releases_lock_and_next_process_recovers_on_mariadb118() =>
        VerifyProcessRecoveryAsync(IntegrationDatabaseTarget.MariaDb118);

    /// <summary>
    /// Verifies process-abort recovery against MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Killed_migrator_releases_lock_and_next_process_recovers_on_mariadb123() =>
        VerifyProcessRecoveryAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task VerifyProcessRecoveryAsync(
        IntegrationDatabaseTarget target
    )
    {
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var databaseName = $"doka_migration_abort_{Guid.NewGuid():N}"[..45];
        var connectionString = IntegrationDatabaseUtilities.BuildConnectionString(baseConnectionString, databaseName);
        var serverVersion = IntegrationTestEnvironment.CreateRequest(target)
            .ServerVersionToken;

        var pauseFile = Path.Combine(Path.GetTempPath(), $"doka-migration-pause-{Guid.NewGuid():N}");
        var lockName = MySqlAdvisoryLockNaming.BuildLockName(connectionString);

        await IntegrationDatabaseUtilities
            .EnsureDatabaseExistsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await InterruptMigratorAsync(connectionString, serverVersion, pauseFile, lockName)
                .ConfigureAwait(false);

            await AssertLockBecomesFreeAsync(connectionString, lockName)
                .ConfigureAwait(false);

            await RunMigratorAsync(connectionString, serverVersion, "migrate")
                .ConfigureAwait(false);
            await RunMigratorAsync(connectionString, serverVersion, "migrate")
                .ConfigureAwait(false);
            await RunMigratorAsync(connectionString, serverVersion, "verify-latest")
                .ConfigureAwait(false);

            await AssertMigratedStateAsync(connectionString)
                .ConfigureAwait(false);
        }
        finally
        {
            File.Delete(pauseFile);

            await IntegrationDatabaseUtilities
                .DropDatabaseAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    private static async Task InterruptMigratorAsync(
        string connectionString,
        string serverVersion,
        string pauseFile,
        string lockName
    )
    {
        using var process = StartMigrator(connectionString, serverVersion, "migrate", pauseFile);
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        try
        {
            var pauseObserved = await WaitForPauseMarkerAsync(process, pauseFile)
                .ConfigureAwait(false);

            if (!pauseObserved)
            {
                await TerminateProcessAsync(process)
                    .ConfigureAwait(false);

                throw new InvalidOperationException(
                    "The migrator did not reach the process-abort checkpoint."
                    + Environment.NewLine
                    + $"stdout: {await standardOutput.ConfigureAwait(false)}"
                    + Environment.NewLine
                    + $"stderr: {await standardError.ConfigureAwait(false)}");
            }

            await AssertLockIsHeldAsync(connectionString, lockName)
                .ConfigureAwait(false);
            await TerminateProcessAsync(process)
                .ConfigureAwait(false);

            Assert.NotEqual(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                await TerminateProcessAsync(process)
                    .ConfigureAwait(false);
            }

            _ = await standardOutput.ConfigureAwait(false);
            _ = await standardError.ConfigureAwait(false);
        }
    }

    private static async Task RunMigratorAsync(
        string connectionString,
        string serverVersion,
        string command
    )
    {
        using var process = StartMigrator(connectionString, serverVersion, command, pauseFile: null);
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        try
        {
            await process
                .WaitForExitAsync()
                .WaitAsync(TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            await TerminateProcessAsync(process)
                .ConfigureAwait(false);

            throw new TimeoutException(
                $"Migration workflow command '{command}' did not exit within 30 seconds."
                + Environment.NewLine
                + $"stdout: {await standardOutput.ConfigureAwait(false)}"
                + Environment.NewLine
                + $"stderr: {await standardError.ConfigureAwait(false)}",
                exception);
        }

        var output = await standardOutput.ConfigureAwait(false);
        var error = await standardError.ConfigureAwait(false);

        Assert.True(
            process.ExitCode == 0,
            $"Migration workflow command '{command}' exited with {process.ExitCode}."
            + Environment.NewLine
            + $"stdout: {output}"
            + Environment.NewLine
            + $"stderr: {error}");
    }

    private static Process StartMigrator(
        string connectionString,
        string serverVersion,
        string command,
        string? pauseFile
    )
    {
        var repositoryRoot = FindRepositoryRoot();
        var executablePath = Path.Combine(
            repositoryRoot,
            "artifacts",
            "bin",
            "MigrationsWorkflow",
#if DEBUG
            "debug",
#else
            "release",
#endif
            "MigrationsWorkflow.dll");

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "The migration workflow executable was not built with the integration tests.",
                executablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(executablePath);
        startInfo.ArgumentList.Add(command);
        startInfo.Environment[ConnectionStringEnvironmentVariable] = connectionString;
        startInfo.Environment[ServerVersionEnvironmentVariable] = serverVersion;

        if (pauseFile is null)
        {
            startInfo.Environment.Remove(PauseFileEnvironmentVariable);
        }
        else
        {
            startInfo.Environment[PauseFileEnvironmentVariable] = pauseFile;
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("The migration workflow process could not be started.");
    }

    private static async Task<bool> WaitForPauseMarkerAsync(
        Process process,
        string pauseFile
    )
    {
        var timeout = Stopwatch.StartNew();

        while (timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (File.Exists(pauseFile))
            {
                return true;
            }

            if (process.HasExited)
            {
                return false;
            }

            await Task
                .Delay(TimeSpan.FromMilliseconds(50))
                .ConfigureAwait(false);
        }

        return File.Exists(pauseFile);
    }

    private static async Task TerminateProcessAsync(
        Process process
    )
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        await process
            .WaitForExitAsync()
            .WaitAsync(TimeSpan.FromSeconds(10))
            .ConfigureAwait(false);
    }

    private static async Task AssertLockIsHeldAsync(
        string connectionString,
        string lockName
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IS_USED_LOCK(@name);";
        command.Parameters.AddWithValue("@name", lockName);

        Assert.NotNull(
            await command
                .ExecuteScalarAsync()
                .ConfigureAwait(false));
    }

    private static async Task AssertLockBecomesFreeAsync(
        string connectionString,
        string lockName
    )
    {
        var timeout = Stopwatch.StartNew();

        while (timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection
                .OpenAsync()
                .ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT IS_FREE_LOCK(@name);";
            command.Parameters.AddWithValue("@name", lockName);

            var result = await command
                .ExecuteScalarAsync()
                .ConfigureAwait(false);

            if (Convert.ToInt64(result, CultureInfo.InvariantCulture) == 1L)
            {
                return;
            }

            await Task
                .Delay(TimeSpan.FromMilliseconds(50))
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            "The database server did not release the migration lock after the migrator process terminated.");
    }

    private static async Task AssertMigratedStateAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await using (var historyCommand = connection.CreateCommand())
        {
            historyCommand.CommandText = "SELECT COUNT(*) FROM `__EFMigrationsHistory`;";

            Assert.Equal(
                1L,
                Convert.ToInt64(
                    await historyCommand
                        .ExecuteScalarAsync()
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture));
        }

        await using (var dataCommand = connection.CreateCommand())
        {
            dataCommand.CommandText = "SELECT COUNT(*) FROM `MigrationWorkflowItems` "
                + "WHERE `Id` = 1 AND `Name` = 'migration-safety-readback';";

            Assert.Equal(
                1L,
                Convert.ToInt64(
                    await dataCommand
                        .ExecuteScalarAsync()
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture));
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Doka.EntityFrameworkCore.MySql.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from the integration-test output path.");
    }
}
