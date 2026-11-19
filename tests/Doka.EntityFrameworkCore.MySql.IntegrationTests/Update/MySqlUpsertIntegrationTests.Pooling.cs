namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlUpsertIntegrationTests
{
    /// <summary>Real context leases and a one-session driver pool preserve all three connection ownership paths.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(ConnectionCases))]
    public async Task Context_and_driver_pool_reuse_do_not_retain_upsert_command_or_tracking_state(
        string target,
        string path
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "unique-control", CancellationToken.None);
        var connectionString = new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            Pooling = true,
            MinimumPoolSize = 1,
            MaximumPoolSize = 1,
        }.ConnectionString;

        await using var borrowed = new MySqlConnection(connectionString);
        await using var dataSource = new MySqlDataSourceBuilder(connectionString).Build();
        var capture = new Writes();
        var builder = IntegrationTestDbContextOptions
            .Create<PooledUpsertContext>()
            .AddInterceptors(capture);

        if (path == "borrowed-connection")
        {
            await borrowed.OpenAsync(CancellationToken.None);
            builder.UseMySql(borrowed, store.Version);
        }
        else if (path == "data-source")
        {
            builder.UseMySql(dataSource, store.Version);
        }
        else
        {
            builder.UseMySql(connectionString, store.Version);
        }

        var factory = new PooledDbContextFactory<PooledUpsertContext>(builder.Options, poolSize: 1);
        Guid firstInstance;
        int firstLease;
        long firstAffected;
        long secondAffected;
        Guid secondInstance;
        int secondLease;
        bool secondTrackerWasEmpty;
        UniqueRow persisted;
        MySqlConnection? firstNativeConnection = null;
        MySqlConnection secondNativeConnection;
        string? effectivePoolConnectionString = null;

        try
        {
            // Act
            await using (var first = await factory.CreateDbContextAsync(CancellationToken.None))
            {
                firstInstance = first.ContextId.InstanceId;
                firstLease = first.ContextId.Lease;
                firstNativeConnection = (MySqlConnection)first.Database.GetDbConnection();

                if (path == "connection-string")
                {
                    // WHY: Provider normalization adds ApplicationName to this pool identity.
                    // Capture before Open can hide credentials from ConnectionString.
                    effectivePoolConnectionString = firstNativeConnection.ConnectionString;
                }

                firstAffected = await first.ExecuteUpsertAsync(new UniqueRow
                    {
                        Id = 1,
                        First = "a",
                        Second = "b",
                        Value = "first",
                    },
                    new[] { nameof(UniqueRow.Value) },
                    CancellationToken.None);
            }

            await using (var second = await factory.CreateDbContextAsync(CancellationToken.None))
            {
                secondInstance = second.ContextId.InstanceId;
                secondLease = second.ContextId.Lease;
                secondNativeConnection = (MySqlConnection)second.Database.GetDbConnection();
                secondTrackerWasEmpty = !second
                    .ChangeTracker
                    .Entries()
                    .Any();

                secondAffected = await second.ExecuteUpsertAsync(new UniqueRow
                    {
                        Id = 1,
                        First = "a",
                        Second = "b",
                        Value = "second",
                    },
                    new[] { nameof(UniqueRow.Value) },
                    CancellationToken.None);

                persisted = await second
                    .Set<UniqueRow>()
                    .AsNoTracking()
                    .SingleAsync(CancellationToken.None);
            }

            // Assert
            Assert.Equal(firstInstance, secondInstance);
            Assert.NotEqual(firstLease, secondLease);
            Assert.Same(firstNativeConnection, secondNativeConnection);
            Assert.True(secondTrackerWasEmpty);
            Assert.Equal(1, firstAffected);
            Assert.Equal(2, secondAffected);
            Assert.Equal("second", persisted.Value);
            Assert.Equal(2, capture.Statements.Count);
            Assert.Equal(capture.ServerThreads[0], capture.ServerThreads[1]);
            Assert.Equal(path == "borrowed-connection"
                    ? ConnectionState.Open
                    : ConnectionState.Closed,
                borrowed.State);
        }
        finally
        {
            // WHY: Clear the actual normalized connection-string pool, not the different
            // raw borrowed identity. Data-source and borrowed ownership remain separate.
            if (effectivePoolConnectionString is { } poolIdentity)
            {
                await using var effectivePool = new MySqlConnection(poolIdentity);
                await MySqlConnection.ClearPoolAsync(effectivePool, CancellationToken.None);
            }
            else
            {
                await MySqlConnection.ClearPoolAsync(borrowed, CancellationToken.None);
            }
        }
    }

    private sealed class PooledUpsertContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public PooledUpsertContext(
            DbContextOptions<PooledUpsertContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder builder
        )
        {
            builder.HasCharSet("utf8mb4");
            builder.UseCollation("utf8mb4_unicode_ci");
            var row = builder.Entity<UniqueRow>();
            row.ToTable("UpsertUnique");
            row.HasKey(value => value.Id);
            row
                .Property(value => value.Id)
                .ValueGeneratedNever();

            row
                .Property(value => value.First)
                .HasMaxLength(120);

            row
                .Property(value => value.Second)
                .HasMaxLength(120);

            row
                .HasIndex(value => value.First)
                .IsUnique();

            row
                .HasIndex(value => value.Second)
                .IsUnique();
        }
    }
}
