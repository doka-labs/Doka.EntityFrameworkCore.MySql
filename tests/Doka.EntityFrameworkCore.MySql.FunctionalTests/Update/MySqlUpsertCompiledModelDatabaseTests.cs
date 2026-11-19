namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Executes public UPSERT using the actual generated C# runtime model on the active specification engine.</summary>
[Trait("Category", "Live")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class MySqlUpsertCompiledModelDatabaseTests
{
    /// <summary>The compiled model preserves native generated defaults and converted values without model rebuilding.</summary>
    [Fact]
    public async Task Generated_compiled_model_executes_native_upsert_and_reads_back_exact_values()
    {
        // Arrange
        using var safety = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = safety.Token;
        var version = MySqlTestEnvironment.ServerVersion;
        var database = "DokaCompiledUpsert_"
            + Guid
                .NewGuid()
                .ToString("N", CultureInfo.InvariantCulture);

        var builder = new MySqlConnectionStringBuilder(MySqlTestEnvironment.ConnectionString)
        {
            Database = string.Empty,
            Pooling = false,
            AutoEnlist = false,
        };

        await using var admin = new MySqlConnection(builder.ConnectionString);
        await admin.OpenAsync(token);
        await using var create =
            new MySqlCommand($"CREATE DATABASE `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;", admin);

        await create.ExecuteNonQueryAsync(token);
        builder.Database = database;
        Exception? primary = null;

        try
        {
            await using var original = MySqlUpsertCompiledModelTests.CreateContext(null,
                rejectRebuild: false,
                concurrency: false,
                builder.ConnectionString,
                version);

            var model = MySqlUpsertCompiledModelTests.CompileModel(original);
            await original.Database.ExecuteSqlRawAsync(original.Database.GenerateCreateScript(), token);
            await using var target = MySqlUpsertCompiledModelTests.CreateContext(model,
                rejectRebuild: true,
                concurrency: false,
                builder.ConnectionString,
                version);

            var input = new MySqlUpsertCompiledModelTests.ModelRow
            {
                Id = 1,
                Value = "Hallo 'Test' \\ \u4e2d",
                ClientGuid = new Guid("00112233-4455-6677-8899-aabbccddeeff"),
            };

            // Act
            var affected = await target.ExecuteUpsertAsync(input,
                new[] { nameof(MySqlUpsertCompiledModelTests.ModelRow.Value) },
                token);

            var persisted = await target
                .Set<MySqlUpsertCompiledModelTests.ModelRow>()
                .AsNoTracking()
                .SingleAsync(token);

            // Assert
            Assert.Same(model, target.Model);
            Assert.Equal(0, target.ModelBuilds);
            Assert.Equal(1, affected);
            Assert.Equal(input.Value, persisted.Value);
            Assert.Equal(input.ClientGuid, persisted.ClientGuid);
            Assert.NotEqual(Guid.Empty, persisted.GeneratedGuid);
            Assert.Equal(2, persisted.Computed);
            Assert.Equal(Guid.Empty, input.GeneratedGuid);
            Assert.Equal(0, input.Computed);
            Assert.Empty(target.ChangeTracker.Entries());
        }
        catch (Exception error)
        {
            primary = error;
        }

        try
        {
            // WHY: This test owns the unique schema. Cleanup runs after forward work so a
            // cleanup failure can retain the original cause without throwing from a finally block.
            await using var drop = new MySqlCommand($"DROP DATABASE IF EXISTS `{database}`;", admin);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (Exception cleanup) when (primary is not null)
        {
            throw new AggregateException("Compiled UPSERT qualification and owned-schema cleanup both failed.",
                primary,
                cleanup);
        }

        if (primary is not null)
        {
            ExceptionDispatchInfo
                .Capture(primary)
                .Throw();
        }
    }
}
