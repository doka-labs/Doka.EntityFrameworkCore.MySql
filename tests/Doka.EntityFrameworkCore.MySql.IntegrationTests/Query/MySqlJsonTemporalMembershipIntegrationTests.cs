namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies temporal scalar membership over normally persisted JSON collections.</summary>
[IntegrationDatabaseTest]
public sealed class MySqlJsonTemporalMembershipIntegrationTests
{
    /// <summary>Gets both Unix-time units for each selected supported database profile.</summary>
    public static TheoryData<string, bool> Cases
    {
        get
        {
            var cases = new TheoryData<string, bool>();

            foreach (var target in IntegrationTestEnvironment
                .GetSelectedTargets()
                .Where(target => IntegrationTestEnvironment
                    .GetSupportedTargets()
                    .Contains(target)))
            {
                cases.Add(target.ToString(), false);
                cases.Add(target.ToString(), true);
            }

            return cases;
        }
    }

    /// <summary>Unix-time transformations preserve matches, near misses and empty-collection semantics.</summary>
    /// <param name="targetName">The selected database engine profile.</param>
    /// <param name="milliseconds">Whether membership uses Unix milliseconds rather than seconds.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(Cases))]
    public async Task Unix_time_projection_preserves_native_membership_results(
        string targetName,
        bool milliseconds
    )
    {
        // Arrange
        await using var store = new TemporalStore(Enum.Parse<IntegrationDatabaseTarget>(targetName));
        await using var context = store.CreateContext();
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var epoch = DateTimeOffset.UnixEpoch;

        // WHY: Normal EF persistence exercises the actual DateTimeOffset JSON representation;
        // the empty array and 112-second near miss distinguish exact transformed membership.
        context.Rows.AddRange(new List<TemporalRow>
        {
            new()
            {
                Id = 1,
                Values = [epoch.AddSeconds(1), epoch.AddSeconds(12)],
            },
            new()
            {
                Id = 2,
                Values = [epoch.AddSeconds(2), epoch.AddSeconds(3)],
            },
            new()
            {
                Id = 3,
                Values = [],
            },
            new()
            {
                Id = 4,
                Values = [epoch.AddSeconds(112)],
            },
            new()
            {
                Id = 5,
                Values = [epoch.AddSeconds(12)],
            },
        });

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var matchingRows = milliseconds
            ? context.Rows.Where(row => row
                .Values
                .Select(value => value.ToUnixTimeMilliseconds())
                .Contains(12000L))
            : context.Rows.Where(row => row
                .Values
                .Select(value => value.ToUnixTimeSeconds())
                .Contains(12L));

        var query = matchingRows
            .OrderBy(row => row.Id)
            .Select(row => row.Id);

        var sql = query.ToQueryString();

        // Act
        var ids = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 5], ids);
        Assert.Contains(milliseconds
                ? "TIMESTAMPDIFF(MICROSECOND,"
                : "TIMESTAMPDIFF(SECOND,",
            sql,
            StringComparison.Ordinal);

        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>Maps the same primitive temporal collection used by normal application persistence.</summary>
    private sealed class TemporalContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public TemporalContext(
            DbContextOptions<TemporalContext> options
        ) : base(options) { }

        /// <summary>Gets the temporal collection query root.</summary>
        public DbSet<TemporalRow> Rows => Set<TemporalRow>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TemporalRow>(entity =>
            {
                entity.ToTable("JsonTemporalMembershipRows");
                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity.PrimitiveCollection(row => row.Values);
            });
        }
    }

    /// <summary>Stores exact DateTimeOffset values inside a primitive JSON collection.</summary>
    private sealed class TemporalRow
    {
        /// <summary>Gets or sets the identifier used by the independent result oracle.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets persisted temporal members.</summary>
        public DateTimeOffset[] Values { get; set; } = [];
    }

    /// <summary>Owns only the unique test database and its isolated EF service provider.</summary>
    private sealed class TemporalStore : IAsyncDisposable
    {
        private readonly string _connectionString;

        private readonly ServiceProvider _services;
        private readonly IntegrationDatabaseTarget _target;

        /// <summary>Takes ownership of the supplied database resources and configuration.</summary>
        public TemporalStore(
            IntegrationDatabaseTarget target
        )
        {
            _target = target;
            _connectionString = new MySqlConnectionStringBuilder(
                IntegrationTestEnvironment.GetBaseConnectionString(target))
            {
                Database = "DokaJsonTemporal_"
                    + Guid
                        .NewGuid()
                        .ToString("N", CultureInfo.InvariantCulture),
                Pooling = false,
                AutoEnlist = false,
            }.ConnectionString;

            _services = new ServiceCollection()
                .AddEntityFrameworkDokaMySql()
                .BuildServiceProvider();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            try
            {
                // WHY: Cleanup cannot inherit query cancellation or leave the uniquely owned schema behind.
                await IntegrationDatabaseUtilities.DropDatabaseAsync(_connectionString);
            }
            finally
            {
                await _services.DisposeAsync();
            }
        }

        /// <summary>Creates a context using the test-owned connection and service-provider identity.</summary>
        internal TemporalContext CreateContext()
        {
            var options = IntegrationTestDbContextOptions
                .Create<TemporalContext>()
                .UseInternalServiceProvider(_services)
                .UseMySql(_connectionString, IntegrationTestEnvironment.GetServerVersion(_target))
                .Options;

            return new TemporalContext(options);
        }
    }
}
