using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Design;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    /// <summary>
    /// Builds provider migration operations from the design-time relational models.
    /// </summary>
    private static List<MigrationOperation> GetDifferences(
        DbContext source,
        DbContext target
    ) => target
        .GetService<IMigrationsModelDiffer>()
        .GetDifferences(
            source.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            target.GetService<IDesignTimeModel>().Model.GetRelationalModel())
        .ToList();

    /// <summary>
    /// Builds provider migration operations from the design-time relational models.
    /// </summary>
    private static List<MigrationOperation> GetDifferences(
        DbContext target
    ) => target
        .GetService<IMigrationsModelDiffer>()
        .GetDifferences(
            null,
            target.GetService<IDesignTimeModel>().Model.GetRelationalModel())
        .ToList();

    /// <summary>
    /// Executes model differentiation and SQL generation as one migration pipeline.
    /// </summary>
    private static string GenerateMigrationSql(
        DbContext source,
        DbContext target
    )
    {
        var operations = GetDifferences(source, target);

        return GenerateMigrationSql(target, operations);
    }

    /// <summary>
    /// Executes model differentiation and SQL generation as one migration pipeline.
    /// </summary>
    private static string GenerateMigrationSql(
        DbContext target,
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var commands = target
            .GetService<IMigrationsSqlGenerator>()
            .Generate(operations, target.Model);

        return string.Join(
            Environment.NewLine,
            commands.Select(command => command.CommandText));
    }

    /// <summary>
    /// Generates executable C# migration source for the supplied operations.
    /// </summary>
    private static string GenerateMigrationCode(
        IReadOnlyList<MigrationOperation> upOperations,
        IReadOnlyList<MigrationOperation> downOperations
    )
    {
        using var serviceProvider = CreateDesignTimeServiceProvider();
        var generator = serviceProvider
            .GetRequiredService<IMigrationsCodeGeneratorSelector>()
            .Select("C#");

        return generator.GenerateMigration(
            "Doka.GeneratedDatabaseOptionMigrations",
            "DatabaseOptionsMigration",
            upOperations,
            downOperations);
    }

    private static void AssertDatabaseOptions(
        AlterDatabaseOperation operation,
        string? targetCharSet,
        string? targetCollation,
        string? sourceCharSet,
        string? sourceCollation
    )
    {
        Assert.Equal(targetCollation, operation.Collation);
        Assert.Equal(sourceCollation, operation.OldDatabase.Collation);
        Assert.Equal(
            targetCharSet,
            operation.FindAnnotation(MySqlAnnotationNames.CharSet)?.Value);
        Assert.Equal(
            sourceCharSet,
            operation.OldDatabase.FindAnnotation(MySqlAnnotationNames.CharSet)?.Value);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext
    => CreateOptions<TContext>(MySqlServerVersion.MySql(new Version(8, 4, 0)));

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlServerVersion serverVersion
    )
        where TContext : DbContext
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>();

        builder.UseMySql(
            "Server=localhost;Database=phase2;User ID=root;Password=password;",
            serverVersion,
            providerOptions => providerOptions.UseNetTopologySuite());

        return builder.Options;
    }

    /// <summary>
    /// Creates the real provider design-time service graph used by generated migration source.
    /// </summary>
    private static ServiceProvider CreateDesignTimeServiceProvider()
    {
        var services = new ServiceCollection();
#pragma warning disable EF1001
        var reporter = new OperationReporter(new OperationReportHandler(_ => { }, _ => { }, _ => { }, _ => { }));
#pragma warning restore EF1001

        services.AddEntityFrameworkDesignTimeServices(reporter, () => new ServiceCollection().BuildServiceProvider());
        services.AddEntityFrameworkDokaMySqlDesignTime();

        return services.BuildServiceProvider(validateScopes: true);
    }
}
