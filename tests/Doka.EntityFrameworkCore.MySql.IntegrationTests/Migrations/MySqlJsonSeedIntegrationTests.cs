using System.Text.Json.Nodes;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Executes provider-generated native JSON seed operations and verifies their
/// materialization against every supported server.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlJsonSeedIntegrationTests
{
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_executes_native_json_seeds() =>
        AssertNativeJsonSeedsAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_executes_native_json_seeds() =>
        AssertNativeJsonSeedsAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_executes_native_json_seeds() =>
        AssertNativeJsonSeedsAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_executes_native_json_seeds() =>
        AssertNativeJsonSeedsAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_executes_native_json_seeds() =>
        AssertNativeJsonSeedsAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_executes_native_json_seeds() =>
        AssertNativeJsonSeedsAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task AssertNativeJsonSeedsAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(target))
        {
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            Pooling = false,
        }.ConnectionString;
        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await CleanupAsync(connection).ConfigureAwait(false);

        try
        {
            foreach (var guidFormat in new[] { MySqlGuidFormat.Binary16, MySqlGuidFormat.Char36 })
            {
                await AssertNativeJsonSeedsAsync(connection, serverVersion, guidFormat).ConfigureAwait(false);
                await CleanupAsync(connection).ConfigureAwait(false);
            }
        }
        finally
        {
            await CleanupAsync(connection).ConfigureAwait(false);
        }
    }

    private static async Task AssertNativeJsonSeedsAsync(
        MySqlConnection connection,
        MySqlServerVersion serverVersion,
        MySqlGuidFormat guidFormat
    )
    {
        await using var context = new JsonSeedIntegrationContext(
            IntegrationTestDbContextOptions
                .Create<JsonSeedIntegrationContext>()
                .UseMySql(
                    connection,
                    serverVersion,
                    options => options.DefaultGuidFormat(guidFormat))
                .Options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel());
        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());
        var columns = createTable.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);

        Assert.Equal(typeof(JsonElement), columns[nameof(JsonSeedIntegrationRecord.Element)].ClrType);
        Assert.Equal(typeof(JsonDocument), columns[nameof(JsonSeedIntegrationRecord.Document)].ClrType);
        Assert.Equal(typeof(JsonNode), columns[nameof(JsonSeedIntegrationRecord.Node)].ClrType);
        Assert.Equal(typeof(JsonObject), columns[nameof(JsonSeedIntegrationRecord.ObjectValue)].ClrType);
        Assert.Equal(typeof(JsonArray), columns[nameof(JsonSeedIntegrationRecord.Array)].ClrType);

        var insert = Assert.Single(operations.OfType<InsertDataOperation>());
        Assert.All(
            insert.Columns
                .Select((column, index) => (column, index))
                .Where(item => item.column != nameof(JsonSeedIntegrationRecord.Id)),
            item => Assert.IsType<string>(insert.Values[0, item.index]));

        var generator = context.GetService<IMigrationsSqlGenerator>();
        var relationalConnection = context.GetService<IRelationalConnection>();

        foreach (var command in generator.Generate(operations, model))
        {
            _ = await command
                .ExecuteNonQueryAsync(relationalConnection, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }

        context.ChangeTracker.Clear();
        var record = await context
            .Set<JsonSeedIntegrationRecord>()
            .SingleAsync(entity => entity.Id == 1, CancellationToken.None)
            .ConfigureAwait(false);

        AssertJsonEqual(JsonSeedIntegrationContract.ElementJson, record.Element.GetRawText());
        AssertJsonEqual(JsonSeedIntegrationContract.DocumentJson, record.Document.RootElement.GetRawText());
        AssertJsonEqual(JsonSeedIntegrationContract.NodeJson, record.Node.ToJsonString());
        AssertJsonEqual(JsonSeedIntegrationContract.ObjectJson, record.ObjectValue.ToJsonString());
        AssertJsonEqual(JsonSeedIntegrationContract.ArrayJson, record.Array.ToJsonString());
    }

    private static void AssertJsonEqual(
        string expected,
        string actual
    )
    {
        var expectedNode = JsonNode.Parse(expected);
        var actualNode = JsonNode.Parse(actual);

        Assert.True(JsonNode.DeepEquals(expectedNode, actualNode));
    }

    private static async Task CleanupAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{JsonSeedIntegrationContract.Table}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

internal static class JsonSeedIntegrationContract
{
    public const string Table = "DokaJsonSeedRecords";
    public const string ElementJson = """{"kind":"element","value":1}""";
    public const string DocumentJson = """{"kind":"document","value":2}""";
    public const string NodeJson = """{"kind":"node","value":3}""";
    public const string ObjectJson = """{"kind":"object","value":4}""";
    public const string ArrayJson = """["array",5,true]""";
}

internal sealed class JsonSeedIntegrationContext : DbContext
{
    public JsonSeedIntegrationContext(
        DbContextOptions<JsonSeedIntegrationContext> options
    ) : base(options) { }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<JsonSeedIntegrationRecord>(entity =>
        {
            entity.ToTable(JsonSeedIntegrationContract.Table);
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Document).IsRequired();
            entity.Property(record => record.Node).IsRequired();
            entity.Property(record => record.ObjectValue).IsRequired();
            entity.Property(record => record.Array).IsRequired();
            entity.HasData(
                new JsonSeedIntegrationRecord
                {
                    Id = 1,
                    Element = JsonElement.Parse(JsonSeedIntegrationContract.ElementJson),
                    Document = JsonDocument.Parse(JsonSeedIntegrationContract.DocumentJson),
                    Node = JsonNode.Parse(JsonSeedIntegrationContract.NodeJson)!,
                    ObjectValue = (JsonObject)JsonNode.Parse(JsonSeedIntegrationContract.ObjectJson)!,
                    Array = (JsonArray)JsonNode.Parse(JsonSeedIntegrationContract.ArrayJson)!,
                });
        });
    }
}

internal sealed class JsonSeedIntegrationRecord
{
    public int Id { get; set; }

    public JsonElement Element { get; set; }

    public JsonDocument Document { get; set; } = null!;

    public JsonNode Node { get; set; } = null!;

    public JsonObject ObjectValue { get; set; } = null!;

    public JsonArray Array { get; set; } = null!;
}
