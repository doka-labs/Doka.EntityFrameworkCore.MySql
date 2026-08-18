namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies generated-key ownership for entity splitting against both engine families.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class EntitySplittingIntegrationTests
{
    private const string PrincipalTable = "EntitySplitInventory";
    private const string SecondaryTable = "EntitySplitInventoryDetails";

    /// <summary>
    /// MySQL keeps AUTO_INCREMENT on the principal table and a non-generating
    /// shared primary/foreign key on the secondary table.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_entity_split_generation_and_crud_contract() =>
        await AssertEntitySplitContractAsync(IntegrationDatabaseTarget.MySql84);

    /// <summary>
    /// MariaDB keeps AUTO_INCREMENT on the principal table and a non-generating
    /// shared primary/foreign key on the secondary table.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_entity_split_generation_and_crud_contract() =>
        await AssertEntitySplitContractAsync(IntegrationDatabaseTarget.MariaDb118);

    private static async Task AssertEntitySplitContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await CleanupAsync(connection);

        try
        {
            await using var source = new EmptyEntitySplitContext(
                CreateOptions<EmptyEntitySplitContext>(connectionString, serverVersion));

            await using var targetContext = new EntitySplitContext(
                CreateOptions<EntitySplitContext>(connectionString, serverVersion));

            var operations = targetContext
                .GetService<IMigrationsModelDiffer>()
                .GetDifferences(
                    source
                        .GetService<IDesignTimeModel>()
                        .Model
                        .GetRelationalModel(),
                    targetContext
                        .GetService<IDesignTimeModel>()
                        .Model
                        .GetRelationalModel());

            var commands = targetContext
                .GetService<IMigrationsSqlGenerator>()
                .Generate(
                    operations,
                    targetContext.GetService<IDesignTimeModel>()
                        .Model);

            foreach (var migrationCommand in commands)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = migrationCommand.CommandText;
                _ = await command.ExecuteNonQueryAsync();
            }

            Assert.Equal("auto_increment", await ReadColumnExtraAsync(connection, PrincipalTable));
            Assert.Equal(string.Empty, await ReadColumnExtraAsync(connection, SecondaryTable));
            Assert.Equal(1L, await CountConstraintAsync(connection, SecondaryTable, "PRIMARY KEY"));
            Assert.Equal(1L, await CountConstraintAsync(connection, SecondaryTable, "FOREIGN KEY"));
            Assert.Equal("CASCADE", await ReadDeleteRuleAsync(connection));

            var inventory = new EntitySplitInventory
            {
                Name = "inventory",
                Description = "initial",
            };
            targetContext.Inventory.Add(inventory);
            await targetContext.SaveChangesAsync();
            Assert.True(inventory.Id > 0);

            await using (var readContext = new EntitySplitContext(
                             CreateOptions<EntitySplitContext>(connectionString, serverVersion)))
            {
                var loaded = await readContext.Inventory.SingleAsync(item => item.Id == inventory.Id);
                Assert.Equal("inventory", loaded.Name);
                Assert.Equal("initial", loaded.Description);
                loaded.Description = "updated";
                await readContext.SaveChangesAsync();
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT `Description` FROM `{SecondaryTable}` WHERE `Id` = @id;";
                command.Parameters.AddWithValue("@id", inventory.Id);
                Assert.Equal(
                    "updated",
                    Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));

                command.CommandText = $"DELETE FROM `{PrincipalTable}` WHERE `Id` = @id;";
                _ = await command.ExecuteNonQueryAsync();

                command.CommandText = $"SELECT COUNT(*) FROM `{SecondaryTable}` WHERE `Id` = @id;";
                Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
            }

            await using var finalContext = new EntitySplitContext(
                CreateOptions<EntitySplitContext>(connectionString, serverVersion));
            Assert.Empty(
                await finalContext
                    .Inventory
                    .AsNoTracking()
                    .ToListAsync());
        }
        finally
        {
            await CleanupAsync(connection);
        }
    }

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

    private static async Task<long> CountConstraintAsync(
        MySqlConnection connection,
        string tableName,
        string constraintType
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.`TABLE_CONSTRAINTS` "
            + "WHERE `CONSTRAINT_SCHEMA` = DATABASE() AND `TABLE_NAME` = @tableName "
            + "AND `CONSTRAINT_TYPE` = @constraintType;";
        command.Parameters.AddWithValue("@tableName", tableName);
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
        public DbSet<EntitySplitInventory> Inventory => Set<EntitySplitInventory>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<EntitySplitInventory>(entity =>
            {
                entity.ToTable(PrincipalTable);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .UseMySqlAutoIncrementColumn();
                entity
                    .Property(item => item.Name)
                    .HasMaxLength(80);
                entity
                    .Property(item => item.Description)
                    .HasMaxLength(80);
                entity.SplitToTable(SecondaryTable, split => split.Property(item => item.Description));
            });
        }
    }

    private sealed class EntitySplitInventory
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;
    }
}
