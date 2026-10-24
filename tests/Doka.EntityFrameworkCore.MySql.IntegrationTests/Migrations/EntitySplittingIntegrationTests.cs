namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies generated-key ownership and secondary GUID columns for entity splitting.</summary>
[IntegrationDatabaseTest]
public sealed class EntitySplittingIntegrationTests
{
    private const string PrincipalTable = "EntitySplitInventory";
    private const string SecondaryTable = "EntitySplitInventoryDetails";

    /// <summary>Reports every selected supported database target.</summary>
    public static IEnumerable<object[]> EntitySplitTargets => IntegrationTestEnvironment.GetSupportedTargets()
        .Where(IntegrationTestEnvironment.IsTargetSelected)
        .Select(target => new object[] { target.ToString() });

    /// <summary>Keeps generated keys on the main table and GUID columns only on their secondary fragment.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(EntitySplitTargets))]
    public async Task Entity_split_migrations_preserve_column_and_key_ownership(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await CleanupAsync(connection);

        try
        {
            await using var source = new EmptyEntitySplitContext(
                CreateOptions<EmptyEntitySplitContext>(connectionString, serverVersion));

            await using var context = CreateContext(target);

            // Act
            var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(
                source.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
                context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

            var commands = context
                .GetService<IMigrationsSqlGenerator>()
                .Generate(operations, context.GetService<IDesignTimeModel>().Model);

            foreach (var migrationCommand in commands)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = migrationCommand.CommandText;
                _ = await command.ExecuteNonQueryAsync();
            }

            var principalExtra = await ReadColumnExtraAsync(connection, PrincipalTable);
            var secondaryExtra = await ReadColumnExtraAsync(connection, SecondaryTable);
            var secondaryPrimaryKeys = await CountConstraintAsync(connection, "PRIMARY KEY");
            var secondaryForeignKeys = await CountConstraintAsync(connection, "FOREIGN KEY");
            var deleteRule = await ReadDeleteRuleAsync(connection);
            var principalGuidColumns = await CountGuidColumnsAsync(connection, PrincipalTable);
            var secondaryGuidColumns = await CountGuidColumnsAsync(connection, SecondaryTable);

            // Assert
            Assert.Equal("auto_increment", principalExtra);
            Assert.Equal(string.Empty, secondaryExtra);
            Assert.Equal(1L, secondaryPrimaryKeys);
            Assert.Equal(1L, secondaryForeignKeys);
            Assert.Equal("CASCADE", deleteRule);
            Assert.Equal(0L, principalGuidColumns);
            Assert.Equal(2L, secondaryGuidColumns);
        }
        finally
        {
            await CleanupAsync(connection);
        }
    }

    /// <summary>Inserts supplied required and nullable GUIDs without generating values for secondary columns.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(EntitySplitTargets))]
    public async Task Entity_split_guid_values_are_preserved_on_insert(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        await using var context = CreateContext(target);
        await using var connection = new MySqlConnection(IntegrationTestEnvironment.GetConnectionString(target));
        await connection.OpenAsync();
        await CleanupAsync(connection);

        try
        {
            await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), CancellationToken.None);
            var rows = CreateInventoryRows();
            context.Inventory.AddRange(rows);

            // Act
            await context.SaveChangesAsync(CancellationToken.None);
            var persisted = await context.Inventory.AsNoTracking().ToDictionaryAsync(row => row.Name);

