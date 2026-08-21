using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.MySql.Examples.MigrationsWorkflow;

/// <summary>
/// Adds the migration-operation handler used by the executable tooling and
/// bundle qualification example.
/// </summary>
internal static class MigrationWorkflowOperationHandlerExtensions
{
    internal const string EvidenceTableName = "MigrationWorkflowHandlerEvidence";
    internal const string ScopeTableName = "MigrationWorkflowHandlerScope";

    /// <summary>
    /// Registers the example handler in EF Core's internal service graph.
    /// </summary>
    /// <param name="optionsBuilder">The context options builder.</param>
    /// <returns>The same builder for fluent configuration.</returns>
    public static DbContextOptionsBuilder<MigrationWorkflowContext> UseMigrationWorkflowOperationHandler(
        this DbContextOptionsBuilder<MigrationWorkflowContext> optionsBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        var extension = optionsBuilder.Options
            .FindExtension<MigrationWorkflowHandlerOptionsExtension>()
            ?? new MigrationWorkflowHandlerOptionsExtension();

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(extension);

        return optionsBuilder;
    }

    /// <summary>
    /// Adds a custom operation that delegates creation of the evidence table
    /// to the provider baseline renderer.
    /// </summary>
    /// <param name="migrationBuilder">The active migration builder.</param>
    public static void CreateMigrationHandlerEvidence(
        this MigrationBuilder migrationBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        var createTable = new CreateTableOperation { Name = EvidenceTableName };
        createTable.Columns.Add(new AddColumnOperation
        {
            Table = EvidenceTableName,
            Name = "Id",
            ClrType = typeof(int),
            ColumnType = "int",
            IsNullable = false,
        });
        createTable.PrimaryKey = new AddPrimaryKeyOperation
        {
            Name = "PK_MigrationWorkflowHandlerEvidence",
            Table = EvidenceTableName,
            Columns = ["Id"],
        };

        migrationBuilder.Operations.Add(
            new MigrationWorkflowOperation(createTable));
    }

    /// <summary>
    /// Adds the corresponding custom operation for removing the evidence
    /// table during rollback.
    /// </summary>
    /// <param name="migrationBuilder">The active migration builder.</param>
    public static void DropMigrationHandlerEvidence(
        this MigrationBuilder migrationBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Operations.Add(
            new MigrationWorkflowOperation(
                new DropTableOperation { Name = EvidenceTableName }));
    }
}

/// <summary>
/// Carries one provider-owned standard operation through the public handler
/// boundary used by the tooling qualification example.
/// </summary>
internal sealed class MigrationWorkflowOperation : MigrationOperation
{
    public MigrationWorkflowOperation(
        MigrationOperation standardOperation
    )
    {
        ArgumentNullException.ThrowIfNull(standardOperation);

        StandardOperation = standardOperation;
    }

    public MigrationOperation StandardOperation { get; }
}

/// <summary>
/// Proves that runtime migration, script generation, and bundles all resolve
/// the same exact custom handler and provider baseline renderer.
/// </summary>
internal sealed class MigrationWorkflowOperationHandler
    : IMySqlMigrationOperationHandler
{
    public string HandlerId =>
        "Doka.EntityFrameworkCore.MySql.Examples.MigrationsWorkflow";

    public Type OperationType => typeof(MigrationWorkflowOperation);

    public MySqlMigrationOperationResult Generate(
        MySqlMigrationOperationContext context
    )
    {
        var operation = (MigrationWorkflowOperation)context.Operation;
        var commands = context
            .RenderStandardOperation(operation.StandardOperation)
            .Select(CreateScopedCommand)
            .ToArray();

        return MySqlMigrationOperationResult.Generated(
            commands,
            "provider_baseline");
    }

    private static MySqlMigrationCommandSpec CreateScopedCommand(
        MySqlMigrationCommandSpec standardCommand
    )
    {
        var body = standardCommand.Fragments
            .Single(fragment => fragment.Kind == MySqlMigrationCommandFragmentKind.Body);
        var providerSetup = standardCommand.Fragments
            .TakeWhile(fragment => fragment.Kind == MySqlMigrationCommandFragmentKind.Setup)
            .Select(fragment => fragment.CommandText.ToString());
        var providerCleanupInAcquisitionOrder = standardCommand.Fragments
            .Reverse()
            .TakeWhile(fragment => fragment.Kind == MySqlMigrationCommandFragmentKind.Cleanup)
            .Select(fragment => fragment.CommandText.ToString());

        // Compose the handler and provider scopes in acquisition order. The
        // public factory reverses cleanup once, so provider cleanup still runs
        // before the handler releases its earlier resources.
        return MySqlMigrationCommandSpec.CreateScoped(
            new[]
            {
                $"CREATE TEMPORARY TABLE `{MigrationWorkflowOperationHandlerExtensions.ScopeTableName}` "
                + "(`Id` int NOT NULL);" + Environment.NewLine,
                $"INSERT INTO `{MigrationWorkflowOperationHandlerExtensions.ScopeTableName}` (`Id`) VALUES (1);"
                + Environment.NewLine,
            }.Concat(providerSetup),
            body.CommandText.ToString(),
            new[]
            {
                $"DROP TEMPORARY TABLE IF EXISTS `{MigrationWorkflowOperationHandlerExtensions.ScopeTableName}`;"
                + Environment.NewLine,
                $"DELETE FROM `{MigrationWorkflowOperationHandlerExtensions.ScopeTableName}`;"
                + Environment.NewLine,
            }.Concat(providerCleanupInAcquisitionOrder),
            standardCommand.TransactionSuppressed);
    }
}

/// <summary>
/// Places the example handler in the options-owned internal service provider,
/// matching the contract required of an external extension package.
/// </summary>
internal sealed class MigrationWorkflowHandlerOptionsExtension
    : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info =>
        _info ??= new ExtensionInfo(this);

    public void ApplyServices(
        IServiceCollection services
    ) => services.TryAddEnumerable(
        ServiceDescriptor.Scoped<
            IMySqlMigrationOperationHandler,
            MigrationWorkflowOperationHandler>());

    public void Validate(
        IDbContextOptions options
    )
    {
        if (options.FindExtension<MySqlOptionsExtension>() is null)
        {
            throw new InvalidOperationException(
                "The migration workflow handler requires the Doka MySQL provider.");
        }
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(
            IDbContextOptionsExtension extension
        ) : base(extension)
        {
        }

        public override bool IsDatabaseProvider => false;

        public override string LogFragment =>
            "migration-workflow-handler ";

        public override int GetServiceProviderHashCode() => 0;

        public override void PopulateDebugInfo(
            IDictionary<string, string> debugInfo
        ) => debugInfo["Doka:MigrationWorkflowHandler"] = "1";

        public override bool ShouldUseSameServiceProvider(
            DbContextOptionsExtensionInfo other
        ) => other is ExtensionInfo;
    }
}
