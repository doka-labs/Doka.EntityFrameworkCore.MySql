namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationOperationHandlerTests
{
    /// <summary>
    /// Verifies null handler result fails before any command is returned.
    /// </summary>
    [Fact]
    public void Null_handler_result_fails_before_any_command_is_returned()
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider([typeof(NullResultHandler)], registerBeforeProvider: true);
        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            generator.Generate([new FirstCustomOperation("invalid")], context.Model));

        // Assert
        Assert.Equal(MySqlMigrationHandlerFailureCode.InvalidHandlerResult, exception.FailureCode);
    }

    /// <summary>
    /// Verifies invalid staged command never mutates the outer builder.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Invalid_staged_command_never_mutates_the_outer_builder(
        int invalidCommandIndex
    )
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider(
            [typeof(InvalidCommandResultHandler)],
            registerBeforeProvider: true);

        using var context = CreateContext(serviceProvider);

        var generator = (MySqlMigrationsSqlGenerator)context.GetService<IMigrationsSqlGenerator>();
        var builder = new MigrationCommandListBuilder(context.GetService<MigrationsSqlGeneratorDependencies>());
        var generate = typeof(MySqlMigrationsSqlGenerator).GetMethod(
            "Generate",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(MigrationOperation),
                typeof(IModel),
                typeof(MigrationCommandListBuilder)
            ],
            modifiers: null);

        // Act
        var invocation = Assert.Throws<TargetInvocationException>(() => generate!.Invoke(
            generator,
            [
                new InvalidResultOperation(invalidCommandIndex),
                context.Model,
                builder,
            ]));

        // Assert
        var exception = Assert.IsType<MySqlMigrationOperationHandlerException>(invocation.InnerException);

        Assert.Equal(MySqlMigrationHandlerFailureCode.InvalidHandlerResult, exception.FailureCode);
        Assert.Empty(builder.GetCommandList());
    }

    /// <summary>
    /// Verifies stateful handler result is snapshotted once before validation.
    /// </summary>
    [Fact]
    public void Stateful_handler_result_is_snapshotted_once_before_validation()
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider(
            [typeof(StatefulResultHandler)],
            registerBeforeProvider: true);

        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var handler = context
            .GetService<IEnumerable<IMySqlMigrationOperationHandler>>()
            .OfType<StatefulResultHandler>()
            .Single();

        // Act
        var commands = generator.Generate([new StatefulResultOperation()], context.Model);

        // Assert
        Assert.Collection(
            commands,
            command => Assert.Equal("SELECT 1;", command.CommandText),
            command => Assert.Equal("SELECT 2;", command.CommandText));
        Assert.Equal(1, handler.Commands?.EnumerationCount);
    }

    /// <summary>
    /// Verifies inconsistent result state fails before any command is returned.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Inconsistent_result_state_fails_before_any_command_is_returned(
        int shapeValue
    )
    {
        // Arrange
        var shape = (MalformedResultShape)shapeValue;
        using var serviceProvider = CreateServiceProvider(
            [typeof(MalformedResultHandler)],
            registerBeforeProvider: true);

        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            generator.Generate([new MalformedResultOperation(shape)], context.Model));

        // Assert
        Assert.Equal(MySqlMigrationHandlerFailureCode.InvalidHandlerResult, exception.FailureCode);
    }
}
