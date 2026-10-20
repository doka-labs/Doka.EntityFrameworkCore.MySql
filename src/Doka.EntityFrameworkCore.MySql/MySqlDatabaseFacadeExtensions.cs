namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Exposes the configured MySQL-compatible server family through the database facade.
/// </summary>
public static class MySqlDatabaseFacadeExtensions
{
    /// <summary>
    /// Gets whether the context is configured to target MariaDB rather than MySQL.
    /// </summary>
    /// <param name="database">The database facade of a context configured with the Doka MySQL provider.</param>
    /// <returns>
    /// <see langword="true"/> for a configured MariaDB target; <see langword="false"/> for a configured MySQL target.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="database"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The context does not configure the Doka MySQL provider or has no configured server version.
    /// </exception>
    /// <remarks>
    /// This synchronous configuration query performs no database I/O or server-version autodetection.
    /// It reads the server version configured by <c>UseMySql</c>, independently of supplied or compiled model
    /// annotations. It does not verify that a reachable server matches that configuration.
    /// </remarks>
    public static bool IsMariaDb(
        this DatabaseFacade database
    )
    {
        ArgumentNullException.ThrowIfNull(database);

        // WHY: The configured options remain authoritative when an application supplies or compiles its model.
        // Reading a model annotation would make this configuration query depend on model-generation choices.
        var extension = database
                .GetService<IDbContextOptions>()
                .FindExtension<MySqlOptionsExtension>()
            ?? throw new InvalidOperationException(
                "Database.IsMariaDb requires the Doka MySQL provider configured with UseMySql.");

        var serverVersion = extension.ServerVersion
            ?? throw new InvalidOperationException("A MySQL server version must be configured.");

        return serverVersion.IsMariaDb;
    }
}
