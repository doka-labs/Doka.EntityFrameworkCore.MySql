namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlQueryTranslationIntegrationTests
{
    /// <summary>Verifies one JSON metric against a real persisted document.</summary>
    /// <param name="method">The independently reported JSON metric.</param>
    /// <param name="expected">The expected document metric.</param>
    [RequiresDatabaseTargetsTheory(IntegrationDatabaseTarget.MySql84)]
    [InlineData("Depth", 3)]
    [InlineData("Length", 2)]
    public async Task Json_metric_returns_expected_value(
        string method,
        int expected
    )
    {
        // Arrange
        const IntegrationDatabaseTarget target = IntegrationDatabaseTarget.MySql84;
        var connectionString = GetConnectionString(target);
        await using var context = new JsonQueryContext(CreateJsonOptions(connectionString, target));

        await CleanupJsonAsync(context);

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS `IntJsonItems` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `Data` json NOT NULL,
                    CONSTRAINT `PK_IntJsonItems` PRIMARY KEY (`Id`)
                ) CHARACTER SET utf8mb4;
                """);

            var jsonValue = """{"a":1,"b":{"c":2}}""";

            await context.Database.ExecuteSqlRawAsync("INSERT INTO `IntJsonItems` (`Data`) VALUES ({0});", jsonValue);

            var query = method switch
            {
                "Depth" => context.Items.Select(item => EF.Functions.JsonDepth(item.Data)),
                "Length" => context.Items.Select(item => EF.Functions.JsonLength(item.Data)),
                _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown JSON metric."),
            };

            // Act
            var result = await query.FirstAsync();

            // Assert
            Assert.Equal(expected, result);
        }
        finally
        {
            await CleanupJsonAsync(context);
        }
    }

    /// <summary>Verifies constructor result types within a single server projection.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Json_constructors_materialize_server_results_on_mysql84() =>
        RunJsonConstructorTest(IntegrationDatabaseTarget.MySql84);

    /// <summary>Verifies constructor result types within a single server projection.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Json_constructors_materialize_server_results_on_mariadb118() =>
        RunJsonConstructorTest(IntegrationDatabaseTarget.MariaDb118);

    private static async Task RunJsonConstructorTest(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        var connectionString = GetConnectionString(target);
        var interceptor = new CommandCaptureInterceptor();
        await using var context = new JsonQueryContext(CreateJsonOptions(connectionString, target, interceptor));
        await CleanupJsonAsync(context);

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS `IntJsonItems` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `Data` json NOT NULL,
                    CONSTRAINT `PK_IntJsonItems` PRIMARY KEY (`Id`)
                ) CHARACTER SET utf8mb4;
                """);
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO `IntJsonItems` (`Data`) VALUES ({0});",
                "{}");

            var suffix = "tail";

            // WHY: One projection checks that array and object constructors keep
            // their independent result mappings through the same materializer.
            var query = context
                .Items.Select(item => new
                {
                    Array = EF.Functions.JsonArray(item.Id, suffix, null),
                    Object = EF.Functions.JsonObject("id", item.Id, "suffix", suffix, "missing", null),
                });

            // Act
            var sql = query.ToQueryString();
            var result = await query.SingleAsync();

            // Assert
            Assert.Contains("JSON_ARRAY(", sql, StringComparison.Ordinal);
            Assert.Contains("JSON_OBJECT(", sql, StringComparison.Ordinal);
            Assert.Contains(
                interceptor.CommandTexts,
                commandText => commandText.Contains("JSON_ARRAY(", StringComparison.Ordinal)
                    && commandText.Contains("JSON_OBJECT(", StringComparison.Ordinal));

            using var array = JsonDocument.Parse(result.Array);

            Assert.Equal(1, array.RootElement[0].GetInt32());
            Assert.Equal(suffix, array.RootElement[1].GetString());
            Assert.Equal(JsonValueKind.Null, array.RootElement[2].ValueKind);

            using var jsonObject = JsonDocument.Parse(result.Object);

            Assert.Equal(1, jsonObject.RootElement.GetProperty("id").GetInt32());
            Assert.Equal(suffix, jsonObject.RootElement.GetProperty("suffix").GetString());
            Assert.Equal(JsonValueKind.Null, jsonObject.RootElement.GetProperty("missing").ValueKind);
        }
        finally
        {
            await CleanupJsonAsync(context);
        }
    }
}
