namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the spatial contracts that differ by driver shape or engine version
/// against live supported servers.
/// </summary>
[IntegrationDatabaseTest]
public sealed partial class MySqlNetTopologySuiteContractIntegrationTests
    : IClassFixture<MySqlNetTopologySuiteContractIntegrationTests.SpatialFixture>
{
    private const string MaterializationTable = "DokaSpatialMaterialization";
    private const string MaterializationChildTable = "DokaSpatialMaterializationChild";
    private const string CrossesTable = "DokaSpatialCrosses";
    private const string SridTable = "DokaSpatialSrid";

    private readonly SpatialFixture _fixture;
    private static readonly IntegrationDatabaseTarget[] s_materializationTargets =
        [IntegrationDatabaseTarget.MySql84, IntegrationDatabaseTarget.MariaDb118];

    private static readonly IntegrationDatabaseTarget[] s_mariaDbTargets =
    [
        IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb114,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123,
    ];

    private static readonly IntegrationDatabaseTarget[] s_functionTargets =
        [IntegrationDatabaseTarget.MySql84, IntegrationDatabaseTarget.MySql97, IntegrationDatabaseTarget.MariaDb123];

    private static readonly string[] s_materializationShapes = ["Untracked", "Scalar", "Include", "SplitInclude"];
    private static readonly string[] s_geometryFunctions = ["Buffer", "Combine", "Union", "Envelope"];

    /// <summary>Uses lazily seeded, class-owned spatial datasets.</summary>
    public MySqlNetTopologySuiteContractIntegrationTests(
        SpatialFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>Identifies MariaDB versions that enforce SRID through a check constraint.</summary>
    public static IEnumerable<object[]> MariaDbCases =>
        s_mariaDbTargets.Where(IntegrationTestEnvironment.IsTargetSelected)
            .Select(target => new object[] { target.ToString() });

    /// <summary>Identifies engines that exercise every geometry reader shape.</summary>
    public static IEnumerable<object[]> MaterializationTargetCases =>
        s_materializationTargets.Where(IntegrationTestEnvironment.IsTargetSelected)
            .Select(target => new object[] { target.ToString() });

    /// <summary>Identifies the independent untracked/projection/include reader paths.</summary>
    public static IEnumerable<object[]> MaterializationCases =>
        from target in s_materializationTargets.Where(IntegrationTestEnvironment.IsTargetSelected)
        from shape in s_materializationShapes
        select new object[] { target.ToString(), shape };

    /// <summary>Identifies engines that expose the tested scalar and aggregate functions.</summary>
    public static IEnumerable<object[]> FunctionTargetCases =>
        s_functionTargets.Where(IntegrationTestEnvironment.IsTargetSelected)
            .Select(target => new object[] { target.ToString() });

    /// <summary>Identifies each independently executable geometry-returning function.</summary>
    public static IEnumerable<object[]> GeometryFunctionCases =>
        from target in s_functionTargets.Where(IntegrationTestEnvironment.IsTargetSelected)
        from function in s_geometryFunctions
        select new object[] { target.ToString(), function };

    /// <summary>Retains migration, enforcement, catalog recovery, and scaffolding as one SRID lifecycle.</summary>
    [RequiresDatabaseTargetsTheory(
        IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb114,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123)]
    [MemberData(nameof(MariaDbCases))]
    public async Task MariaDb_srid_check_migrates_enforces_and_scaffolds(
        string target
    )
    {
        // Arrange
        var engine = Enum.Parse<IntegrationDatabaseTarget>(target);
        var connectionString = GetConnectionString(engine, "Srid");
        await DropContractTablesAsync(connectionString).ConfigureAwait(false);
        await using var source = new EmptySpatialContractContext(
            CreateOptions<EmptySpatialContractContext>(connectionString, engine));
        await using var context = new SpatialSridContractContext(
            CreateOptions<SpatialSridContractContext>(connectionString, engine));
        await using var provider = ScaffoldingTestServices.CreateDesignTimeServiceProvider(
            includeSpatialServices: true);

        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var databaseOptions = new DatabaseModelFactoryOptions([SridTable], Array.Empty<string>());

        try
        {
            // WHY: Each observation depends on the same schema transition:
            // generated DDL must be enforced and then recovered as SRID metadata.
            // Act
            var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(
                source.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
                context.GetService<IDesignTimeModel>().Model.GetRelationalModel());
            var commands = context.GetService<IMigrationsSqlGenerator>().Generate(operations, context.Model);
            var sql = string.Join(Environment.NewLine, commands.Select(command => command.CommandText));

            foreach (var command in commands)
            {
                await context.Database.ExecuteSqlRawAsync(command.CommandText).ConfigureAwait(false);
            }

            var showCreate = await ReadShowCreateTableAsync(connectionString, SridTable).ConfigureAwait(false);
            var failure = await Record.ExceptionAsync(() => context.Database.ExecuteSqlRawAsync(
                $"INSERT INTO `{SridTable}` (`Id`, `Location`) VALUES (1, ST_GeomFromText('POINT (1 2)', 0));"));
            var databaseModel = services.GetRequiredService<IDatabaseModelFactory>()
                .Create(connectionString, databaseOptions);
            var scaffolded = services.GetRequiredService<IReverseEngineerScaffolder>().ScaffoldModel(
                connectionString,
                databaseOptions,
                new ModelReverseEngineerOptions(),
                ScaffoldingTestServices.CreateCodeGenerationOptions(connectionString));

            // Assert
            Assert.Contains("CHECK (ST_SRID(`Location`) = 4326)", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("point SRID 4326", sql, StringComparison.OrdinalIgnoreCase);
            Assert.True(showCreate.HasRow);
            var showCreateSql = showCreate.Sql;

            Assert.NotNull(showCreateSql);
            Assert.Contains("CHECK (srid(`Location`) = 4326)", showCreateSql, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(4025, Assert.IsType<MySqlException>(failure).Number);
            var table = Assert.Single(databaseModel.Tables);
            var location = Assert.Single(table.Columns, column => column.Name == "Location");

            Assert.Equal(4326, location.FindAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId)?.Value);
            Assert.Null(table.FindAnnotation(MySqlAnnotationNames.ScaffoldingCheckConstraints));
            Assert.Contains(".HasSrid(4326)", scaffolded.ContextFile.Code, StringComparison.Ordinal);
            Assert.DoesNotContain("HasCheckConstraint", scaffolded.ContextFile.Code, StringComparison.Ordinal);
        }
        finally
        {
            await DropContractTablesAsync(connectionString).ConfigureAwait(false);
        }
    }
}
