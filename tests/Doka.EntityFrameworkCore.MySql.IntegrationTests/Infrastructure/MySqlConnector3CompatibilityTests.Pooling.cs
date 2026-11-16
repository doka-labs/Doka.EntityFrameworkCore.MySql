namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlConnector3CompatibilityTests
{
    /// <summary>Separates callers who request session reset from those who explicitly retain session state.</summary>
    public static TheoryData<string, bool> DatabaseResetCases
    {
        get
        {
            var cases = new TheoryData<string, bool>();
            foreach (var target in GetSelectedSupportedTargets())
            {
                cases.Add(target.ToString(), false);
                cases.Add(target.ToString(), true);
            }

            return cases;
        }
    }

    /// <summary>Restores the configured database while preserving the qualified session-reset behavior.</summary>
    /// <param name="target">The selected native database engine.</param>
    /// <param name="connectionReset">Whether the caller requests reset when a pooled connection is reused.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(DatabaseResetCases))]
    public async Task Changed_database_pool_reuse_preserves_the_qualified_session_contract(
        string target,
        bool connectionReset
    )
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var baseConnectionString = CreateConnectionString(target);
        var alternateDatabase = $"doka_connector3_pool_{Guid.NewGuid():N}";
        var settings = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = 1,
            ConnectionReset = connectionReset,
            ApplicationName = $"doka-connector3-{Guid.NewGuid():N}",
        };

        await using var admin = new MySqlConnection(baseConnectionString);
        await admin.OpenAsync(cancellationToken);
        await ExecuteAsync(admin, $"CREATE DATABASE `{alternateDatabase}`;", cancellationToken);
        await using var first = new MySqlConnection(settings.ConnectionString);

        try
        {
            await first.OpenAsync(cancellationToken);
            var physicalThread = first.ServerThread;
            await ExecuteAsync(first, "SET @doka_connector3_marker = 41;", cancellationToken);
            await first.ChangeDatabaseAsync(alternateDatabase, cancellationToken);

            // Act
            await first.CloseAsync();
            await using var reused = new MySqlConnection(settings.ConnectionString);
            await reused.OpenAsync(cancellationToken);
            var database = await ScalarAsync(reused, "SELECT DATABASE();", cancellationToken);
            var marker = await ScalarAsync(reused, "SELECT @doka_connector3_marker;", cancellationToken);

            // Assert
            Assert.Equal(physicalThread, reused.ServerThread);
            Assert.Equal(settings.Database, Assert.IsType<string>(database));
            if (connectionReset || !UsesConnector3)
            {
                // WHY: Connector 2 reauthenticates after a database change even
                // with reset disabled; 3 retains the explicitly requested session state.
                Assert.Equal(DBNull.Value, marker);
            }
            else
            {
                Assert.Equal(41, Convert.ToInt32(marker, CultureInfo.InvariantCulture));
            }
        }
        finally
        {
            // WHY: Cleanup owns only this unique pool/schema and must finish
            // even after an assertion or command fails.
            try
            {
                await first.CloseAsync();
            }
            finally
            {
                try
                {
                    await MySqlConnection.ClearPoolAsync(first, CancellationToken.None);
                }
                finally
                {
                    await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS `{alternateDatabase}`;", CancellationToken.None);
                }
            }
        }
    }
}
