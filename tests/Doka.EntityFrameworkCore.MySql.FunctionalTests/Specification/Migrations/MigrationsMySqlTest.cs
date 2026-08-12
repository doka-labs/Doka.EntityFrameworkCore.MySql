using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Migrations;

/// <summary>
/// Migrations-infrastructure specification subclass. Exercises the standard EF Core migration
/// runner (apply, generate-script, revert, idempotent-script) against the MySQL provider's
/// <see cref="MySqlHistoryRepository"/> + <c>__EFMigrationsHistory</c> table contract.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class MigrationsMySqlTest : MigrationsInfrastructureTestBase<MigrationsMySqlTest.MigrationsMySqlFixture>
{
    // Structural-skip rationale for the four Can_diff_against_X_X(_Identity)_model overrides
    // below: each upstream test verifies that an EF-Core-X.Y-era ModelSnapshot diffs to zero
    // operations against the current model. The premise is that the provider existed during
    // that prior EF Core version and produced a real-world snapshot artifact. Doka's first
    // release is on EF Core 10; no prior-version Doka-MySQL snapshot exists in the wild, so
    // a hand-fabricated snapshot would only verify symmetry with the fabrication itself
    // (round-tripping our own assumptions). Recorded as structurally inapplicable in the
    // machine-readable specification disposition ledger per ADR D-021.
    private const string SnapshotInapplicableReason =
        "[spec-not-applicable:DOKA-HISTORICAL-SNAPSHOT-NOT-APPLICABLE] "
        + "Doka first ships on EF Core 10; no prior-EF-Core-version model snapshots exist for this provider. "
        + "See ADR D-021 and SpecDispositions.json.";

    public MigrationsMySqlTest(
        MigrationsMySqlFixture fixture
    ) : base(fixture)
    {
    }

    protected override async Task ExecuteSqlAsync(
        string value
    )
    {
        var connectionString = ((MySqlTestStore)Fixture.TestStore).ConnectionString;
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await MySqlClientScriptExecutor.ExecuteAsync(
            connection,
            value,
            MySqlTestStore.DefaultCommandTimeout);
    }

    [Fact(Skip = SnapshotInapplicableReason)]
    public override void Can_diff_against_2_2_model()
    {
        // Skipped per attribute.
    }

    [Fact(Skip = SnapshotInapplicableReason)]
    public override void Can_diff_against_2_1_ASP_NET_Identity_model()
    {
        // Skipped per attribute.
    }

    [Fact(Skip = SnapshotInapplicableReason)]
    public override void Can_diff_against_2_2_ASP_NET_Identity_model()
    {
        // Skipped per attribute.
    }

    [Fact(Skip = SnapshotInapplicableReason)]
    public override void Can_diff_against_3_0_ASP_NET_Identity_model()
    {
        // Skipped per attribute.
    }

    /// <summary>
    /// The base spec test opens the user transaction via
    /// <c>using var transaction = db.Database.BeginTransactionAsync();</c> -- without
    /// <c>await</c>. The result is a <see cref="Task{T}"/> that is disposed as if it were the
    /// transaction itself; the actual <see cref="IDbContextTransaction"/> never enters scope.
    /// On providers whose <c>BeginTransactionAsync</c> completes synchronously the transaction
    /// is open by the time the migrator runs, so <c>MigrationsUserTransactionWarning</c> fires
    /// and the migration proceeds inside the user transaction. MySqlConnector's
    /// <c>BeginTransactionAsync</c> is truly async; the pending task races the migrator's
    /// <c>CurrentTransaction is null</c> check and the migrator's own
    /// <c>BeginTransactionAsync</c> then throws <c>TransactionAlreadyStarted</c>. Awaiting the
    /// task explicitly removes the race and matches what the test author almost certainly meant.
    /// </summary>
    public override async Task Can_apply_two_migrations_in_transaction_async()
    {
        using var db = Fixture.CreateContext();
        await db.Database.EnsureDeletedAsync();
        await db.GetService<IRelationalDatabaseCreator>().CreateAsync();

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("Migration1");
            await migrator.MigrateAsync("Migration2");

            var history = db.GetService<IHistoryRepository>();
            Assert.Collection(
                await history.GetAppliedMigrationsAsync(),
                x => Assert.Equal("00000000000001_Migration1", x.MigrationId),
                x => Assert.Equal("00000000000002_Migration2", x.MigrationId));
        });

        Assert.Equal(
            LogLevel.Warning,
            Fixture.TestSqlLoggerFactory.Log.First(
                l => l.Id == RelationalEventId.MigrationsUserTransactionWarning).Level);
    }

    public class MigrationsMySqlFixture : MigrationsInfrastructureFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => PerContextConnectionFactory.Instance;
    }

    /// <summary>
    /// Test-store factory variant that produces a <see cref="MySqlTestStore"/> with
    /// <see cref="MySqlTestStore.UseSharedConnectionInProviderOptions"/> set to
    /// <see langword="false"/>. The Migrations spec tests spawn
    /// <c>Parallel.For(0, Environment.ProcessorCount, ...)</c> across freshly created contexts
    /// and call <c>Migrate</c> on each; the shared-connection default would race them on a
    /// single <see cref="DbConnection"/> and surface as <c>Cannot Open when State is Connecting</c>.
    /// </summary>
    private sealed class PerContextConnectionFactory : MySqlTestStoreFactory
    {
        public static new PerContextConnectionFactory Instance { get; } = new();

        public override TestStore Create(
            string storeName
        ) => new PerContextConnectionTestStore(storeName, shared: false);

        public override TestStore GetOrCreate(
            string storeName
        ) => new PerContextConnectionTestStore(storeName, shared: true);
    }

    private sealed class PerContextConnectionTestStore : MySqlTestStore
    {
        public PerContextConnectionTestStore(
            string name,
            bool shared
        ) : base(name, shared) { }

        public override bool UseSharedConnectionInProviderOptions => false;
    }
}
