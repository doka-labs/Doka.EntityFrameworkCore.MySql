namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySql84CrudBaselineTests
{
    /// <summary>
    /// Verifies that Binary16 and Char36 GUID mappings roundtrip together in the same model.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Binary16_and_char36_guid_mappings_roundtrip_together()
    {
        // Arrange
        var connectionString = GetConnectionString();

        await ResetDatabaseObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new GuidRoundtripContext(CreateGuidRoundtripOptions(connectionString));

            await context
                .Database.ExecuteSqlRawAsync(
                    $"""
                     CREATE TABLE `{BinaryGuidTableName}` (
                         `Id` binary(16) NOT NULL,
                         `Name` longtext NOT NULL,
                         CONSTRAINT `PK_{BinaryGuidTableName}` PRIMARY KEY (`Id`)
                     ) CHARACTER SET utf8mb4;

                     CREATE TABLE `{CharGuidTableName}` (
                         `Id` char(36) NOT NULL,
                         `Name` longtext NOT NULL,
                         CONSTRAINT `PK_{CharGuidTableName}` PRIMARY KEY (`Id`)
                     ) CHARACTER SET utf8mb4;
                     """)
                .ConfigureAwait(false);

            var binaryEntity = new BinaryGuidEntity
            {
                Name = "binary",
            };

            var charEntity = new CharGuidEntity
            {
                Name = "char36",
            };

            context.Add(binaryEntity);
            context.Add(charEntity);

            // Act
            // WHY: Both mappings intentionally share one model and save operation
            // so one property cannot silently change the other property's transport.
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            context.ChangeTracker.Clear();

            var loadedBinary = await context
                .BinaryGuidEntities.SingleAsync(entity => entity.Id == binaryEntity.Id)
                .ConfigureAwait(false);

            var loadedChar = await context
                .CharGuidEntities.SingleAsync(entity => entity.Id == charEntity.Id)
                .ConfigureAwait(false);

            // Assert
            Assert.NotEqual(Guid.Empty, binaryEntity.Id);
            Assert.NotEqual(Guid.Empty, charEntity.Id);
            Assert.Equal(binaryEntity.Id, loadedBinary.Id);
            Assert.Equal("binary", loadedBinary.Name);
            Assert.Equal(charEntity.Id, loadedChar.Id);
            Assert.Equal("char36", loadedChar.Name);
        }
        finally
        {
            await ResetDatabaseObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }
}
