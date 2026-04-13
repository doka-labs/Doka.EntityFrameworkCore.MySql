namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Anchors an official EF relational specification test to the supported Phase 1 MySQL 8.4 baseline.
/// </summary>
public sealed class MySqlSpecTestAnchorTests
{
    /// <summary>
    /// Verifies that the provider can execute a narrow migrations-infrastructure specification anchor
    /// against the supported MySQL 8.4 baseline without claiming broader Phase 2 semantics.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Migration_infrastructure_spec_anchor_executes_against_mysql84()
    {
        await IntegrationDatabaseUtilities
            .EnsureDatabaseExistsAsync(
                IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84)
            )
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
                    IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84)
                )
                .ConfigureAwait(false);
        }
    }

    private sealed class MySqlMigrationsInfrastructureAnchor
        : MigrationsInfrastructureTestBase<MySqlMigrationsInfrastructureFixture>
    {
        public MySqlMigrationsInfrastructureAnchor(
            MySqlMigrationsInfrastructureFixture fixture
        )
            : base(fixture) { }

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

            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            await command
                .ExecuteNonQueryAsync()
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
        )
        {
            return new MySqlSpecTestStore(storeName);
        }

        public override TestStore GetOrCreate(
            string storeName
        )
        {
            return new MySqlSpecTestStore(storeName);
        }

        public override IServiceCollection AddProviderServices(
            IServiceCollection serviceCollection
        )
        {
            return serviceCollection.AddEntityFrameworkDokaMySql();
        }
    }

    private sealed class MySqlSpecTestStore : RelationalTestStore
    {
        public MySqlSpecTestStore(
            string name
        )
            : base(name, shared: true, new MySqlConnection(BuildConnectionString())) { }

        public override DbContextOptionsBuilder AddProviderOptions(
            DbContextOptionsBuilder builder
        )
        {
            return builder.UseMySql(
                BuildConnectionString(),
                MySqlServerVersion.MySql(new Version(8, 4, 0))
            );
        }

        private static string BuildConnectionString()
        {
            return IntegrationTestEnvironment.GetConnectionString(
                IntegrationDatabaseTarget.MySql84
            );
        }
    }
}
