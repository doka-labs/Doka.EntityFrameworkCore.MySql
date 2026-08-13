namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Anchors an official EF relational specification test to the supported MySQL 8.4 LTS target.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlSpecTestAnchorTests
{
    /// <summary>
    /// Verifies that the provider can execute a narrow migrations-infrastructure specification anchor
    /// against the supported MySQL 8.4 baseline independently of the broader functional suite.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Migration_infrastructure_spec_anchor_executes_against_mysql84()
    {
        await IntegrationDatabaseUtilities
            .EnsureDatabaseExistsAsync(
                IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84))
            .ConfigureAwait(false);

        var fixture = new MySqlMigrationsInfrastructureFixture();

        await fixture
            .InitializeAsync()
            .ConfigureAwait(false);

        try
        {
            var anchor = new MySqlMigrationsInfrastructureAnchor(fixture);

            anchor.Can_get_active_provider();
            await anchor
                .Can_generate_no_migration_script()
                .ConfigureAwait(false);
        }
        finally
        {
            await fixture
                .DisposeAsync()
                .ConfigureAwait(false);
            await IntegrationDatabaseUtilities
                .EnsureDatabaseExistsAsync(
                    IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84))
                .ConfigureAwait(false);
        }
    }

    private sealed class
        MySqlMigrationsInfrastructureAnchor : MigrationsInfrastructureTestBase<MySqlMigrationsInfrastructureFixture>
    {
        private const int DefaultCommandTimeoutSeconds = 30;

        public MySqlMigrationsInfrastructureAnchor(
            MySqlMigrationsInfrastructureFixture fixture
        ) : base(fixture) { }

        public override void Can_diff_against_2_2_model() { }

        public override void Can_diff_against_3_0_ASP_NET_Identity_model() { }

        public override void Can_diff_against_2_2_ASP_NET_Identity_model() { }

        public override void Can_diff_against_2_1_ASP_NET_Identity_model() { }

        protected override async Task ExecuteSqlAsync(
            string sql
        )
        {
            await using var connection = new MySqlConnection(Fixture.TestStore.ConnectionString);
            await connection
                .OpenAsync()
                .ConfigureAwait(false);

            await MySqlClientScriptExecutor
                .ExecuteAsync(connection, sql, DefaultCommandTimeoutSeconds)
                .ConfigureAwait(false);
        }
    }

    private sealed class MySqlMigrationsInfrastructureFixture : MigrationsInfrastructureFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlSpecTestStoreFactory.Instance;

        protected override string StoreName => "DokaMySqlPhase1MigrationsInfrastructure";
    }

    private sealed class MySqlSpecTestStoreFactory : RelationalTestStoreFactory
    {
        public static MySqlSpecTestStoreFactory Instance { get; } = new();

        public override TestStore Create(
            string storeName
        ) => new MySqlSpecTestStore(storeName);

        public override TestStore GetOrCreate(
            string storeName
        ) => new MySqlSpecTestStore(storeName);

        public override IServiceCollection AddProviderServices(
            IServiceCollection serviceCollection
        ) => serviceCollection.AddEntityFrameworkDokaMySql();
    }

    private sealed class MySqlSpecTestStore : RelationalTestStore
    {
        public MySqlSpecTestStore(
            string name
        ) : base(name, shared: true, new MySqlConnection(BuildConnectionString())) { }

        public override DbContextOptionsBuilder AddProviderOptions(
            DbContextOptionsBuilder builder
        ) => builder.UseMySql(BuildConnectionString(), MySqlServerVersion.MySql(new Version(8, 4, 0)));

        private static string BuildConnectionString() => IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
    }
}
