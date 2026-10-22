namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Executes provider-generated mixed-format Guid seed operations and their
/// relationship constraints against every supported server.
/// </summary>
[IntegrationDatabaseTest]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlGuidSeedIntegrationTests
{
    private static readonly Guid s_firstPrincipalId = new("bf1da273-beed-4197-ab57-4cf8395244d4");
    private static readonly Guid s_secondPrincipalId = new("a5e91c65-450d-47fa-9683-b6471d3df651");

    /// <summary>
    /// Verifies my sql84 executes Guid seed relationships.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_executes_guid_seed_relationships() =>
        AssertGuidSeedRelationshipsAsync(IntegrationDatabaseTarget.MySql84);

    /// <summary>
    /// Verifies my sql97 executes Guid seed relationships.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_executes_guid_seed_relationships() =>
        AssertGuidSeedRelationshipsAsync(IntegrationDatabaseTarget.MySql97);

    /// <summary>
    /// Verifies maria db1011 executes Guid seed relationships.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_executes_guid_seed_relationships() =>
        AssertGuidSeedRelationshipsAsync(IntegrationDatabaseTarget.MariaDb1011);

    /// <summary>
    /// Verifies maria db114 executes Guid seed relationships.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_executes_guid_seed_relationships() =>
        AssertGuidSeedRelationshipsAsync(IntegrationDatabaseTarget.MariaDb114);

    /// <summary>
    /// Verifies maria db118 executes Guid seed relationships.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_executes_guid_seed_relationships() =>
        AssertGuidSeedRelationshipsAsync(IntegrationDatabaseTarget.MariaDb118);

    /// <summary>
    /// Verifies maria db123 executes Guid seed relationships.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_executes_guid_seed_relationships() =>
        AssertGuidSeedRelationshipsAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task AssertGuidSeedRelationshipsAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        var connectionString = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(target))
        {
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            Pooling = false,
        }.ConnectionString;

        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await CleanupAsync(connection).ConfigureAwait(false);

        try
        {
            await using var context = new GuidSeedIntegrationContext(
                IntegrationTestDbContextOptions
                    .Create<GuidSeedIntegrationContext>()
                    .UseMySql(
                        connection,
                        serverVersion,
                        options => options.DefaultGuidFormat(MySqlGuidFormat.Char36))
                    .Options);

            // Act
            var model = context.GetService<IDesignTimeModel>().Model;
            var operations = context
                .GetService<IMigrationsModelDiffer>()
                .GetDifferences(null, model.GetRelationalModel());

            var generator = context.GetService<IMigrationsSqlGenerator>();
            var relationalConnection = context.GetService<IRelationalConnection>();

            foreach (var command in generator.Generate(operations, model))
            {
                _ = await command
                    .ExecuteNonQueryAsync(relationalConnection, cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
            }

            context.ChangeTracker.Clear();
            var first = await context
                .Set<GuidSeedIntegrationPrincipal>()
                .Include(principal => principal.RequiredDependents)
                .Include(principal => principal.OptionalDependents)
                .SingleAsync(principal => principal.Id == s_firstPrincipalId, CancellationToken.None)
                .ConfigureAwait(false);

            var second = await context
                .Set<GuidSeedIntegrationPrincipal>()
                .Include(principal => principal.RequiredDependents)
                .Include(principal => principal.OptionalDependents)
                .SingleAsync(principal => principal.Id == s_secondPrincipalId, CancellationToken.None)
                .ConfigureAwait(false);

            var physicalStorage = await ReadPhysicalGuidStorageAsync(connection).ConfigureAwait(false);
            int deletedRows;

            await using (var delete = connection.CreateCommand())
            {
                delete.CommandText = $"DELETE FROM `{GuidSeedIntegrationContract.PrincipalTable}` WHERE `Id` = @id;";
                delete.Parameters.AddWithValue("@id", s_firstPrincipalId.ToString("D", CultureInfo.InvariantCulture));
                deletedRows = await delete.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }

            context.ChangeTracker.Clear();
            var dependentRemains = await context
                    .Set<GuidSeedIntegrationDependent>()
                    .AnyAsync(dependent => dependent.PrincipalId == s_firstPrincipalId, CancellationToken.None)
                    .ConfigureAwait(false);

            // Assert
            Assert.Single(first.RequiredDependents);
            Assert.Same(first, first.RequiredDependents.Single().Principal);
            Assert.Empty(first.OptionalDependents);
            Assert.Single(second.RequiredDependents);
            Assert.Single(second.OptionalDependents);
            Assert.Same(second, second.OptionalDependents.Single().OptionalPrincipal);
            Assert.Null(second.RequiredDependents.Single().OptionalPrincipalId);
            Assert.Equal(
                [s_secondPrincipalId.ToString("D", CultureInfo.InvariantCulture),
                    s_firstPrincipalId.ToString("D", CultureInfo.InvariantCulture)],
                physicalStorage.Ids.OrderBy(value => value, StringComparer.Ordinal).ToArray());
            Assert.All(physicalStorage.BinaryLengths, length => Assert.Equal(16, length));
            Assert.Equal(1, physicalStorage.NullCount);
            Assert.Equal(1, deletedRows);
            Assert.False(dependentRemains);
        }
        finally
        {
            await CleanupAsync(connection).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Captures physical Guid values before the lifecycle test deletes its principal row.
    /// </summary>
    private static async Task<(IReadOnlyList<string> Ids, IReadOnlyList<int> BinaryLengths, int NullCount)>
        ReadPhysicalGuidStorageAsync(
        MySqlConnection connection
    )
    {

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT LOWER(`Id`), OCTET_LENGTH(`Binary16`), `OptionalChar36` IS NULL "
            + $"FROM `{GuidSeedIntegrationContract.PrincipalTable}` ORDER BY `Name`;";

        await using var reader = await command
            .ExecuteReaderAsync(CancellationToken.None)
            .ConfigureAwait(false);

        var observedIds = new List<string>();
        var binaryLengths = new List<int>();
        var nullCount = 0;
        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            observedIds.Add(reader.GetString(0));
            binaryLengths.Add(reader.GetInt32(1));
            nullCount += reader.GetBoolean(2) ? 1 : 0;
        }

        return (observedIds, binaryLengths, nullCount);
    }

    private static async Task CleanupAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{GuidSeedIntegrationContract.DependentTable}`; "
            + $"DROP TABLE IF EXISTS `{GuidSeedIntegrationContract.PrincipalTable}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

internal static class GuidSeedIntegrationContract
{
    /// <summary>
    /// The principal table used by this migration fixture.
    /// </summary>
    public const string PrincipalTable = "DokaGuidSeedPrincipals";

    /// <summary>
    /// The dependent table used by this migration fixture.
    /// </summary>
    public const string DependentTable = "DokaGuidSeedDependents";
}

internal sealed class GuidSeedIntegrationContext : DbContext
{
    /// <summary>
    /// Initializes the GuidSeedIntegrationContext fixture.
    /// </summary>
    public GuidSeedIntegrationContext(
        DbContextOptions<GuidSeedIntegrationContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.HasCharSet("utf8mb4");

        modelBuilder.Entity<GuidSeedIntegrationPrincipal>(entity =>
        {
            entity.ToTable(GuidSeedIntegrationContract.PrincipalTable);
            entity.HasKey(principal => principal.Id);
            entity
                .Property(principal => principal.Binary16)
                .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
            entity.HasData(
                new GuidSeedIntegrationPrincipal
                {
                    Id = new Guid("bf1da273-beed-4197-ab57-4cf8395244d4"),
                    Binary16 = new Guid("89a78261-ea26-494e-a520-b518f51ed3d1"),
                    OptionalChar36 = new Guid("1e15bed0-86cb-408e-b1cf-08952340a095"),
                    Name = "first",
                },
                new GuidSeedIntegrationPrincipal
                {
                    Id = new Guid("a5e91c65-450d-47fa-9683-b6471d3df651"),
                    Binary16 = new Guid("79cf7ff0-1bb0-4007-8ef4-b345386a6f41"),
                    OptionalChar36 = null,
                    Name = "second",
                });
        });

        modelBuilder.Entity<GuidSeedIntegrationDependent>(entity =>
        {
            entity.ToTable(GuidSeedIntegrationContract.DependentTable);
            entity.HasKey(dependent => dependent.Id);
            entity
                .HasOne(dependent => dependent.Principal)
                .WithMany(principal => principal.RequiredDependents)
                .HasForeignKey(dependent => dependent.PrincipalId)
                .OnDelete(DeleteBehavior.Cascade);
            entity
                .HasOne(dependent => dependent.OptionalPrincipal)
                .WithMany(principal => principal.OptionalDependents)
                .HasForeignKey(dependent => dependent.OptionalPrincipalId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasData(
                new GuidSeedIntegrationDependent
                {
                    Id = 1,
                    PrincipalId = new Guid("bf1da273-beed-4197-ab57-4cf8395244d4"),
                    OptionalPrincipalId = new Guid("a5e91c65-450d-47fa-9683-b6471d3df651"),
                    Name = "related",
                },
                new GuidSeedIntegrationDependent
                {
                    Id = 2,
                    PrincipalId = new Guid("a5e91c65-450d-47fa-9683-b6471d3df651"),
                    OptionalPrincipalId = null,
                    Name = "nullable-related",
                });
        });
    }
}

internal sealed class GuidSeedIntegrationPrincipal
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the binary16.
    /// </summary>
    public Guid Binary16 { get; set; }

    /// <summary>
    /// Gets or sets the optional char36.
    /// </summary>
    public Guid? OptionalChar36 { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets the required dependents.
    /// </summary>
    public ICollection<GuidSeedIntegrationDependent> RequiredDependents { get; } = [];

    /// <summary>
    /// Gets the optional dependents.
    /// </summary>
    public ICollection<GuidSeedIntegrationDependent> OptionalDependents { get; } = [];
}

internal sealed class GuidSeedIntegrationDependent
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the principal id.
    /// </summary>
    public Guid PrincipalId { get; set; }

    /// <summary>
    /// Gets or sets the optional principal id.
    /// </summary>
    public Guid? OptionalPrincipalId { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the principal.
    /// </summary>
    public GuidSeedIntegrationPrincipal Principal { get; set; } = null!;

    /// <summary>
    /// Gets or sets the optional principal.
    /// </summary>
    public GuidSeedIntegrationPrincipal? OptionalPrincipal { get; set; }
}
