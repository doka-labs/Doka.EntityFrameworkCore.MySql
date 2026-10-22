namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    /// <summary>
    /// An existing table gains its application-time contract only after the
    /// period columns and before its replacement primary key.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_orders_existing_application_time_activation()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        using var sourceContext = new OrdinaryApplicationTimeMigrationDslContext(
            CreateOptions<OrdinaryApplicationTimeMigrationDslContext>(serverVersion));

        using var targetContext = new ApplicationTimeMigrationDslContext(
            CreateOptions<ApplicationTimeMigrationDslContext>(serverVersion));

        // Act
        var operations = GetDifferences(sourceContext, targetContext);

        // Assert
        var transition = Assert.Single(operations.OfType<AlterTableOperation>());
        var dropPrimaryKey = Assert.Single(operations.OfType<DropPrimaryKeyOperation>());
        var addPrimaryKey = Assert.Single(operations.OfType<AddPrimaryKeyOperation>());
        var periodColumns = operations
            .OfType<AddColumnOperation>()
            .Where(column => column.Name is "ValidFrom" or "ValidTo")
            .ToArray();

        Assert.Empty(operations.OfType<CreateTableOperation>());
        Assert.True(transition.FindAnnotation(MySqlAnnotationNames.IsApplicationTime)?.Value as bool?);
        Assert.True(addPrimaryKey.FindAnnotation(MySqlAnnotationNames.ApplicationTimeWithoutOverlaps)?.Value as bool?);
        Assert.True(operations.IndexOf(dropPrimaryKey) < operations.IndexOf(transition));
        Assert.True(operations.IndexOf(transition) < operations.IndexOf(addPrimaryKey));
        Assert.Equal(2, periodColumns.Length);
        Assert.Contains(periodColumns, column => column.Name == "ValidFrom");
        Assert.Contains(periodColumns, column => column.Name == "ValidTo");
        Assert.All(periodColumns, column => Assert.True(operations.IndexOf(column) < operations.IndexOf(transition)));
    }

    /// <summary>
    /// An existing table drops the application-time contract before changing
    /// its period columns and primary key.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_orders_existing_application_time_deactivation()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        using var sourceContext = new ApplicationTimeMigrationDslContext(
            CreateOptions<ApplicationTimeMigrationDslContext>(serverVersion));

        using var targetContext = new OrdinaryApplicationTimeMigrationDslContext(
            CreateOptions<OrdinaryApplicationTimeMigrationDslContext>(serverVersion));

        // Act
        var operations = GetDifferences(sourceContext, targetContext);

        // Assert
        var transition = Assert.Single(operations.OfType<AlterTableOperation>());
        var dropPrimaryKey = Assert.Single(operations.OfType<DropPrimaryKeyOperation>());
        var addPrimaryKey = Assert.Single(operations.OfType<AddPrimaryKeyOperation>());
        var periodColumns = operations
            .OfType<DropColumnOperation>()
            .Where(column => column.Name is "ValidFrom" or "ValidTo")
            .ToArray();

        Assert.Empty(operations.OfType<CreateTableOperation>());
        Assert.True(transition.FindAnnotation(MySqlAnnotationNames.ApplicationTimeSourceIsApplicationTime)?.Value as bool?);
        Assert.True(dropPrimaryKey.FindAnnotation(MySqlAnnotationNames.ApplicationTimeSourceWithoutOverlaps)?.Value as bool?);
        Assert.True(operations.IndexOf(dropPrimaryKey) < operations.IndexOf(transition));
        Assert.True(operations.IndexOf(transition) < operations.IndexOf(addPrimaryKey));
        Assert.Equal(2, periodColumns.Length);
        Assert.Contains(periodColumns, column => column.Name == "ValidFrom");
        Assert.Contains(periodColumns, column => column.Name == "ValidTo");
        Assert.All(periodColumns, column => Assert.True(operations.IndexOf(transition) < operations.IndexOf(column)));
    }

    /// <summary>
    /// An unchanged application-time contract does not synthesize a primary-key transition.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_keeps_existing_application_time_contract_unchanged()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        using var sourceContext = new ApplicationTimeMigrationDslContext(
            CreateOptions<ApplicationTimeMigrationDslContext>(serverVersion));

        using var targetContext = new ApplicationTimeMigrationDslContext(
            CreateOptions<ApplicationTimeMigrationDslContext>(serverVersion));

        // Act
        var operations = GetDifferences(sourceContext, targetContext);

        // Assert
        Assert.Empty(operations);
    }
}
