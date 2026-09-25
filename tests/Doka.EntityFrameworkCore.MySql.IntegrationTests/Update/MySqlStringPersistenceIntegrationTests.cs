namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies that string writes preserve their exact bytes across the supported
/// database engines and SQL modes.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlStringPersistenceIntegrationTests
{
    private const string TableName = "DokaStringRoundTrip";
    private const string MixedQuoteValue = "Hallo 'Test' und \"Test\"";

    /// <summary>
    /// Provides independently reported quote cases under both SQL modes.
    /// </summary>
    public static TheoryData<bool, string> QuoteCases => CreateCases(
        "Hallo 'Test'",
        "Hallo \"Test\"",
        MixedQuoteValue);

    /// <summary>
    /// Provides independently reported escaping and UTF-8 cases under both SQL modes.
    /// </summary>
    public static TheoryData<bool, string> SpecialCases => CreateCases(
        "path\\segment",
        "line one\nline two",
        "gr\u00FCn \U0001F680",
        "before\0after",
        string.Empty);

    /// <summary>
    /// Checks that parameterized entity inserts store each quote variant byte-for-byte.
    /// </summary>
    [Theory]
    [MemberData(nameof(QuoteCases))]
    public async Task Insert_preserves_single_and_double_quotes(bool noBackslashEscapes, string value)
    {
        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            await AssertInsertPreservesValueAsync(target, noBackslashEscapes, value);
        }
    }

    /// <summary>
    /// Checks that parameterized entity updates do not substitute one quote kind for another.
    /// </summary>
    [Theory]
    [MemberData(nameof(QuoteCases))]
    public async Task Update_preserves_single_and_double_quotes(bool noBackslashEscapes, string value)
    {
        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            await AssertUpdatePreservesValueAsync(target, noBackslashEscapes, value);
        }
    }

    /// <summary>
    /// Checks that tracked inserts preserve text requiring escaping or UTF-8 encoding.
    /// </summary>
    [Theory]
    [MemberData(nameof(SpecialCases))]
    public async Task Insert_preserves_special_string_values(bool noBackslashEscapes, string value)
    {
        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            await AssertInsertPreservesValueAsync(target, noBackslashEscapes, value);
        }
    }

    /// <summary>
    /// Checks that tracked updates preserve text requiring escaping or UTF-8 encoding.
    /// </summary>
    [Theory]
    [MemberData(nameof(SpecialCases))]
    public async Task Update_preserves_special_string_values(bool noBackslashEscapes, string value)
    {
        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            await AssertUpdatePreservesValueAsync(target, noBackslashEscapes, value);
        }
    }

    /// <summary>
    /// Checks the separate set-based update path while another row remains unchanged.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteUpdate_preserves_quotes(bool noBackslashEscapes)
    {
        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            await AssertExecuteUpdatePreservesQuotesAsync(target, noBackslashEscapes);
        }
    }

    private static async Task AssertInsertPreservesValueAsync(
        IntegrationDatabaseTarget target,
        bool noBackslashEscapes,
        string value
    )
    {
        await using var connection = await OpenConnectionAsync(target, noBackslashEscapes);
        await CreateTemporaryTableAsync(connection);

        try
        {
            await using var context = CreateContext(connection, target);
            context.Items.Add(new StringValue
            {
                Id = 1,
                Value = value,
            });

            await context.SaveChangesAsync(CancellationToken.None);

            await AssertStoredValuesAsync(connection, [value]);
        }
        finally
        {
            await DropTemporaryTableAsync(connection);
        }
    }

    private static async Task AssertUpdatePreservesValueAsync(
        IntegrationDatabaseTarget target,
        bool noBackslashEscapes,
        string value
    )
    {
        await using var connection = await OpenConnectionAsync(target, noBackslashEscapes);
        await CreateTemporaryTableAsync(connection);

        try
        {
            // Seed outside the provider so the UPDATE assertion does not depend
            // on the correctness of Doka's INSERT path.
            await using (var seed = connection.CreateCommand())
            {
                seed.CommandText = $"INSERT INTO `{TableName}` (`Id`, `Value`) VALUES "
                    + "(1, 'before'), (2, 'unchanged');";
                _ = await seed.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await using var context = CreateContext(connection, target);
            var item = new StringValue
            {
                Id = 1,
                Value = value,
            };

            context.Attach(item);
            context.Entry(item).Property(entry => entry.Value).IsModified = true;

            await context.SaveChangesAsync(CancellationToken.None);

            await AssertStoredValuesAsync(connection, [value, "unchanged"]);
        }
        finally
        {
            await DropTemporaryTableAsync(connection);
        }
    }

    private static async Task AssertExecuteUpdatePreservesQuotesAsync(
        IntegrationDatabaseTarget target,
        bool noBackslashEscapes
    )
    {
        await using var connection = await OpenConnectionAsync(target, noBackslashEscapes);
        await CreateTemporaryTableAsync(connection);

        try
        {
            await using (var seed = connection.CreateCommand())
            {
                seed.CommandText = $"INSERT INTO `{TableName}` (`Id`, `Value`) "
                    + "VALUES (1, 'before'), (2, 'unchanged');";
                _ = await seed.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await using var context = CreateContext(connection, target);
            var affected = await context.Items
                .Where(item => item.Id == 1)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.Value, MixedQuoteValue),
                    CancellationToken.None);

            Assert.Equal(1, affected);
            await AssertStoredValuesAsync(connection, [MixedQuoteValue, "unchanged"]);
        }
        finally
        {
            await DropTemporaryTableAsync(connection);
        }
    }

    private static async Task<MySqlConnection> OpenConnectionAsync(
        IntegrationDatabaseTarget target,
        bool noBackslashEscapes
    )
    {
        var connectionString = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(target))
        {
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            NoBackslashEscapes = noBackslashEscapes,
            Pooling = false,
        }.ConnectionString;

        var connection = new MySqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(CancellationToken.None);

            if (noBackslashEscapes)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SET SESSION sql_mode = 'ANSI_QUOTES,NO_BACKSLASH_ESCAPES,STRICT_TRANS_TABLES';";
                _ = await command.ExecuteNonQueryAsync(CancellationToken.None);
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();

            throw;
        }
    }

    private static async Task CreateTemporaryTableAsync(MySqlConnection connection)
    {
        // A session-local table avoids permanent schema changes and keeps
        // concurrent test processes independent.
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TEMPORARY TABLE `{TableName}` "
            + "(`Id` int NOT NULL PRIMARY KEY, `Value` varchar(128) NOT NULL) CHARACTER SET utf8mb4;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task DropTemporaryTableAsync(MySqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TEMPORARY TABLE `{TableName}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task AssertStoredValuesAsync(
        MySqlConnection connection,
        string[] expectedValues
    )
    {
        // Read database bytes without EF materialization so a read converter
        // cannot hide write-side changes to quotation or special characters.
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT HEX(`Value`) FROM `{TableName}` ORDER BY `Id`;";
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var actualHex = new List<string>();

        while (await reader.ReadAsync(CancellationToken.None))
        {
            actualHex.Add(reader.GetString(0));
        }

        var expectedHex = expectedValues
            .Select(value => Convert.ToHexString(Encoding.UTF8.GetBytes(value)))
            .ToArray();

        Assert.Equal(expectedHex, actualHex);
    }

    private static StringContext CreateContext(
        MySqlConnection connection,
        IntegrationDatabaseTarget target
    ) => new(
        IntegrationTestDbContextOptions.Create<StringContext>()
            .UseMySql(connection, IntegrationTestEnvironment.GetServerVersion(target))
            .Options);

    private static TheoryData<bool, string> CreateCases(params string[] values)
    {
        var cases = new TheoryData<bool, string>();

        foreach (var value in values)
        {
            cases.Add(false, value);
            cases.Add(true, value);
        }

        return cases;
    }

    private sealed class StringValue
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;
    }

    private sealed class StringContext : DbContext
    {
        public StringContext(DbContextOptions<StringContext> options) : base(options) { }

        public DbSet<StringValue> Items => Set<StringValue>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<StringValue>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).ValueGeneratedNever();
                entity.Property(item => item.Value).HasColumnType("varchar(128)").IsRequired();
            });
        }
    }
}
