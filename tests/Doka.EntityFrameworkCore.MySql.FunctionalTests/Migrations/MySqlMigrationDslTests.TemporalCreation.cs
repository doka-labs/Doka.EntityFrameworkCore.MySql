namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    /// <summary>
    /// Verifies that the relational model preserves the complete temporal table
    /// contract when EF Core materializes migration operations.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_carries_temporal_annotations_into_create_operations()
    {
        // Arrange
        using var sourceContext = new EmptyMigrationDslContext(CreateOptions<EmptyMigrationDslContext>());
        using var targetContext = new TemporalMigrationDslContext(CreateOptions<TemporalMigrationDslContext>());

        // Act
        var operations = GetDifferences(sourceContext, targetContext);

        // Assert
        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());
        var periodStart = Assert.Single(
            createTable.Columns,
            column => column.Name == "ValidFrom");

        var periodEnd = Assert.Single(
            createTable.Columns,
            column => column.Name == "ValidTo");

        Assert.True(createTable.FindAnnotation(MySqlAnnotationNames.IsTemporal)?.Value as bool?);
        Assert.Equal(
            "MigrationDslHistory",
            createTable.FindAnnotation(MySqlAnnotationNames.TemporalHistoryTable)?.Value);
        Assert.Equal(
            "ValidFrom",
            createTable.FindAnnotation(MySqlAnnotationNames.TemporalPeriodStartColumn)?.Value);
        Assert.Equal(
            "ValidTo",
            createTable.FindAnnotation(MySqlAnnotationNames.TemporalPeriodEndColumn)?.Value);
        Assert.True(periodStart.FindAnnotation(MySqlAnnotationNames.TemporalPeriodStartColumn)?.Value as bool?);
        Assert.True(periodEnd.FindAnnotation(MySqlAnnotationNames.TemporalPeriodEndColumn)?.Value as bool?);
    }

    /// <summary>
    /// Verifies that MariaDB receives its native system-versioned table contract.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_uses_native_system_versioning_on_mariadb()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        using var sourceContext = new EmptyMigrationDslContext(
            CreateOptions<EmptyMigrationDslContext>(serverVersion));

        using var targetContext = new TemporalMigrationDslContext(
            CreateOptions<TemporalMigrationDslContext>(serverVersion));

        // Act
        var sql = GenerateMigrationSql(sourceContext, targetContext);

        // Assert
        Assert.Contains(
            "`ValidFrom` timestamp(6) GENERATED ALWAYS AS ROW START",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "`ValidTo` timestamp(6) GENERATED ALWAYS AS ROW END",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "PERIOD FOR SYSTEM_TIME (`ValidFrom`, `ValidTo`)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("WITH SYSTEM VERSIONING", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TRIGGER", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TABLE `MigrationDslHistory`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a typed application-time model becomes MariaDB period DDL
    /// and binds <c>WITHOUT OVERLAPS</c> to the configured period.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_creates_application_time_period_on_mariadb()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        using var sourceContext = new EmptyMigrationDslContext(
            CreateOptions<EmptyMigrationDslContext>(serverVersion));

        using var targetContext = new ApplicationTimeMigrationDslContext(
            CreateOptions<ApplicationTimeMigrationDslContext>(serverVersion));

        // Act
        var sql = GenerateMigrationSql(sourceContext, targetContext);

        // Assert
        Assert.Contains(
            "PERIOD FOR `BusinessValidity` (`ValidFrom`, `ValidTo`)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "PRIMARY KEY (`Id`, `BusinessValidity` WITHOUT OVERLAPS)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            ",\n    CONSTRAINT `PK_MigrationDsl` PRIMARY KEY",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("WITH SYSTEM VERSIONING", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that bitemporal configuration composes both independent engine
    /// contracts rather than replacing one temporal dimension with the other.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_creates_bitemporal_table_on_mariadb()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        using var sourceContext = new EmptyMigrationDslContext(
            CreateOptions<EmptyMigrationDslContext>(serverVersion));

        using var targetContext = new BitemporalMigrationDslContext(
            CreateOptions<BitemporalMigrationDslContext>(serverVersion));

        // Act
        var sql = GenerateMigrationSql(sourceContext, targetContext);

        // Assert
        Assert.Contains("PERIOD FOR SYSTEM_TIME (`SystemValidFrom`, `SystemValidTo`)", sql, StringComparison.Ordinal);
        Assert.Contains(
            "PERIOD FOR `BusinessValidity` (`BusinessValidFrom`, `BusinessValidTo`)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("WITH SYSTEM VERSIONING", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that MySQL receives the complete transactional temporal emulation.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_uses_history_table_and_triggers_on_mysql()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new EmptyMigrationDslContext(
            CreateOptions<EmptyMigrationDslContext>(serverVersion));

        using var targetContext = new TemporalMigrationDslContext(
            CreateOptions<TemporalMigrationDslContext>(serverVersion));

        // Act
        var sql = GenerateMigrationSql(sourceContext, targetContext);

        // Assert
        Assert.Contains("CREATE TABLE `MigrationDslHistory`", sql, StringComparison.Ordinal);
        Assert.Contains("BEFORE INSERT ON `MigrationDsl`", sql, StringComparison.Ordinal);
        Assert.Contains("BEFORE UPDATE ON `MigrationDsl`", sql, StringComparison.Ordinal);
        Assert.Contains("BEFORE DELETE ON `MigrationDsl`", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `MigrationDslHistory`", sql, StringComparison.Ordinal);
        Assert.Contains("UTC_TIMESTAMP(6)", sql, StringComparison.Ordinal);
        Assert.Contains("doka-temporal-v1:", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WITH SYSTEM VERSIONING", sql, StringComparison.Ordinal);
    }
}
