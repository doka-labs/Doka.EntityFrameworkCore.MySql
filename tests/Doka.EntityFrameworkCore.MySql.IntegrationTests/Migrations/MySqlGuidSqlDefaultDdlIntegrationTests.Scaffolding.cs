namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGuidSqlDefaultDdlIntegrationTests
{
    private static readonly bool[] s_scaffoldTextGuidPolicies = [false, true];

    /// <summary>
    /// Identifies textual and native Guid defaults with both textual reverse-engineering policies.
    /// </summary>
    public static IEnumerable<object[]> ScaffoldingCases =>
        from target in SelectedTargets()
        from format in FormatsFor(target).Where(format => format != MySqlGuidFormat.Binary16)
        from scaffoldTextGuids in s_scaffoldTextGuidPolicies
        select new object[] { target.ToString(), format, scaffoldTextGuids };

    /// <summary>
    /// Live defaults, CLR metadata, and generated C# survive compilation and executable schema reconstruction.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(ScaffoldingCases))]
    public async Task Scaffolded_guid_sql_default_compiles_and_replays_with_expected_clr_type(
        string target,
        MySqlGuidFormat format,
        bool scaffoldTextGuids
    )
    {
        // Arrange
        var databaseTarget = Enum.Parse<IntegrationDatabaseTarget>(target);
        var tableName = CreateTableName();
        var connectionString = GetConnectionString(databaseTarget);
        await using var connection = CreateConnection(databaseTarget);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var sourceContext = CreateContext(connection, databaseTarget, format);
        await using var serviceProvider = ScaffoldingTestServices.CreateDesignTimeServiceProvider(
            configure: scaffoldTextGuids ? options => options.ScaffoldTextGuidsAsGuids() : null);
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;
        var databaseOptions = new DatabaseModelFactoryOptions([tableName], Array.Empty<string>());

        try
        {
            _ = await ExecuteOperationsAsync(
                    sourceContext,
                    connection,
                    CreateGuidDefaultTable(tableName, format, "UUID()").Operations)
                .ConfigureAwait(false);

            // Act
            var databaseModel = services.GetRequiredService<IDatabaseModelFactory>()
                .Create(connectionString, databaseOptions);
            var scaffoldedModel = services.GetRequiredService<IReverseEngineerScaffolder>()
                .ScaffoldModel(
                    connectionString,
                    databaseOptions,
                    new ModelReverseEngineerOptions(),
                    ScaffoldingTestServices.CreateCodeGenerationOptions(
                        connectionString,
                        contextName: "GuidSqlDefaultScaffoldContext",
                        suppressOnConfiguring: true));
            var compiledAssembly = CompileScaffoldedModel(scaffoldedModel);
            await using var generatedContext = CreateCompiledContext(
                compiledAssembly,
                connection,
                databaseTarget);
            var generatedModel = generatedContext.GetService<IDesignTimeModel>().Model;
            var idProperty = generatedModel.GetEntityTypes().Single().FindProperty("Id")
                ?? throw new InvalidOperationException("The compiled scaffolded model lost its ID property.");
            var replaySql = generatedContext.Database.GenerateCreateScript();

            await DropTableAsync(connection, tableName).ConfigureAwait(false);
            await ExecuteRawAsync(connection, replaySql).ConfigureAwait(false);
            await ExecuteRawAsync(connection, $"INSERT INTO `{tableName}` (`Value`) VALUES (1), (2);")
                .ConfigureAwait(false);
            var rows = await ReadRowsAsync(connection, tableName, format).ConfigureAwait(false);
            var replayedDatabaseModel = services.GetRequiredService<IDatabaseModelFactory>()
                .Create(connectionString, databaseOptions);

            // Assert
            var originalColumn = databaseModel.Tables.Single().Columns.Single(column => column.Name == "Id");
            var replayedColumn = replayedDatabaseModel.Tables.Single().Columns.Single(column => column.Name == "Id");
            var expectedClrType = format == MySqlGuidFormat.Uuid || scaffoldTextGuids ? typeof(Guid) : typeof(string);

            Assert.Equal(StoreTypeFor(format), originalColumn.StoreType, ignoreCase: true);
            Assert.Contains("uuid()", originalColumn.DefaultValueSql, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(originalColumn.DefaultValueSql, idProperty.GetDefaultValueSql());
            Assert.Equal(originalColumn.DefaultValueSql, replayedColumn.DefaultValueSql);
            Assert.Equal(originalColumn.StoreType, replayedColumn.StoreType, ignoreCase: true);
            Assert.Equal(expectedClrType, idProperty.ClrType);
            Assert.Equal(ValueGenerated.OnAdd, idProperty.ValueGenerated);
            Assert.Contains("HasDefaultValueSql(", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
            Assert.DoesNotContain("OnConfiguring", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
            Assert.False(
                scaffoldedModel.ContextFile.Code.Contains(connectionString, StringComparison.Ordinal),
                "Generated source must not contain the configured connection details.");
            AssertGeneratedRows(rows, format, [1, 2]);
        }
        finally
        {
            await DropTableAsync(connection, tableName).ConfigureAwait(false);
        }
    }
}
