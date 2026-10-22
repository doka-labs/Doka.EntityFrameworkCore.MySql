namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationOperationHandlerTests
{
    /// <summary>
    /// Verifies plugin options extension registers handler before or after use MySQL.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Plugin_options_extension_registers_handler_before_or_after_use_mysql(
        bool registerBeforeUseMySql
    )
    {
        // Arrange
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<HandlerContext>();

        if (registerBeforeUseMySql)
        {
            AddPlugin<SecondHandler>(builder);
        }

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 11)));

        if (!registerBeforeUseMySql)
        {
            AddPlugin<SecondHandler>(builder);
        }

        using var context = new HandlerContext(builder.Options);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var commands = generator.Generate([new SecondCustomOperation()], context.Model);

        // Assert
        var command = Assert.Single(commands);
        Assert.Equal("SELECT 3;", command.CommandText);
    }

    /// <summary>
    /// Verifies independent plugin options extensions compose in the internal service graph.
    /// </summary>
    [Fact]
    public void Independent_plugin_options_extensions_compose_in_the_internal_service_graph()
    {
        // Arrange
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<HandlerContext>();
        AddPlugin<BaselineRenderingHandler>(builder);
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 11)));
        AddPlugin<SecondHandler>(builder);

        using var context = new HandlerContext(builder.Options);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var commands = generator.Generate(
            [
                new FirstCustomOperation("first"),
                new SecondCustomOperation(),
            ],
            context.Model);

        // Assert
        Assert.Equal(3, commands.Count);
    }

    /// <summary>
    /// Verifies conflicting plugin options extensions fail service graph construction.
    /// </summary>
    [Theory]
    [InlineData(typeof(DuplicateIdHandler), MySqlMigrationHandlerFailureCode.DuplicateHandlerId)]
    [InlineData(typeof(DuplicateOperationHandler), MySqlMigrationHandlerFailureCode.DuplicateOperationOwnership)]
    public void Conflicting_plugin_options_extensions_fail_service_graph_construction(
        Type conflictingHandlerType,
        MySqlMigrationHandlerFailureCode expectedFailureCode
    )
    {
        // Arrange
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<HandlerContext>();
        AddPlugin<BaselineRenderingHandler>(builder);
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 11)));
        if (conflictingHandlerType == typeof(DuplicateIdHandler))
        {
            AddPlugin<DuplicateIdHandler>(builder);
        }
        else
        {
            Assert.Equal(typeof(DuplicateOperationHandler), conflictingHandlerType);
            AddPlugin<DuplicateOperationHandler>(builder);
        }

        using var context = new HandlerContext(builder.Options);

        // Act
        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(context.GetService<IMigrationsSqlGenerator>);

        // Assert
        Assert.Equal(expectedFailureCode, exception.FailureCode);
    }
}
