using System.Text.Json.Nodes;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Executes provider-generated native JSON seed operations and verifies their
/// materialization against every supported server.
/// </summary>
[IntegrationDatabaseTest]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlJsonSeedIntegrationTests
{
    /// <summary>
    /// Verifies my sql84 executes native JSON seeds.
    /// </summary>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MySql84 })]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public Task MySql84_executes_native_json_seeds(
        MySqlGuidFormat guidFormat
    ) => AssertNativeJsonSeedsAsync(IntegrationDatabaseTarget.MySql84, guidFormat);

    /// <summary>
    /// Verifies my sql97 executes native JSON seeds.
    /// </summary>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MySql97 })]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public Task MySql97_executes_native_json_seeds(
        MySqlGuidFormat guidFormat
    ) => AssertNativeJsonSeedsAsync(IntegrationDatabaseTarget.MySql97, guidFormat);

    /// <summary>
    /// Verifies maria db1011 executes native JSON seeds.
    /// </summary>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MariaDb1011 })]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public Task MariaDb1011_executes_native_json_seeds(
        MySqlGuidFormat guidFormat
    ) => AssertNativeJsonSeedsAsync(IntegrationDatabaseTarget.MariaDb1011, guidFormat);

    /// <summary>
    /// Verifies maria db114 executes native JSON seeds.
    /// </summary>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MariaDb114 })]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public Task MariaDb114_executes_native_json_seeds(
        MySqlGuidFormat guidFormat
    ) => AssertNativeJsonSeedsAsync(IntegrationDatabaseTarget.MariaDb114, guidFormat);

    /// <summary>
    /// Verifies maria db118 executes native JSON seeds.
    /// </summary>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MariaDb118 })]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public Task MariaDb118_executes_native_json_seeds(
        MySqlGuidFormat guidFormat
    ) => AssertNativeJsonSeedsAsync(IntegrationDatabaseTarget.MariaDb118, guidFormat);

    /// <summary>
    /// Verifies maria db123 executes native JSON seeds.
    /// </summary>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MariaDb123 })]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public Task MariaDb123_executes_native_json_seeds(
        MySqlGuidFormat guidFormat
    ) => AssertNativeJsonSeedsAsync(IntegrationDatabaseTarget.MariaDb123, guidFormat);

    private static async Task AssertNativeJsonSeedsAsync(
        IntegrationDatabaseTarget target,
        MySqlGuidFormat guidFormat
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
            await AssertNativeJsonSeedsAsync(connection, serverVersion, guidFormat).ConfigureAwait(false);
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
        // Arrange
        await using var context = new JsonSeedIntegrationContext(
            IntegrationTestDbContextOptions
                .Create<JsonSeedIntegrationContext>()
                .UseMySql(
                    connection,
                    serverVersion,
                    options => options.DefaultGuidFormat(guidFormat))
                .Options);

        // Act
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel());

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

        // Assert
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
        // Arrange
        var expectedNode = JsonNode.Parse(expected);

        // Act
        var actualNode = JsonNode.Parse(actual);

        // Assert
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
    /// <summary>
    /// The table used by this migration fixture.
    /// </summary>
    public const string Table = "DokaJsonSeedRecords";

    /// <summary>
    /// The element JSON used by this migration fixture.
    /// </summary>
    public const string ElementJson = """{"kind":"element","value":"it's here"}""";

    /// <summary>
    /// The document JSON used by this migration fixture.
    /// </summary>
    public const string DocumentJson = """{"kind":"document","value":"C:\\data"}""";

    /// <summary>
    /// The node JSON used by this migration fixture.
    /// </summary>
    public const string NodeJson = """{"kind":"node","value":"quoted \"value\""}""";

    /// <summary>
    /// The object JSON used by this migration fixture.
    /// </summary>
    public const string ObjectJson = "{\"kind\":\"object\",\"value\":\"gr\u00FCn\"}";

    /// <summary>
    /// The array JSON used by this migration fixture.
    /// </summary>
    public const string ArrayJson = """["array","line\nbreak","it's here"]""";
}

internal sealed class JsonSeedIntegrationContext : DbContext
{
    /// <summary>
    /// Initializes the JsonSeedIntegrationContext fixture.
    /// </summary>
    public JsonSeedIntegrationContext(
        DbContextOptions<JsonSeedIntegrationContext> options
    ) : base(options) { }

    /// <inheritdoc />
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
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the element.
    /// </summary>
    public JsonElement Element { get; set; }

    /// <summary>
    /// Gets or sets the document.
    /// </summary>
    public JsonDocument Document { get; set; } = null!;

    /// <summary>
    /// Gets or sets the node.
    /// </summary>
    public JsonNode Node { get; set; } = null!;

    /// <summary>
    /// Gets or sets the object value.
    /// </summary>
    public JsonObject ObjectValue { get; set; } = null!;

    /// <summary>
    /// Gets or sets the array.
    /// </summary>
    public JsonArray Array { get; set; } = null!;
}
