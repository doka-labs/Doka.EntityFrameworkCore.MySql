namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    private const string OptionalOwnedOwnerTable = "IntBoundOptionalOwners";
    private const string OptionalOwnedProfileTable = "IntBoundOptionalProfiles";

    /// <summary>Verifies the optional separate owned materialization contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertOptionalSeparateOwnedMaterializationAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await using var fixture = await CreateOptionalOwnedFixtureAsync(target, cancellationToken);

        // Act
        var query = fixture.Context.Owners.WithMaterializedSource(source => source
            .Where(owner => owner.Id <= 2)
            .OrderBy(owner => owner.Id));

        var actual = await query.ToArrayAsync(cancellationToken);

        // Assert
        Assert.Equal([1, 2], actual.Select(owner => owner.Id));
        Assert.Equal(["owner-1", "owner-2"], actual.Select(owner => owner.Label));
        var profile = Assert.IsType<OptionalOwnedProfile>(actual[0].Profile);
        Assert.Equal("owned-1", profile.Name);
        Assert.Equal(s_binaryToken, profile.Token);
        Assert.Null(actual[1].Profile);
        Assert.Equal(2, fixture.Context.ChangeTracker.Entries<OptionalOwnedOwner>().Count());
        Assert.Single(fixture.Context.ChangeTracker.Entries<OptionalOwnedProfile>());
        Assert.Equal(EntityState.Unchanged, fixture.Context.Entry(profile).State);
        Assert.Equal(1, fixture.Probe.Count);
    }

    /// <summary>Creates the optional owned fixture with cancellable database work.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task<OptionalOwnedFixture> CreateOptionalOwnedFixtureAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        var probe = new BindingCommandProbe();
        var options = IntegrationTestDbContextOptions.Create<OptionalOwnedContext>()
            .UseMySql(
                IntegrationTestEnvironment.GetConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target))
            .AddInterceptors(probe)
            .Options;

        var context = new OptionalOwnedContext(options);

        try
        {
            await DropOptionalOwnedTablesAsync(context, cancellationToken);
            await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), cancellationToken);
            context.Owners.AddRange(
                new OptionalOwnedOwner
                {
                    Id = 1,
                    Label = "owner-1",
                    Profile = new OptionalOwnedProfile { Name = "owned-1", Token = s_binaryToken },
                },
                new OptionalOwnedOwner { Id = 2, Label = "owner-2" },
                new OptionalOwnedOwner
                {
                    Id = 3,
                    Label = "excluded-owner",
                    Profile = new OptionalOwnedProfile { Name = "excluded-profile", Token = Guid.Empty },
                });
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
            probe.Reset();

            return new OptionalOwnedFixture(context, probe);
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    /// <summary>Drops the optional owned tables using the caller's cancellation policy.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static Task<int> DropOptionalOwnedTablesAsync(
        OptionalOwnedContext context,
        CancellationToken cancellationToken
    ) => context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{OptionalOwnedProfileTable}`; DROP TABLE IF EXISTS `{OptionalOwnedOwnerTable}`;",
        cancellationToken);

    private sealed class OptionalOwnedFixture(
        OptionalOwnedContext context,
        BindingCommandProbe probe
    ) : IAsyncDisposable
    {
        public OptionalOwnedContext Context { get; } = context;

        public BindingCommandProbe Probe { get; } = probe;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await DropOptionalOwnedTablesAsync(Context, CancellationToken.None);
            }
            finally
            {
                await Context.DisposeAsync();
            }
        }
    }

    private sealed class OptionalOwnedContext(
        DbContextOptions<OptionalOwnedContext> options
    ) : DbContext(options)
    {
        public DbSet<OptionalOwnedOwner> Owners => Set<OptionalOwnedOwner>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<OptionalOwnedOwner>(entity =>
            {
                entity.ToTable(OptionalOwnedOwnerTable);
                entity.Property(owner => owner.Id).ValueGeneratedNever();
                entity.Property(owner => owner.Label).HasMaxLength(64);
                entity.OwnsOne(owner => owner.Profile, owned =>
                {
                    owned.ToTable(OptionalOwnedProfileTable);
                    owned.Property(profile => profile.Name).HasMaxLength(64);
                    owned.Property(profile => profile.Token).HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                });
                entity.Navigation(owner => owner.Profile).IsRequired(false);
            });
        }
    }

    private sealed class OptionalOwnedOwner
    {
        public int Id { get; set; }

        public string Label { get; set; } = string.Empty;

        public OptionalOwnedProfile? Profile { get; set; }
    }

    private sealed class OptionalOwnedProfile
    {
        public string Name { get; set; } = string.Empty;

        public Guid Token { get; set; }
    }
}
