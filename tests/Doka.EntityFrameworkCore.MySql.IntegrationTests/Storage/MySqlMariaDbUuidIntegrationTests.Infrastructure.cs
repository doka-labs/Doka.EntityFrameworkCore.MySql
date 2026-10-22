namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Centralizes UUID schema ownership, model creation, catalog reads, and teardown.</summary>
public sealed partial class MySqlMariaDbUuidIntegrationTests
{
    /// <summary>Uses one stable schema owner for every partial participating in a dataset.</summary>
    private static string GetConnectionString(
        IntegrationDatabaseTarget target,
        string purpose
    )
    {
        // WHY: Discovery, seeding, queries, and cleanup live in different partial
        // files; an explicit owner prevents CallerFilePath from separating their schemas.
        return IntegrationTestEnvironment.GetConnectionString(
            target,
            $"{nameof(MySqlMariaDbUuidIntegrationTests)}.{purpose}");
    }

    private static string CreateNativeConnectionString(
        IntegrationDatabaseTarget target,
        string purpose
    ) => new MySqlConnectionStringBuilder(GetConnectionString(target, purpose))
    {
        GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
        Pooling = false,
    }.ConnectionString;

    /// <summary>Creates a context in either the shared read-only or isolated lifecycle schema.</summary>
    private static NativeUuidContext CreateNativeContext(
        IntegrationDatabaseTarget target,
        string purpose
    ) => new(
        IntegrationTestDbContextOptions.Create<NativeUuidContext>()
            .UseMySql(
                CreateNativeConnectionString(target, purpose),
                IntegrationTestEnvironment.GetServerVersion(target),
                options => options.DefaultGuidFormat(MySqlGuidFormat.Uuid))
            .Options);

    /// <summary>Creates a context over the class-owned versioned UUID dataset.</summary>
    private static VersionedUuidContext CreateVersionedContext(
        IntegrationDatabaseTarget target
    ) => new(
        IntegrationTestDbContextOptions.Create<VersionedUuidContext>()
            .UseMySql(
                CreateNativeConnectionString(target, nameof(UuidDataSet.Versioned)),
                IntegrationTestEnvironment.GetServerVersion(target),
                options => options.DefaultGuidFormat(MySqlGuidFormat.Uuid))
            .Options);

    /// <summary>Executes the model differ and SQL generator pipeline, including model-managed UUID data.</summary>
    private static async Task CreateNativeModelAsync(
        NativeUuidContext context
    )
    {
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel());

        var generator = context.GetService<IMigrationsSqlGenerator>();
        var relationalConnection = context.GetService<IRelationalConnection>();

        foreach (var command in generator.Generate(operations, model))
        {
            _ = await command.ExecuteNonQueryAsync(
                relationalConnection,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
    }


    private static async Task<Dictionary<string, string>> ReadOptInColumnTypesAsync(
        NativeUuidOptInContext context
    )
    {
        var connection = (MySqlConnection)context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_NAME, DATA_TYPE FROM information_schema.COLUMNS "
            + "WHERE TABLE_SCHEMA = DATABASE() "
            + $"AND TABLE_NAME = '{OptInTable}' AND COLUMN_NAME IN ('NativeId', 'BinaryId');";
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var reader = await command
            .ExecuteReaderAsync(CancellationToken.None)
            .ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            result.Add(reader.GetString(0), reader.GetString(1));
        }

        return result;
    }

    private static async Task<Dictionary<string, string>> ReadColumnTypesAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_NAME, DATA_TYPE FROM information_schema.COLUMNS "
            + "WHERE TABLE_SCHEMA = DATABASE() AND "
            + $"((TABLE_NAME = '{PrincipalTable}' AND COLUMN_NAME IN "
            + "('Id', 'OptionalNativeId', 'BinaryId', 'CharId', 'ConvertedId')) OR "
            + $"(TABLE_NAME = '{DependentTable}' AND COLUMN_NAME = 'PrincipalId'));";
        await using var reader = await command
            .ExecuteReaderAsync(CancellationToken.None)
            .ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            result.Add(reader.GetString(0), reader.GetString(1));
        }

        return result;
    }

    private static async Task DropModelTablesAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{DependentTable}`; "
            + $"DROP TABLE IF EXISTS `{PrincipalTable}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task DropProbeTableAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{ProbeTable}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task DropVersionedTableAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{VersionedTable}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
