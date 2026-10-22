namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationOperationHandlerTests
{
    /// <summary>
    /// Verifies rendering context expires at the handler boundary.
    /// </summary>
    [Fact]
    public void Rendering_context_expires_at_the_handler_boundary()
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider(
            [typeof(BaselineRenderingHandler)],
            registerBeforeProvider: true);

        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var handler = context
            .GetService<IEnumerable<IMySqlMigrationOperationHandler>>()
            .OfType<BaselineRenderingHandler>()
            .Single();

        // Act
        _ = generator.Generate([new FirstCustomOperation("first")], context.Model);

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            handler.LastContext!.RenderStandardOperation(new SqlOperation { Sql = "SELECT 4;" }));

        // Assert
        Assert.Equal(MySqlMigrationHandlerFailureCode.ContextExpired, exception.FailureCode);
    }

    /// <summary>
    /// Verifies rendering context rejects custom operations.
    /// </summary>
    [Fact]
    public void Rendering_context_rejects_custom_operations()
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider(
            [typeof(CustomRenderingHandler)],
            registerBeforeProvider: true);

        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            generator.Generate([new FirstCustomOperation("first")], context.Model));

        // Assert
        Assert.Equal(MySqlMigrationHandlerFailureCode.UnknownOperationType, exception.FailureCode);
    }

    /// <summary>
    /// Verifies baseline renderer exposes provider owned command fragments without parsing.
    /// </summary>
    [Fact]
    public void Baseline_renderer_exposes_provider_owned_command_fragments_without_parsing()
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider(
            [typeof(BaselineRenderingHandler)],
            registerBeforeProvider: true);

        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var handler = context
            .GetService<IEnumerable<IMySqlMigrationOperationHandler>>()
            .OfType<BaselineRenderingHandler>()
            .Single();

        var operation = new AddColumnOperation
        {
            Table = "Entries",
            Name = "Description",
            ClrType = typeof(string),
            ColumnType = "varchar(64)",
            IsNullable = true,
            Comment = "path\\segment; 'quoted' /* data */",
        };

        // Act
        _ = generator.Generate(
            [new FirstCustomOperation("structured-baseline", operation)],
            context.Model);

        // Assert
        var command = Assert.Single(handler.LastBaseline!);

        Assert.Collection(
            command.Fragments,
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Setup, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Setup, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Body, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Cleanup, fragment.Kind));
        Assert.Equal(
            command.CommandText,
            string.Concat(command.Fragments.Select(static fragment => fragment.CommandText.ToString())));
        Assert.Contains(
            "ALTER TABLE `Entries` ADD `Description` varchar(64) NULL COMMENT",
            command.Fragments[2].CommandText.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies ordinary baseline command contains one body fragment.
    /// </summary>
    [Fact]
    public void Ordinary_baseline_command_contains_one_body_fragment()
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider(
            [typeof(BaselineRenderingHandler)],
            registerBeforeProvider: true);

        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var handler = context
            .GetService<IEnumerable<IMySqlMigrationOperationHandler>>()
            .OfType<BaselineRenderingHandler>()
            .Single();

        // Act
        _ = generator.Generate(
            [new FirstCustomOperation("ordinary", new SqlOperation { Sql = "SELECT 4;" })],
            context.Model);

        // Assert
        var command = Assert.Single(handler.LastBaseline!);
        var fragment = Assert.Single(command.Fragments);

        Assert.Equal(MySqlMigrationCommandFragmentKind.Body, fragment.Kind);
        Assert.Equal(command.CommandText, fragment.CommandText.ToString());
    }

    /// <summary>
    /// Verifies handler scoped command materializes provider owned executor.
    /// </summary>
    [Fact]
    public void Handler_scoped_command_materializes_provider_owned_executor()
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider([typeof(ScopedCommandHandler)], registerBeforeProvider: true);

        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var commands = generator.Generate([new FirstCustomOperation("scoped")], context.Model);

        // Assert
        var command = Assert.Single(commands);
        var scopedCommand = Assert.IsType<MySqlScopedMigrationCommand>(command);

        Assert.True(command.TransactionSuppressed);
        Assert.Equal(MySqlMigrationCommandScopeKind.Handler, scopedCommand.Layout.ScopeKind);
        Assert.Equal(2, scopedCommand.Layout.SetupCommandTexts.Count);
        Assert.Equal(2, scopedCommand.Layout.CleanupCommandTexts.Count);
        Assert.Equal("SELECT @first + @second;\n", scopedCommand.Layout.BodyCommandText);
    }

    /// <summary>
    /// Verifies every comment wrapper path exposes the same structured scope.
    /// </summary>
    [Theory]
    [InlineData("create")]
    [InlineData("add")]
    [InlineData("alter-column")]
    [InlineData("alter-table")]
    [InlineData("rename-fallback")]
    public void Every_comment_wrapper_path_exposes_the_same_structured_scope(
        string path
    )
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider(
            [typeof(BaselineRenderingHandler)],
            registerBeforeProvider: true);

        var serverVersion = path == "rename-fallback"
            ? MySqlServerVersion.MariaDb(
                new Version(10, 5, 0),
                MySqlServerVersionCompatibilityMode.AllowUnsupported)
            : MySqlServerVersion.MySql(new Version(8, 4, 11));

        using var context = CreateContext(serviceProvider, serverVersion);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var handler = context
            .GetService<IEnumerable<IMySqlMigrationOperationHandler>>()
            .OfType<BaselineRenderingHandler>()
            .Single();

        var operation = CreateCommentWrapperOperation(path);
        var model = context.GetService<IDesignTimeModel>().Model;

        // Act
        _ = generator.Generate(
            [new FirstCustomOperation("comment-wrapper", operation)],
            model);

        // Assert
        var command = Assert.Single(handler.LastBaseline!);

        Assert.Collection(
            command.Fragments,
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Setup, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Setup, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Body, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Cleanup, fragment.Kind));
    }
}
