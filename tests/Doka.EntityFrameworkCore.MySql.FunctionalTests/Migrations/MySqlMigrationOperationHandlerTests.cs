namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies migration operation handler contracts.
/// </summary>
public sealed partial class MySqlMigrationOperationHandlerTests
{
    /// <summary>
    /// Verifies custom handler preserves command order boundaries and generation context.
    /// </summary>
    [Theory]
    [MemberData(nameof(GenerationModes))]
    public void Custom_handler_preserves_command_order_boundaries_and_generation_context(
        MigrationsSqlGenerationOptions options
    )
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

        var operation = new FirstCustomOperation("payload-not-telemetry");

        // Act
        var commands = generator.Generate([operation], context.Model, options);

        // Assert
        Assert.Collection(
            commands,
            command =>
            {
                Assert.Equal("SELECT 1;" + Environment.NewLine, command.CommandText);
                Assert.True(command.TransactionSuppressed);
            },
            command =>
            {
                Assert.Equal("SELECT 2;", command.CommandText);
                Assert.False(command.TransactionSuppressed);
            });
        Assert.NotNull(handler.LastContext);
        Assert.Same(operation, handler.LastContext.Operation);
        Assert.Same(context.Model, handler.LastContext.Model);
        Assert.Equal(options, handler.LastContext.Options);
        Assert.Equal(0, handler.LastContext.OperationOrdinal);
        Assert.Equal(new Version(8, 4, 11), handler.LastContext.ServerVersion.Version);
        Assert.Equal(
            MySqlMigrationFeatureSupport.Emulated,
            handler.LastContext.Features.GetSupport(MySqlMigrationFeature.Sequences));
    }

    /// <summary>
    /// Verifies consumed handler emits no command and preserves neighbor boundaries.
    /// </summary>
    [Theory]
    [MemberData(nameof(GenerationModes))]
    public void Consumed_handler_emits_no_command_and_preserves_neighbor_boundaries(
        MigrationsSqlGenerationOptions options
    )
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider(
            [typeof(ConsumedHandler)],
            registerBeforeProvider: true);

        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var handler = context
            .GetService<IEnumerable<IMySqlMigrationOperationHandler>>()
            .OfType<ConsumedHandler>()
            .Single();

        var first = new SqlOperation
        {
            Sql = "SELECT 1;",
            SuppressTransaction = true,
        };

        var second = new SqlOperation { Sql = "SELECT 2;" };

        // Act
        var expected = generator.Generate([first, second], context.Model, options);
        var actual = generator.Generate(
            [first, new ConsumedCustomOperation(), second],
            context.Model,
            options);

        // Assert
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].CommandText, actual[index].CommandText);
            Assert.Equal(expected[index].TransactionSuppressed, actual[index].TransactionSuppressed);
        }

        Assert.NotNull(handler.LastContext);
        Assert.Equal(options, handler.LastContext.Options);
        Assert.Equal(1, handler.LastContext.OperationOrdinal);
    }

    /// <summary>
    /// A consumed operation succeeds without creating a migration command in every generation mode.
    /// </summary>
    [Theory]
    [MemberData(nameof(GenerationModes))]
    public void Consumed_handler_alone_emits_no_command(
        MigrationsSqlGenerationOptions options
    )
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider(
            [typeof(ConsumedHandler)],
            registerBeforeProvider: true);

        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var commands = generator.Generate([new ConsumedCustomOperation()], context.Model, options);

        // Assert
        Assert.Empty(commands);
    }

    /// <summary>
    /// Verifies independent handlers coexist and dispatch by exact runtime type.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Independent_handlers_coexist_and_dispatch_by_exact_runtime_type(
        bool registerBeforeProvider,
        bool reverseHandlerOrder
    )
    {
        // Arrange
        Type[] handlerTypes = reverseHandlerOrder
            ? [typeof(BaselineRenderingHandler), typeof(SecondHandler)]
            : [typeof(SecondHandler), typeof(BaselineRenderingHandler)];

        using var serviceProvider = CreateServiceProvider(handlerTypes, registerBeforeProvider);
        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var handlers = context
            .GetService<IEnumerable<IMySqlMigrationOperationHandler>>()
            .ToArray();

        var first = handlers
            .OfType<BaselineRenderingHandler>()
            .Single();

        var second = handlers
            .OfType<SecondHandler>()
            .Single();

        // Act
        var commands = generator.Generate(
            [
                new FirstCustomOperation("first"),
                new SecondCustomOperation()
            ],
            context.Model);

        // Assert
        Assert.Equal(3, commands.Count);
        Assert.Equal("SELECT 1;" + Environment.NewLine, commands[0].CommandText);
        Assert.Equal("SELECT 2;", commands[1].CommandText);
        Assert.Equal("SELECT 3;", commands[2].CommandText);
        Assert.Equal(0, first.LastContext?.OperationOrdinal);
        Assert.Equal(1, second.LastContext?.OperationOrdinal);
    }

    /// <summary>
    /// Verifies handler ownership does not match derived operation types.
    /// </summary>
    [Fact]
    public void Handler_ownership_does_not_match_derived_operation_types()
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
        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            generator.Generate([new DerivedCustomOperation()], context.Model));

        // Assert
        Assert.Equal(MySqlMigrationHandlerFailureCode.UnknownOperationType, exception.FailureCode);
        Assert.Null(handler.LastContext);
    }

    /// <summary>
    /// Verifies unknown custom operation fails closed.
    /// </summary>
    [Fact]
    public void Unknown_custom_operation_fails_closed()
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider([], registerBeforeProvider: true);
        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            generator.Generate([new FirstCustomOperation("unknown")], context.Model));

        // Assert
        Assert.Equal(MySqlMigrationHandlerFailureCode.UnknownOperationType, exception.FailureCode);
        Assert.Null(exception.HandlerId);
        Assert.Equal(typeof(FirstCustomOperation).FullName, exception.OperationType);
        Assert.Equal(0, exception.OperationOrdinal);
    }

    /// <summary>
    /// Verifies handler exception is wrapped without copying sensitive text.
    /// </summary>
    [Fact]
    public void Handler_exception_is_wrapped_without_copying_sensitive_text()
    {
        // Arrange
        const string sensitiveText = "password=never-log-this";
        const string sensitiveData = "tenant=never-export-this";
        using var serviceProvider = CreateServiceProvider([typeof(ThrowingHandler)], registerBeforeProvider: true);
        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            generator.Generate([new FirstCustomOperation("secret")], context.Model));

        // Assert
        Assert.Equal(MySqlMigrationHandlerFailureCode.HandlerFailed, exception.FailureCode);
        var innerException = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(sensitiveData, innerException.Data["private-context"]);
        Assert.DoesNotContain(sensitiveText, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveData, exception.Message, StringComparison.Ordinal);
    }
}
