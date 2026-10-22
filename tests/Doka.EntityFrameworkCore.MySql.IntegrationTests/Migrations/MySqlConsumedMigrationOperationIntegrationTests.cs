using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies that an entirely commandless custom migration still participates
/// in EF Core's provider-owned migration-history lifecycle.
/// </summary>
[IntegrationDatabaseTest]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlConsumedMigrationOperationIntegrationTests
{
    /// <summary>
    /// Verifies my sql84 records consumed only migration history.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_records_consumed_only_migration_history() =>
        AssertConsumedOnlyMigrationHistoryAsync(IntegrationDatabaseTarget.MySql84);

    /// <summary>
    /// Verifies my sql97 records consumed only migration history.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_records_consumed_only_migration_history() =>
        AssertConsumedOnlyMigrationHistoryAsync(IntegrationDatabaseTarget.MySql97);

    /// <summary>
    /// Verifies maria db1011 records consumed only migration history.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_records_consumed_only_migration_history() =>
        AssertConsumedOnlyMigrationHistoryAsync(IntegrationDatabaseTarget.MariaDb1011);

    /// <summary>
    /// Verifies maria db114 records consumed only migration history.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_records_consumed_only_migration_history() =>
        AssertConsumedOnlyMigrationHistoryAsync(IntegrationDatabaseTarget.MariaDb114);

    /// <summary>
    /// Verifies maria db118 records consumed only migration history.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_records_consumed_only_migration_history() =>
        AssertConsumedOnlyMigrationHistoryAsync(IntegrationDatabaseTarget.MariaDb118);

    /// <summary>
    /// Verifies maria db123 records consumed only migration history.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_records_consumed_only_migration_history() =>
        AssertConsumedOnlyMigrationHistoryAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task AssertConsumedOnlyMigrationHistoryAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
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
            var initiallyApplied = await context.Database
                .GetAppliedMigrationsAsync(CancellationToken.None)
                .ConfigureAwait(false);

            var initialInvocationCount = handler.InvocationCount;

            // WHY: Reapplying this migration must observe the history written by the first application.
            // Act
            await migrator
                .MigrateAsync(cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            var firstInvocationCount = handler.InvocationCount;
            var appliedAfterFirstMigration = await context.Database
                .GetAppliedMigrationsAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await migrator
                .MigrateAsync(cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            var appliedAfterRepeatMigration = await context.Database
                .GetAppliedMigrationsAsync(CancellationToken.None)
                .ConfigureAwait(false);

            // Assert
            Assert.Empty(initiallyApplied);
            Assert.Equal(0, initialInvocationCount);
            Assert.Equal(1, firstInvocationCount);
            Assert.Equal([ConsumedOnlyMigrationContract.MigrationId], appliedAfterFirstMigration);
            Assert.Equal(1, handler.InvocationCount);
            Assert.Equal(
                [ConsumedOnlyMigrationContract.MigrationId],
                appliedAfterRepeatMigration);
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
        /// <summary>
        /// Gets the handler id.
        /// </summary>
        public string HandlerId => "tests.consumed_only.runtime";

        /// <summary>
        /// Gets the invocation count.
        /// </summary>
        public int InvocationCount { get; private set; }

        /// <summary>
        /// Verifies typeof.
        /// </summary>
        public Type OperationType => typeof(ConsumedOnlyMigrationOperation);

        /// <summary>
        /// Verifies generate.
        /// </summary>
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

        /// <summary>
        /// Verifies extension info.
        /// </summary>
        public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

        /// <summary>
        /// Verifies apply services.
        /// </summary>
        public void ApplyServices(
            IServiceCollection services
        ) => services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IMySqlMigrationOperationHandler, ConsumedOnlyMigrationOperationHandler>());

        /// <summary>
        /// Verifies validate.
        /// </summary>
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
            /// <summary>
            /// Initializes the ExtensionInfo fixture.
            /// </summary>
            public ExtensionInfo(
                IDbContextOptionsExtension extension
            ) : base(extension) { }

            /// <inheritdoc />
            public override bool IsDatabaseProvider => false;

            /// <inheritdoc />
            public override string LogFragment => "consumed-only-migration-tests ";

            /// <inheritdoc />
            public override int GetServiceProviderHashCode() => 0;

            /// <inheritdoc />
            public override void PopulateDebugInfo(
                IDictionary<string, string> debugInfo
            ) => debugInfo["Doka:ConsumedOnlyMigrationTests"] = "1";

            /// <inheritdoc />
            public override bool ShouldUseSameServiceProvider(
                DbContextOptionsExtensionInfo other
            ) => other is ExtensionInfo;
        }
    }
}

internal static class ConsumedOnlyMigrationContract
{
    /// <summary>
    /// The history table used by this migration fixture.
    /// </summary>
    public const string HistoryTable = "__DokaConsumedOnlyMigrationHistory";

    /// <summary>
    /// The migration id used by this migration fixture.
    /// </summary>
    public const string MigrationId = "20260909000000_ConsumedOnlyMigration";
}

internal sealed class ConsumedOnlyMigrationContext : DbContext
{
    /// <summary>
    /// Initializes the ConsumedOnlyMigrationContext fixture.
    /// </summary>
    public ConsumedOnlyMigrationContext(
        DbContextOptions<ConsumedOnlyMigrationContext> options
    ) : base(options) { }
}

internal sealed class ConsumedOnlyMigrationOperation : MigrationOperation;

[DbContext(typeof(ConsumedOnlyMigrationContext))]
[Migration(ConsumedOnlyMigrationContract.MigrationId)]
internal sealed class ConsumedOnlyMigration : Migration
{
    /// <inheritdoc />
    protected override void Up(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.Operations.Add(new ConsumedOnlyMigrationOperation());

    /// <inheritdoc />
    protected override void Down(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.Operations.Add(new ConsumedOnlyMigrationOperation());
}