            // Assert
            Assert.Equal(2, persisted.Count);
            Assert.All(rows, row =>
            {
                Assert.True(row.Id > 0);
                Assert.Equal(row.Id, persisted[row.Name].Id);
                Assert.Equal(row.TreeId, persisted[row.Name].TreeId);
                Assert.Equal(row.OptionalTreeId, persisted[row.Name].OptionalTreeId);
            });
            Assert.Null(persisted["inventory-1"].OptionalTreeId);
            Assert.Equal(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), persisted["inventory-2"].OptionalTreeId);
        }
        finally
        {
            await CleanupAsync(connection);
        }
    }

    /// <summary>Updates secondary GUIDs and preserves null-to-value and value-to-null transitions.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(EntitySplitTargets))]
    public async Task Entity_split_guid_updates_preserve_null_transitions(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        await using var context = CreateContext(target);
        await using var connection = new MySqlConnection(IntegrationTestEnvironment.GetConnectionString(target));
        await connection.OpenAsync();
        await CleanupAsync(connection);

        try
        {
            await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), CancellationToken.None);
            context.Inventory.AddRange(CreateInventoryRows());
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            var rows = await context.Inventory.OrderBy(row => row.Name).ToListAsync();
            var changedTreeId = Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f");
            var changedOptionalTreeId = Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100");
            rows[0].TreeId = changedTreeId;
            rows[0].OptionalTreeId = changedOptionalTreeId;
            rows[1].OptionalTreeId = null;

            // Act
            await context.SaveChangesAsync(CancellationToken.None);
            var persisted = await context.Inventory.AsNoTracking().ToDictionaryAsync(row => row.Name);

            // Assert
            Assert.Equal(2, persisted.Count);
            Assert.Equal(changedTreeId, persisted["inventory-1"].TreeId);
            Assert.Equal(changedOptionalTreeId, persisted["inventory-1"].OptionalTreeId);
            Assert.Null(persisted["inventory-2"].OptionalTreeId);
            Assert.Equal(rows[1].TreeId, persisted["inventory-2"].TreeId);
        }
        finally
        {
            await CleanupAsync(connection);
        }
    }

    /// <summary>Deletes the GUID-bearing secondary fragment through its physical cascading foreign key.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(EntitySplitTargets))]
    public async Task Entity_split_secondary_rows_cascade_on_principal_delete(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        await using var context = CreateContext(target);
        await using var connection = new MySqlConnection(IntegrationTestEnvironment.GetConnectionString(target));
        await connection.OpenAsync();
        await CleanupAsync(connection);

        try
        {
            await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), CancellationToken.None);
            context.Inventory.AddRange(CreateInventoryRows());
            await context.SaveChangesAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM `{PrincipalTable}`;";

            // Act
            var deleted = await command.ExecuteNonQueryAsync();
            command.CommandText = $"SELECT COUNT(*) FROM `{SecondaryTable}`;";
            var remainingSecondaryRows = Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            var remainingInventory = await context.Inventory.AsNoTracking().ToListAsync();

            // Assert
            Assert.Equal(2, deleted);
            Assert.Equal(0L, remainingSecondaryRows);
            Assert.Empty(remainingInventory);
        }
        finally
        {
            await CleanupAsync(connection);
        }
    }

    private static EntitySplitInventory[] CreateInventoryRows() =>
    [
        new()
        {
            Name = "inventory-1",
            TreeId = Guid.Parse("11223344-5566-7788-99aa-bbccddeeff00"),
            Description = "initial-1",
        },
        new()
        {
            Name = "inventory-2",
            TreeId = Guid.Parse("22334455-6677-8899-aabb-ccddeeff0011"),
            OptionalTreeId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            Description = "initial-2",
        },
    ];

    private static EntitySplitContext CreateContext(
        IntegrationDatabaseTarget target
    ) => new(CreateOptions<EntitySplitContext>(
        IntegrationTestEnvironment.GetConnectionString(target), IntegrationTestEnvironment.GetServerVersion(target)));

    private static async Task<string> ReadColumnExtraAsync(
        MySqlConnection connection,
        string tableName
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT `EXTRA` FROM information_schema.`COLUMNS` "
            + "WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = @tableName AND `COLUMN_NAME` = 'Id';";
        command.Parameters.AddWithValue("@tableName", tableName);

        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static async Task<long> CountGuidColumnsAsync(
        MySqlConnection connection,
        string tableName
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.`COLUMNS` "
            + "WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = @tableName "
            + "AND `COLUMN_NAME` IN ('TreeId', 'OptionalTreeId');";
        command.Parameters.AddWithValue("@tableName", tableName);

        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<long> CountConstraintAsync(
        MySqlConnection connection,
        string constraintType
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.`TABLE_CONSTRAINTS` "
            + "WHERE `CONSTRAINT_SCHEMA` = DATABASE() AND `TABLE_NAME` = @tableName "
            + "AND `CONSTRAINT_TYPE` = @constraintType;";
        command.Parameters.AddWithValue("@tableName", SecondaryTable);
        command.Parameters.AddWithValue("@constraintType", constraintType);

        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ReadDeleteRuleAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT `DELETE_RULE` FROM information_schema.`REFERENTIAL_CONSTRAINTS` "
            + "WHERE `CONSTRAINT_SCHEMA` = DATABASE() AND `TABLE_NAME` = @tableName;";
        command.Parameters.AddWithValue("@tableName", SecondaryTable);

        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task CleanupAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{SecondaryTable}`; DROP TABLE IF EXISTS `{PrincipalTable}`;";
        _ = await command.ExecuteNonQueryAsync();
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        string connectionString,
        MySqlServerVersion serverVersion
    )
        where TContext : DbContext => IntegrationTestDbContextOptions
        .Create<TContext>()
        .UseMySql(connectionString, serverVersion)
        .Options;

    private sealed class EmptyEntitySplitContext(DbContextOptions<EmptyEntitySplitContext> options)
        : DbContext(options);

    private sealed class EntitySplitContext(DbContextOptions<EntitySplitContext> options) : DbContext(options)
    {
        /// <summary>Gets inventory rows with GUIDs stored in their secondary table.</summary>
        public DbSet<EntitySplitInventory> Inventory => Set<EntitySplitInventory>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<EntitySplitInventory>(entity =>
            {
                entity.ToTable(PrincipalTable);
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).UseMySqlAutoIncrementColumn();
                entity.Property(item => item.Name).HasMaxLength(80);
                entity.Property(item => item.Description).HasMaxLength(80);

                // WHY: The GUID properties must have no column in the main
                // table, or invalid primary-store lookups would go unnoticed.
                entity.SplitToTable(SecondaryTable, split =>
                {
                    split.Property(item => item.Description);
                    split.Property(item => item.TreeId);
                    split.Property(item => item.OptionalTreeId);
                });
            });
        }
    }

    private sealed class EntitySplitInventory
    {
        /// <summary>Gets or sets the main table's generated key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the main table's payload.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the secondary table's description.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Gets or sets the required secondary GUID.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional secondary GUID.</summary>
        public Guid? OptionalTreeId { get; set; }
    }
}
