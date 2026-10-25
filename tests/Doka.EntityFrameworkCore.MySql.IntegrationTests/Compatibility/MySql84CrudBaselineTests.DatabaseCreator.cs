namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySql84CrudBaselineTests
{
    /// <summary>Verifies the selected database-existence API returns false for a missing database.</summary>
    /// <param name="useAsync">Whether to exercise the asynchronous database-creator API.</param>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MySql84 })]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Database_creator_exists_returns_false_for_missing_database(
        bool useAsync
    )
    {
        // Arrange
        var missingDatabaseBuilder = new MySqlConnectionStringBuilder(GetConnectionString())
        {
            Database = "doka_provider_missing_phase1_exists_check",
        };

        await using var context = new MySql84CrudContext(CreateOptions(missingDatabaseBuilder.ConnectionString));
        var creator = context.GetService<IRelationalDatabaseCreator>();

        // Act
        bool exists;

        if (useAsync)
        {
            exists = await creator.ExistsAsync().ConfigureAwait(false);
        }
        else
        {
            // WHY: This theory case independently verifies the synchronous API.
            // ReSharper disable once MethodHasAsyncOverload
            exists = creator.Exists();
        }

        // Assert
        Assert.False(exists);
    }

    /// <summary>Verifies the selected database-existence API preserves real access failures.</summary>
    /// <param name="useAsync">Whether to exercise the asynchronous database-creator API.</param>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MySql84 })]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Database_creator_exists_surfaces_access_denied(
        bool useAsync
    )
    {
        // Arrange
        var invalidCredentialsBuilder = new MySqlConnectionStringBuilder(GetConnectionString())
        {
            Password = "definitely_wrong_password",
        };

        await using var context = new MySql84CrudContext(CreateOptions(invalidCredentialsBuilder.ConnectionString));
        var creator = context.GetService<IRelationalDatabaseCreator>();

        // Act
        MySqlException exception;

        if (useAsync)
        {
            exception = await Assert.ThrowsAsync<MySqlException>(() => creator.ExistsAsync())
                .ConfigureAwait(false);
        }
        else
        {
            // WHY: This theory case independently verifies the synchronous API.
            // ReSharper disable once MethodHasAsyncOverload
            exception = Assert.Throws<MySqlException>(() => creator.Exists());
        }

        // Assert
        Assert.NotEqual(MySqlErrorCode.NoSuchDb, exception.ErrorCode);
    }
}
