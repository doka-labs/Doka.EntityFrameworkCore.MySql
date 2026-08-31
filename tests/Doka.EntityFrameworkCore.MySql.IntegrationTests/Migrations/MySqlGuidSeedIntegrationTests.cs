namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Executes provider-generated mixed-format Guid seed operations and their
/// relationship constraints against every supported server.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlGuidSeedIntegrationTests
{
    private static readonly Guid s_firstPrincipalId = new("bf1da273-beed-4197-ab57-4cf8395244d4");
    private static readonly Guid s_secondPrincipalId = new("a5e91c65-450d-47fa-9683-b6471d3df651");

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_executes_guid_seed_relationships() =>
        AssertGuidSeedRelationshipsAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_executes_guid_seed_relationships() =>
        AssertGuidSeedRelationshipsAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_executes_guid_seed_relationships() =>
        AssertGuidSeedRelationshipsAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_executes_guid_seed_relationships() =>
        AssertGuidSeedRelationshipsAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_executes_guid_seed_relationships() =>
        AssertGuidSeedRelationshipsAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_executes_guid_seed_relationships() =>
        AssertGuidSeedRelationshipsAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task AssertGuidSeedRelationshipsAsync(
        IntegrationDatabaseTarget target
    )
    {
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

            Assert.Single(first.RequiredDependents);
            Assert.Same(first, first.RequiredDependents.Single().Principal);
            Assert.Empty(first.OptionalDependents);
            Assert.Single(second.RequiredDependents);
            Assert.Single(second.OptionalDependents);
            Assert.Same(second, second.OptionalDependents.Single().OptionalPrincipal);
            Assert.Null(second.RequiredDependents.Single().OptionalPrincipalId);

            await AssertPhysicalGuidStorageAsync(connection).ConfigureAwait(false);

            await using (var delete = connection.CreateCommand())
            {
                delete.CommandText = $"DELETE FROM `{GuidSeedIntegrationContract.PrincipalTable}` WHERE `Id` = @id;";
                delete.Parameters.AddWithValue("@id", s_firstPrincipalId.ToString("D", CultureInfo.InvariantCulture));
                Assert.Equal(1, await delete.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false));
            }

            context.ChangeTracker.Clear();
            Assert.False(
                await context
                    .Set<GuidSeedIntegrationDependent>()
                    .AnyAsync(dependent => dependent.PrincipalId == s_firstPrincipalId, CancellationToken.None)
                    .ConfigureAwait(false));
        }
        finally
        {
            await CleanupAsync(connection).ConfigureAwait(false);
        }
    }

    private static async Task AssertPhysicalGuidStorageAsync(
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
        var nullCount = 0;
        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            observedIds.Add(reader.GetString(0));
            Assert.Equal(16, reader.GetInt32(1));
            nullCount += reader.GetBoolean(2) ? 1 : 0;
        }

        Assert.Equal(
            [s_secondPrincipalId.ToString("D", CultureInfo.InvariantCulture),
                s_firstPrincipalId.ToString("D", CultureInfo.InvariantCulture)],
            observedIds.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        Assert.Equal(1, nullCount);
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
    public const string PrincipalTable = "DokaGuidSeedPrincipals";
    public const string DependentTable = "DokaGuidSeedDependents";
}

internal sealed class GuidSeedIntegrationContext : DbContext
{
    public GuidSeedIntegrationContext(
        DbContextOptions<GuidSeedIntegrationContext> options
    ) : base(options) { }

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
    public Guid Id { get; set; }

    public Guid Binary16 { get; set; }

    public Guid? OptionalChar36 { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<GuidSeedIntegrationDependent> RequiredDependents { get; } = [];

    public ICollection<GuidSeedIntegrationDependent> OptionalDependents { get; } = [];
}

internal sealed class GuidSeedIntegrationDependent
{
    public int Id { get; set; }

    public Guid PrincipalId { get; set; }

    public Guid? OptionalPrincipalId { get; set; }

    public string Name { get; set; } = string.Empty;

    public GuidSeedIntegrationPrincipal Principal { get; set; } = null!;

    public GuidSeedIntegrationPrincipal? OptionalPrincipal { get; set; }
}
