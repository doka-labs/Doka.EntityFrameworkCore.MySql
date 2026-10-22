namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySql84CrudBaselineTests
{
    /// <summary>
    /// Verifies that the provider baseline can execute migration-style DDL and CRUD against MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_crud_smoke_path_and_migration_baseline_succeed()
    {
        // Arrange
        var connectionString = GetConnectionString();

        await ResetDatabaseObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new MySql84CrudContext(CreateOptions(connectionString));
            var historyRepository = context.GetService<IHistoryRepository>();
            var migrationsSqlGenerator = context.GetService<IMigrationsSqlGenerator>();

            // Act
            // WHY: This deliberately exercises one entity through the complete
            // create/read/update/delete lifecycle on migration-generated objects.
            await historyRepository
                .CreateIfNotExistsAsync()
                .ConfigureAwait(false);

            var migrationCommands = migrationsSqlGenerator.Generate(
                new MigrationOperation[]
                {
                    new CreateTableOperation
                    {
                        Name = MigrationProbeTableName,
                        Columns =
                        {
                            new AddColumnOperation
                            {
                                Name = "Id",
                                ClrType = typeof(int),
                                ColumnType = "int",
                                IsNullable = false,
                            },
                        },
                    },
                },
                context.Model);

            foreach (var command in migrationCommands)
            {
                // Safe: SQL emitted by IMigrationsSqlGenerator from fixture-controlled operations.
                await context
                    .Database.ExecuteSqlRawAsync(command.CommandText)
                    .ConfigureAwait(false);
            }

            await context
                .Database.ExecuteSqlRawAsync(
                    """
                    CREATE TABLE `Phase1CrudEntities` (
                        `Id` int NOT NULL AUTO_INCREMENT,
                        `Name` longtext NOT NULL,
                        CONSTRAINT `PK_Phase1CrudEntities` PRIMARY KEY (`Id`)
                    ) CHARACTER SET utf8mb4;
                    """)
                .ConfigureAwait(false);

            var createdEntity = new MySql84CrudEntity
            {
                Name = "initial",
            };

            context.Entities.Add(createdEntity);
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            context.ChangeTracker.Clear();

            var queriedEntity = await context
                .Entities.SingleAsync(entity => entity.Id == createdEntity.Id)
                .ConfigureAwait(false);

            var initialName = queriedEntity.Name;

            queriedEntity.Name = "updated";
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            context.ChangeTracker.Clear();

            var updatedEntity = await context
                .Entities.SingleAsync(entity => entity.Id == createdEntity.Id)
                .ConfigureAwait(false);

            var updatedName = updatedEntity.Name;

            context.Entities.Remove(updatedEntity);
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            var remainingCount = await context
                .Entities.CountAsync()
                .ConfigureAwait(false);

            var historyExists = await TableExistsAsync(connectionString, HistoryTableName)
                .ConfigureAwait(false);

            var migrationProbeExists = await TableExistsAsync(connectionString, MigrationProbeTableName)
                .ConfigureAwait(false);

            var crudTableExists = await TableExistsAsync(connectionString, CrudTableName)
                .ConfigureAwait(false);

            // Assert
            Assert.True(createdEntity.Id > 0);
            Assert.Equal("initial", initialName);
            Assert.Equal("updated", updatedName);
            Assert.Equal(0, remainingCount);
            Assert.True(historyExists);
            Assert.True(migrationProbeExists);
            Assert.True(crudTableExists);
        }
        finally
        {
            await ResetDatabaseObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies that the batching baseline stays singular and parameterized for generated-value inserts.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Save_changes_uses_singular_parameterized_insert_commands_for_generated_values()
    {
        // Arrange
        var connectionString = GetConnectionString();
        var interceptor = new CommandCaptureInterceptor();

        await ResetDatabaseObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new MySql84CrudContext(CreateOptions(connectionString, interceptor));

            await context
                .Database.ExecuteSqlRawAsync(
                    """
                    CREATE TABLE `Phase1CrudEntities` (
                        `Id` int NOT NULL AUTO_INCREMENT,
                        `Name` longtext NOT NULL,
                        CONSTRAINT `PK_Phase1CrudEntities` PRIMARY KEY (`Id`)
                    ) CHARACTER SET utf8mb4;
                    """)
                .ConfigureAwait(false);

            context.Entities.AddRange(
                new MySql84CrudEntity
                {
                    Name = "first",
                },
                new MySql84CrudEntity
                {
                    Name = "second",
                });

            // Act
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            var insertCommands = interceptor
                .Commands.Where(command => command.CommandText.Contains(
                    "INSERT INTO `Phase1CrudEntities`",
                    StringComparison.Ordinal))
                .ToList();

            // Assert
            // WHY: AffectedCountModificationCommandBatch may combine inserts;
            // the contract is parameterization, independent of command count.
            Assert.True(insertCommands.Count >= 1, $"Expected at least 1 insert command, got {insertCommands.Count}");
            Assert.All(
                insertCommands,
                command =>
                {
                    Assert.Contains("@", command.CommandText, StringComparison.Ordinal);
                    Assert.DoesNotContain("'first'", command.CommandText, StringComparison.Ordinal);
                    Assert.DoesNotContain("'second'", command.CommandText, StringComparison.Ordinal);
                    Assert.True(command.ParameterCount > 0);
                });
        }
        finally
        {
            await ResetDatabaseObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }
}
