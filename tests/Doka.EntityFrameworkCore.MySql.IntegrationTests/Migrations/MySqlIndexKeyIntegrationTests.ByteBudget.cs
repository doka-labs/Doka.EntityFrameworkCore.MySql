namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlIndexKeyIntegrationTests
{
    private const string ImplicitBudgetTable = "DokaImplicitIndexBudgetProbe";
    private const string ImplicitBudgetIndex = "IX_DokaImplicitIndexBudget_Ancestor";
    private const string OwnedBudgetTable = "DokaOwnedTableIndexBudgetProbe";
    private const string OwnedBudgetIndex = "IX_DokaOwnedTableIndexBudget_Values";
    private const string DecimalBudgetTable = "DokaDecimalIndexBudgetProbe";
    private const string DecimalBudgetIndex = "IX_DokaDecimalIndexBudget_Values";

    /// <summary>
    /// The report's overlapping implicit indexes create a complete native MySQL
    /// index and persist maximum-width utf8mb4 values without truncation.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_implicit_composite_indexes_share_the_physical_byte_budget() =>
        WithBudgetDatabaseAsync(IntegrationDatabaseTarget.MySql84, AssertImplicitCompositeBudgetAsync);

    /// <summary>
    /// The same implicit allocation preserves full native index semantics on
    /// MariaDB's supported long-term maintenance line.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_implicit_composite_indexes_share_the_physical_byte_budget() =>
        WithBudgetDatabaseAsync(IntegrationDatabaseTarget.MariaDb118, AssertImplicitCompositeBudgetAsync);

    /// <summary>
    /// An owned MySQL index honors the owner's actual shared-table charset and
    /// persists complete maximum-width values without an implicit prefix.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_owned_indexes_use_the_owner_physical_table_character_set() =>
        WithBudgetDatabaseAsync(IntegrationDatabaseTarget.MySql84, AssertOwnedTableBudgetAsync);

    /// <summary>
    /// MariaDB uses the same physical owner-table default for owned key parts.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_owned_indexes_use_the_owner_physical_table_character_set() =>
        WithBudgetDatabaseAsync(IntegrationDatabaseTarget.MariaDb118, AssertOwnedTableBudgetAsync);

    /// <summary>
    /// MySQL accepts the complete index when the default DECIMAL consumes its
    /// actual nine bytes and the remaining implicit string has one character.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_decimal_index_uses_the_native_packed_width() =>
        WithBudgetDatabaseAsync(IntegrationDatabaseTarget.MySql84, AssertDecimalBudgetAsync);

    /// <summary>
    /// MariaDB accepts the same complete key without a hidden index prefix.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_decimal_index_uses_the_native_packed_width() =>
        WithBudgetDatabaseAsync(IntegrationDatabaseTarget.MariaDb118, AssertDecimalBudgetAsync);

    private static async Task AssertImplicitCompositeBudgetAsync(
        MySqlConnection connection,
        MySqlServerVersion serverVersion
    )
    {
        // Arrange
        await using var context = new ImplicitBudgetContext(
            IntegrationTestDbContextOptions.Create<ImplicitBudgetContext>()
                .UseMySql(connection, serverVersion)
                .Options);

        var value = string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x1F642), 254));
        var row = new Dictionary<string, object>
        {
            ["Id"] = value,
            ["Scope"] = value,
            ["Tree"] = value,
            ["L"] = 1L,
            ["R"] = 4L,
            ["Position"] = 0L,
            ["Depth"] = 0,
        };

        try
        {
            // Act
            await using var create = connection.CreateCommand();
            create.CommandText = context.Database.GenerateCreateScript();
            _ = await create.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            context.Set<Dictionary<string, object>>("Budget").Add(row);

            _ = await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            var persisted = await context.Set<Dictionary<string, object>>("Budget")
                .AsNoTracking()
                .SingleAsync(CancellationToken.None)
                .ConfigureAwait(false);

            var indexedColumns = await ReadBudgetIndexAsync(connection, ImplicitBudgetTable, ImplicitBudgetIndex)
                .ConfigureAwait(false);

            var model = context.GetService<IDesignTimeModel>().Model.FindEntityType("Budget")!;

            // Assert
            Assert.Equal(value, persisted["Id"]);
            Assert.Equal(value, persisted["Scope"]);
            Assert.Equal(value, persisted["Tree"]);
            Assert.True((bool)persisted["Flag"]);
            Assert.Equal(["Scope", "Flag", "Tree", "R", "L", "Id"], indexedColumns.Select(column => column.Name));
            Assert.All(indexedColumns, column => Assert.Null(column.Prefix));
            Assert.Equal(254, model.FindProperty("Id")!.GetMaxLength());
            Assert.Equal(254, model.FindProperty("Scope")!.GetMaxLength());
            Assert.Equal(254, model.FindProperty("Tree")!.GetMaxLength());
            Assert.Equal(255, model.FindProperty("Parent")!.GetMaxLength());
        }
        finally
        {
            await DropImplicitBudgetTableAsync(connection).ConfigureAwait(false);
        }
    }

    private static async Task<List<(string Name, long? Prefix)>> ReadBudgetIndexAsync(
        MySqlConnection connection,
        string table,
        string index
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_NAME, SUB_PART FROM information_schema.STATISTICS "
            + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND INDEX_NAME = @index "
            + "ORDER BY SEQ_IN_INDEX;";
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@index", index);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
        var columns = new List<(string Name, long? Prefix)>();

        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            columns.Add((
                reader.GetString(0),
                await reader.IsDBNullAsync(1, CancellationToken.None).ConfigureAwait(false)
                    ? null
                    : reader.GetInt64(1)));
        }

        return columns;
    }

    private static async Task DropImplicitBudgetTableAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{ImplicitBudgetTable}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task AssertOwnedTableBudgetAsync(
        MySqlConnection connection,
        MySqlServerVersion serverVersion
    )
    {
        // Arrange
        await using var context = new OwnedBudgetContext(
            IntegrationTestDbContextOptions.Create<OwnedBudgetContext>()
                .UseMySql(connection, serverVersion)
                .Options);

        var character = char.ConvertFromUtf32(0x1F642);
        var row = new OwnedBudgetOwner
        {
            Id = 1,
            Detail = new OwnedBudgetDetail
            {
                A = string.Concat(Enumerable.Repeat(character, 700)),
                B = string.Concat(Enumerable.Repeat(character, 68)),
            },
        };

        // Act
        await using var create = connection.CreateCommand();
        create.CommandText = context.Database.GenerateCreateScript();
        _ = await create.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        context.Add(row);

        _ = await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        var persisted = await context.Set<OwnedBudgetOwner>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None)
            .ConfigureAwait(false);

        var indexedColumns = await ReadBudgetIndexAsync(connection, OwnedBudgetTable, OwnedBudgetIndex)
            .ConfigureAwait(false);

        await using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT COLUMN_NAME, CHARACTER_MAXIMUM_LENGTH, CHARACTER_SET_NAME "
            + "FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table "
            + "AND COLUMN_NAME IN ('Detail_A', 'Detail_B') ORDER BY COLUMN_NAME;";
        schema.Parameters.AddWithValue("@table", OwnedBudgetTable);

        await using var reader = await schema.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
        var columns = new List<(string Name, long Length, string CharSet)>();

        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            columns.Add((reader.GetString(0), reader.GetInt64(1), reader.GetString(2)));
        }

        // Assert
        Assert.Equal(row.Detail.A, persisted.Detail.A);
        Assert.Equal(row.Detail.B, persisted.Detail.B);
        Assert.Equal(["Detail_A", "Detail_B"], indexedColumns.Select(column => column.Name));
        Assert.All(indexedColumns, column => Assert.Null(column.Prefix));
        Assert.Equal([("Detail_A", 700L, "utf8mb4"), ("Detail_B", 68L, "utf8mb4")], columns);
    }

    private static async Task AssertDecimalBudgetAsync(
        MySqlConnection connection,
        MySqlServerVersion serverVersion
    )
    {
        // Arrange
        await using var context = new DecimalBudgetContext(
            IntegrationTestDbContextOptions.Create<DecimalBudgetContext>()
                .UseMySql(connection, serverVersion)
                .Options);

        var character = char.ConvertFromUtf32(0x1F642);
        var row = new Dictionary<string, object>
        {
            ["Id"] = 1,
            ["A"] = string.Concat(Enumerable.Repeat(character, 255)),
            ["B"] = string.Concat(Enumerable.Repeat(character, 255)),
            ["C"] = string.Concat(Enumerable.Repeat(character, 254)),
            ["D"] = 1234567890123456.78m,
            ["E"] = character,
        };

        // Act
        await using var create = connection.CreateCommand();
        create.CommandText = context.Database.GenerateCreateScript();
        _ = await create.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        context.Set<Dictionary<string, object>>("DecimalBudget").Add(row);

        _ = await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        var persisted = await context.Set<Dictionary<string, object>>("DecimalBudget")
            .AsNoTracking()
            .SingleAsync(CancellationToken.None)
            .ConfigureAwait(false);

        var indexedColumns = await ReadBudgetIndexAsync(connection, DecimalBudgetTable, DecimalBudgetIndex)
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(row["A"], persisted["A"]);
        Assert.Equal(row["B"], persisted["B"]);
        Assert.Equal(row["C"], persisted["C"]);
        Assert.Equal(row["D"], persisted["D"]);
        Assert.Equal(row["E"], persisted["E"]);
        Assert.Equal(["A", "B", "C", "D", "E"], indexedColumns.Select(column => column.Name));
        Assert.All(indexedColumns, column => Assert.Null(column.Prefix));
    }

    /// <summary>
    /// Applies complete generated DDL inside one disposable native database.
    /// </summary>
    private static async Task WithBudgetDatabaseAsync(
        IntegrationDatabaseTarget target,
        Func<MySqlConnection, MySqlServerVersion, Task> assertion
    )
    {
        var databaseName = "doka_index_budget_" + Guid.NewGuid().ToString("N");
        var connectionString = new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetConnectionString(target))
        {
            Database = string.Empty,
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            Pooling = false,
        };

        await using var administration = new MySqlConnection(connectionString.ConnectionString);
        await administration.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var command = administration.CreateCommand();
        command.CommandText = $"CREATE DATABASE `{databaseName}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            // WHY: GenerateCreateScript includes ALTER DATABASE. An owned database
            // preserves that exact DDL without changing another test's defaults.
            connectionString.Database = databaseName;
            await using var connection = new MySqlConnection(connectionString.ConnectionString);
            await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await assertion(connection, IntegrationTestEnvironment.GetServerVersion(target)).ConfigureAwait(false);
        }
        finally
        {
            command.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`;";
            _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private sealed class OwnedBudgetOwner
    {
        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Verifies new.
        /// </summary>
        public OwnedBudgetDetail Detail { get; set; } = new();
    }

    private sealed class OwnedBudgetDetail
    {
        /// <summary>
        /// Gets or sets the a.
        /// </summary>
        public string A { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the b.
        /// </summary>
        public string B { get; set; } = string.Empty;
    }

    private sealed class OwnedBudgetContext(
        DbContextOptions<OwnedBudgetContext> options
    ) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("latin1");
            modelBuilder.Entity<OwnedBudgetOwner>(owner =>
            {
                owner.ToTable(OwnedBudgetTable).HasCharSet("utf8mb4");
                owner.OwnsOne(value => value.Detail, detail =>
                {
                    detail.ToTable(OwnedBudgetTable);
                    detail.Property(value => value.A).HasMaxLength(700);
                    detail.HasIndex(value => new { value.A, value.B }).HasDatabaseName(OwnedBudgetIndex);
                });
                owner.Navigation(value => value.Detail).IsRequired();
            });
        }
    }

    private sealed class ImplicitBudgetContext(
        DbContextOptions<ImplicitBudgetContext> options
    ) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("utf8mb4");
            var entity = modelBuilder.SharedTypeEntity<Dictionary<string, object>>("Budget");
            entity.ToTable(ImplicitBudgetTable);
            entity.Property<string>("Id").IsRequired();
            entity.Property<string>("Scope").IsRequired();
            entity.Property<string>("Tree").IsRequired();
            entity.Property<string>("Parent");
            entity.Property<long>("L");
            entity.Property<long>("R");
            entity.Property<long>("Position");
            entity.Property<int>("Depth");
            entity.Property<bool>("Flag").HasComputedColumnSql("`R` > `L` + 1", stored: false);
            entity.HasKey("Id");
            entity.HasIndex("Scope", "Tree", "L");
            entity.HasIndex("Scope", "Tree", "R");
            entity.HasIndex("Scope", "Tree", "Parent", "Position");
            entity.HasIndex("Scope", "Flag", "Tree", "R", "L", "Id").HasDatabaseName(ImplicitBudgetIndex);
        }
    }

    private sealed class DecimalBudgetContext(
        DbContextOptions<DecimalBudgetContext> options
    ) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("utf8mb4");
            var entity = modelBuilder.SharedTypeEntity<Dictionary<string, object>>("DecimalBudget");
            entity.ToTable(DecimalBudgetTable);
            entity.Property<int>("Id");
            entity.Property<string>("A").HasMaxLength(255).IsRequired();
            entity.Property<string>("B").HasMaxLength(255).IsRequired();
            entity.Property<string>("C").HasMaxLength(254).IsRequired();
            entity.Property<decimal>("D");
            entity.Property<string>("E").IsRequired();
            entity.HasKey("Id");
            entity.HasIndex("A", "B", "C", "D", "E").HasDatabaseName(DecimalBudgetIndex);
        }
    }
}
