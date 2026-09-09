using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies that an entirely commandless custom migration still participates
/// in EF Core's provider-owned migration-history lifecycle.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlConsumedMigrationOperationIntegrationTests
{
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_records_consumed_only_migration_history() =>
        AssertConsumedOnlyMigrationHistoryAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_records_consumed_only_migration_history() =>
        AssertConsumedOnlyMigrationHistoryAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_records_consumed_only_migration_history() =>
        AssertConsumedOnlyMigrationHistoryAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_records_consumed_only_migration_history() =>
        AssertConsumedOnlyMigrationHistoryAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_records_consumed_only_migration_history() =>
        AssertConsumedOnlyMigrationHistoryAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_records_consumed_only_migration_history() =>
        AssertConsumedOnlyMigrationHistoryAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task AssertConsumedOnlyMigrationHistoryAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(target))
        {
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
        }.ConnectionString;

        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await DropHistoryTableAsync(connection)
            .ConfigureAwait(false);

        try
        {
            await using var context = CreateContext(connection, target);
            var handler = context
                .GetService<IEnumerable<IMySqlMigrationOperationHandler>>()
                .OfType<ConsumedOnlyMigrationOperationHandler>()
                .Single();
            var migrator = context.GetService<IMigrator>();

            Assert.Empty(
                await context.Database
                    .GetAppliedMigrationsAsync(CancellationToken.None)
                    .ConfigureAwait(false));
            Assert.Equal(0, handler.InvocationCount);

            await migrator
                .MigrateAsync(cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Equal(1, handler.InvocationCount);
            Assert.Equal(
                [ConsumedOnlyMigrationContract.MigrationId],
                await context.Database
                    .GetAppliedMigrationsAsync(CancellationToken.None)
                    .ConfigureAwait(false));

            await migrator
                .MigrateAsync(cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Equal(1, handler.InvocationCount);
            Assert.Equal(
                [ConsumedOnlyMigrationContract.MigrationId],
                await context.Database
                    .GetAppliedMigrationsAsync(CancellationToken.None)
                    .ConfigureAwait(false));
        }
        finally
        {
            await DropHistoryTableAsync(connection)
                .ConfigureAwait(false);
        }
    }

    private static ConsumedOnlyMigrationContext CreateContext(
        MySqlConnection connection,
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions
            .Create<ConsumedOnlyMigrationContext>()
            .UseMySql(
                connection,
                IntegrationTestEnvironment.GetServerVersion(target),
                options => options
                    .MigrationsAssembly(typeof(MySqlConsumedMigrationOperationIntegrationTests).Assembly.FullName!)
                    .MigrationsHistoryTable(ConsumedOnlyMigrationContract.HistoryTable));

        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(new ConsumedOnlyMigrationOperationOptionsExtension());

        return new ConsumedOnlyMigrationContext(builder.Options);
    }

    private static async Task DropHistoryTableAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{ConsumedOnlyMigrationContract.HistoryTable}`;";
        _ = await command
            .ExecuteNonQueryAsync(CancellationToken.None)
            .ConfigureAwait(false);
    }

    private sealed class ConsumedOnlyMigrationOperationHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "tests.consumed_only.runtime";

        public int InvocationCount { get; private set; }

        public Type OperationType => typeof(ConsumedOnlyMigrationOperation);

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            InvocationCount++;
            return MySqlMigrationOperationResult.Consumed("consumed_only_migration");
        }
    }

    private sealed class ConsumedOnlyMigrationOperationOptionsExtension : IDbContextOptionsExtension
    {
        private DbContextOptionsExtensionInfo? _info;

        public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

        public void ApplyServices(
            IServiceCollection services
        ) => services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IMySqlMigrationOperationHandler, ConsumedOnlyMigrationOperationHandler>());

        public void Validate(
            IDbContextOptions options
        )
        {
            if (options.FindExtension<MySqlOptionsExtension>() is null)
            {
                throw new InvalidOperationException("The consumed-only migration test requires the Doka MySQL provider.");
            }
        }

        private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
        {
            public ExtensionInfo(
                IDbContextOptionsExtension extension
            ) : base(extension) { }

            public override bool IsDatabaseProvider => false;

            public override string LogFragment => "consumed-only-migration-tests ";

            public override int GetServiceProviderHashCode() => 0;

            public override void PopulateDebugInfo(
                IDictionary<string, string> debugInfo
            ) => debugInfo["Doka:ConsumedOnlyMigrationTests"] = "1";

            public override bool ShouldUseSameServiceProvider(
                DbContextOptionsExtensionInfo other
            ) => other is ExtensionInfo;
        }
    }
}

internal static class ConsumedOnlyMigrationContract
{
    public const string HistoryTable = "__DokaConsumedOnlyMigrationHistory";
    public const string MigrationId = "20260909000000_ConsumedOnlyMigration";
}

internal sealed class ConsumedOnlyMigrationContext : DbContext
{
    public ConsumedOnlyMigrationContext(
        DbContextOptions<ConsumedOnlyMigrationContext> options
    ) : base(options) { }
}

internal sealed class ConsumedOnlyMigrationOperation : MigrationOperation;

[DbContext(typeof(ConsumedOnlyMigrationContext))]
[Migration(ConsumedOnlyMigrationContract.MigrationId)]
internal sealed class ConsumedOnlyMigration : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.Operations.Add(new ConsumedOnlyMigrationOperation());

    protected override void Down(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.Operations.Add(new ConsumedOnlyMigrationOperation());
}
