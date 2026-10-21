namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies byte-exact writes for character and binary properties across the
/// supported database engines and SQL modes.
/// </summary>
[IntegrationDatabaseTest]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlNonStringPersistenceIntegrationTests
{
    private const string TableName = "DokaNonStringRoundTrip";

    private static readonly byte[] s_binaryValue = [0x00, 0x27, 0x22, 0x5C, 0xFF];

    /// <summary>
    /// Provides separately reported character cases under both SQL modes.
    /// </summary>
    public static TheoryData<string, bool, char> CharacterCases => CreateCharacterCases(
        '\'', '"', '\\', '\0', '\u00E4');

    /// <summary>
    /// Reports each selected engine and SQL mode as a separate binary case.
    /// </summary>
    public static TheoryData<string, bool> SqlModeCases => CreateSqlModeCases();

    /// <summary>
    /// Checks that entity inserts retain each character's encoded database bytes.
    /// </summary>
    [Theory]
    [MemberData(nameof(CharacterCases))]
    public Task Char_insert_preserves_special_characters(
        string targetName,
        bool noBackslashEscapes,
        char value
    ) => AssertCharInsertAsync(
        Enum.Parse<IntegrationDatabaseTarget>(targetName),
        noBackslashEscapes,
        value);

    /// <summary>
    /// Checks that entity updates retain each character's encoded database bytes.
    /// </summary>
    [Theory]
    [MemberData(nameof(CharacterCases))]
    public Task Char_update_preserves_special_characters(
        string targetName,
        bool noBackslashEscapes,
        char value
    ) => AssertCharUpdateAsync(
        Enum.Parse<IntegrationDatabaseTarget>(targetName),
        noBackslashEscapes,
        value);

    /// <summary>
    /// Checks that binary inserts do not reinterpret quote, slash, NUL, or high bytes as text.
    /// </summary>
    [Theory]
    [MemberData(nameof(SqlModeCases))]
    public Task Binary_insert_preserves_special_bytes(
        string targetName,
        bool noBackslashEscapes
    ) => AssertBinaryInsertAsync(
        Enum.Parse<IntegrationDatabaseTarget>(targetName),
        noBackslashEscapes);

    /// <summary>
    /// Checks that binary updates retain every byte and leave other rows unchanged.
    /// </summary>
    [Theory]
    [MemberData(nameof(SqlModeCases))]
    public Task Binary_update_preserves_special_bytes(
        string targetName,
        bool noBackslashEscapes
    ) => AssertBinaryUpdateAsync(
        Enum.Parse<IntegrationDatabaseTarget>(targetName),
        noBackslashEscapes);

    private static async Task AssertCharInsertAsync(
        IntegrationDatabaseTarget target,
        bool noBackslashEscapes,
        char value
    )
    {
        // Arrange
        await using var connection = await OpenConnectionAsync(target, noBackslashEscapes);
        await CreateTemporaryTableAsync(connection);

        try
        {
            await using var context = CreateContext(connection, target);
            context.Items.Add(new NonStringValue
            {
                Id = 1,
                CharacterValue = value,
                BinaryValue = [0x01],
            });

            // Act
            await context.SaveChangesAsync(CancellationToken.None);

            // Assert
            await AssertStoredHexAsync(
                connection,
                nameof(NonStringValue.CharacterValue),
                [Convert.ToHexString(Encoding.UTF8.GetBytes(value.ToString()))]);
        }
        finally
        {
            await DropTemporaryTableAsync(connection);
        }
    }

    private static async Task AssertCharUpdateAsync(
        IntegrationDatabaseTarget target,
        bool noBackslashEscapes,
        char value
    )
    {
        // Arrange
        await using var connection = await OpenConnectionAsync(target, noBackslashEscapes);
        await CreateTemporaryTableAsync(connection);

        try
        {
            await using (var seed = connection.CreateCommand())
            {
                seed.CommandText = $"INSERT INTO `{TableName}` (`Id`, `CharacterValue`, `BinaryValue`) "
                    + "VALUES (1, 'x', X'01'), (2, 'y', X'02');";
                _ = await seed.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await using var context = CreateContext(connection, target);
            var item = new NonStringValue
            {
                Id = 1,
                CharacterValue = value,
                BinaryValue = [0x01],
            };

            context.Attach(item);
            context.Entry(item).Property(entry => entry.CharacterValue).IsModified = true;

            // Act
            await context.SaveChangesAsync(CancellationToken.None);

            // Assert
            await AssertStoredHexAsync(
                connection,
                nameof(NonStringValue.CharacterValue),
                [Convert.ToHexString(Encoding.UTF8.GetBytes(value.ToString())), "79"]);
        }
        finally
        {
            await DropTemporaryTableAsync(connection);
        }
    }

    private static async Task AssertBinaryInsertAsync(
        IntegrationDatabaseTarget target,
        bool noBackslashEscapes
    )
    {
        // Arrange
        await using var connection = await OpenConnectionAsync(target, noBackslashEscapes);
        await CreateTemporaryTableAsync(connection);

        try
        {
            await using var context = CreateContext(connection, target);
            context.Items.Add(new NonStringValue
            {
                Id = 1,
                CharacterValue = 'x',
                BinaryValue = s_binaryValue,
            });

            // Act
            await context.SaveChangesAsync(CancellationToken.None);

            // Assert
            await AssertStoredHexAsync(
                connection,
                nameof(NonStringValue.BinaryValue),
                [Convert.ToHexString(s_binaryValue)]);
        }
        finally
        {
            await DropTemporaryTableAsync(connection);
        }
    }

    private static async Task AssertBinaryUpdateAsync(
        IntegrationDatabaseTarget target,
        bool noBackslashEscapes
    )
    {
        // Arrange
        await using var connection = await OpenConnectionAsync(target, noBackslashEscapes);
        await CreateTemporaryTableAsync(connection);

        try
        {
            await using (var seed = connection.CreateCommand())
            {
                seed.CommandText = $"INSERT INTO `{TableName}` (`Id`, `CharacterValue`, `BinaryValue`) "
                    + "VALUES (1, 'x', X'01'), (2, 'y', X'02');";
                _ = await seed.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await using var context = CreateContext(connection, target);
            var item = new NonStringValue
            {
                Id = 1,
                CharacterValue = 'x',
                BinaryValue = s_binaryValue,
            };

            context.Attach(item);
            context.Entry(item).Property(entry => entry.BinaryValue).IsModified = true;

            // Act
            await context.SaveChangesAsync(CancellationToken.None);

            // Assert
            await AssertStoredHexAsync(
                connection,
                nameof(NonStringValue.BinaryValue),
                [Convert.ToHexString(s_binaryValue), "02"]);
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
        // A session-local table isolates exact-byte assertions from other test processes.
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TEMPORARY TABLE `{TableName}` "
            + "(`Id` int NOT NULL PRIMARY KEY, `CharacterValue` char(1) NOT NULL, "
            + "`BinaryValue` varbinary(16) NOT NULL) CHARACTER SET utf8mb4;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task DropTemporaryTableAsync(MySqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TEMPORARY TABLE `{TableName}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task AssertStoredHexAsync(
        MySqlConnection connection,
        string columnName,
        string[] expectedHex
    )
    {
        // Read raw bytes so an EF read conversion cannot conceal a write error.
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT HEX(`{columnName}`) FROM `{TableName}` ORDER BY `Id`;";
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var actualHex = new List<string>();

        while (await reader.ReadAsync(CancellationToken.None))
        {
            actualHex.Add(reader.GetString(0));
        }

        Assert.Equal(expectedHex, actualHex);
    }

    private static NonStringContext CreateContext(
        MySqlConnection connection,
        IntegrationDatabaseTarget target
    ) => new(
        IntegrationTestDbContextOptions.Create<NonStringContext>()
            .UseMySql(connection, IntegrationTestEnvironment.GetServerVersion(target))
            .Options);

    private static TheoryData<string, bool, char> CreateCharacterCases(params char[] values)
    {
        var cases = new TheoryData<string, bool, char>();

        // WHY: The target enum is internal, but xUnit requires public theory
        // methods. Names keep each engine visible without widening that contract.
        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            foreach (var value in values)
            {
                cases.Add(target.ToString(), false, value);
                cases.Add(target.ToString(), true, value);
            }
        }

        return cases;
    }

    private static TheoryData<string, bool> CreateSqlModeCases()
    {
        var cases = new TheoryData<string, bool>();

        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            cases.Add(target.ToString(), false);
            cases.Add(target.ToString(), true);
        }

        return cases;
    }

    private sealed class NonStringValue
    {
        public int Id { get; set; }

        public char CharacterValue { get; set; }

        public byte[] BinaryValue { get; set; } = [];
    }

    private sealed class NonStringContext : DbContext
    {
        public NonStringContext(DbContextOptions<NonStringContext> options) : base(options) { }

        public DbSet<NonStringValue> Items => Set<NonStringValue>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NonStringValue>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).ValueGeneratedNever();
                entity.Property(item => item.CharacterValue).HasColumnType("char(1)").IsRequired();
                entity.Property(item => item.BinaryValue).HasColumnType("varbinary(16)").IsRequired();
            });
        }
    }
}
