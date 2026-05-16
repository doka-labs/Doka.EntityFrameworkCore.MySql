namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the credential-free MySQL 8.0 compatibility baseline against a live server.
/// </summary>
public sealed class MySql80CompatibilityBaselineTests
{
    private const string TableName = "Phase4MySql80Entities";

    /// <summary>
    /// Verifies that the approved repo-local compatibility baseline succeeds against MySQL 8.0.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql80)]
    public async Task MySql80_repo_local_compatibility_baseline_succeeds()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql80);

        await ResetDatabaseObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection
                .OpenAsync()
                .ConfigureAwait(false);

            var detectedServerVersion = MySqlServerVersion.AutoDetect(connection);

            Assert.False(detectedServerVersion.IsMariaDb);
            Assert.Equal(8, detectedServerVersion.Version.Major);
            Assert.Equal(0, detectedServerVersion.Version.Minor);
            Assert.True(detectedServerVersion.Profile.Has(Capability.SupportsNativeJsonType));
            Assert.False(detectedServerVersion.Profile.Has(Capability.UsesJsonAliasForJsonColumns));
            Assert.True(detectedServerVersion.Profile.Has(Capability.SupportsSavepoints));

            await using var context = new MySql80CompatibilityContext(CreateOptions(connectionString));

            await context
                .Database.ExecuteSqlRawAsync(
                    $"""
                     CREATE TABLE `{TableName}` (
                         `Id` int NOT NULL AUTO_INCREMENT,
                         `Name` longtext NOT NULL,
                         CONSTRAINT `PK_{TableName}` PRIMARY KEY (`Id`)
                     ) CHARACTER SET utf8mb4;
                     """)
                .ConfigureAwait(false);

            var entity = new MySql80CompatibilityEntity
            {
                Name = "mysql80",
            };

            context.Entities.Add(entity);
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            Assert.True(entity.Id > 0);

            context.ChangeTracker.Clear();

            var loadedEntity = await context
                .Entities.SingleAsync(candidate => candidate.Id == entity.Id)
                .ConfigureAwait(false);

            Assert.Equal("mysql80", loadedEntity.Name);
        }
        finally
        {
            await ResetDatabaseObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    private static DbContextOptions<MySql80CompatibilityContext> CreateOptions(
        string connectionString
    )
    {
        var optionsBuilder = new DbContextOptionsBuilder<MySql80CompatibilityContext>();

        optionsBuilder.UseMySql(
            connectionString,
            MySqlServerVersion.MySql(new Version(8, 0, 0)),
            options => options.EnableRetryOnFailure(maxRetryCount: 2, maxRetryDelay: TimeSpan.FromMilliseconds(1)));

        return optionsBuilder.Options;
    }

    private static async Task ResetDatabaseObjectsAsync(
        string connectionString
    )
    {
        await IntegrationDatabaseUtilities
            .EnsureDatabaseExistsAsync(connectionString)
            .ConfigureAwait(false);

        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{TableName}`;";

        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private sealed class MySql80CompatibilityContext : DbContext
    {
        public MySql80CompatibilityContext(
            DbContextOptions<MySql80CompatibilityContext> options
        ) : base(options) { }

        public DbSet<MySql80CompatibilityEntity> Entities => Set<MySql80CompatibilityEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<MySql80CompatibilityEntity>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(candidate => candidate.Id);
                entity
                    .Property(candidate => candidate.Id)
                    .UseMySqlAutoIncrementColumn();
                entity.Property(candidate => candidate.Name);
            });
        }
    }

    private sealed class MySql80CompatibilityEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
