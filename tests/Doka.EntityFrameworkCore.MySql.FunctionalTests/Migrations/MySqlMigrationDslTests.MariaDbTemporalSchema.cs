namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    /// <summary>
    /// Verifies that native MariaDB temporal history is never made inaccurate
    /// through the engine's permissive system-versioning alteration mode.
    /// </summary>
    [Theory]
    [InlineData(NativeTemporalSchemaChange.AddColumn)]
    [InlineData(NativeTemporalSchemaChange.RenameColumn)]
    [InlineData(NativeTemporalSchemaChange.AlterColumn)]
    [InlineData(NativeTemporalSchemaChange.DropColumn)]
    [InlineData(NativeTemporalSchemaChange.RenameTable)]
    public void Migrations_sql_generator_rejects_unsafe_native_temporal_schema_changes_on_mariadb(
        NativeTemporalSchemaChange schemaChange
    )
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        var contexts = CreateNativeTemporalSchemaChangeContexts(schemaChange, serverVersion);

        using var sourceContext = contexts.Source;
        using var targetContext = contexts.Target;

        // Act
        var exception = Assert.Throws<InvalidOperationException>(
            () => GenerateMigrationSql(sourceContext, targetContext));

        // Assert
        Assert.Contains("native MariaDB temporal table", exception.Message, StringComparison.Ordinal);
        Assert.Contains("history", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that MariaDB temporal activation creates the native period
    /// before enabling system versioning without MySQL emulation artifacts.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_enables_native_temporal_contract_on_mariadb()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        using var nonTemporalContext = new NonTemporalSchemaContext(
            CreateOptions<NonTemporalSchemaContext>(serverVersion));

        using var temporalContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));

        // Act
        var enableOperations = GetDifferences(nonTemporalContext, temporalContext);
        var enableSql = GenerateMigrationSql(temporalContext, enableOperations);
        var finalPeriodColumn = enableSql.IndexOf(
            "ADD `ValidTo` timestamp(6) GENERATED ALWAYS AS ROW END",
            StringComparison.Ordinal);

        var periodActivation = enableSql.IndexOf(
            "ADD PERIOD FOR SYSTEM_TIME (`ValidFrom`, `ValidTo`)",
            StringComparison.Ordinal);

        // Assert
        Assert.Contains(
            "ALTER TABLE `TemporalRecords` "
            + "ADD `ValidFrom` timestamp(6) GENERATED ALWAYS AS ROW START, "
            + "ADD `ValidTo` timestamp(6) GENERATED ALWAYS AS ROW END, "
            + "ADD PERIOD FOR SYSTEM_TIME (`ValidFrom`, `ValidTo`), "
            + "ADD SYSTEM VERSIONING;",
            enableSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ADD `ValidTo` timestamp(6) GENERATED ALWAYS AS ROW END",
            enableSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ADD PERIOD FOR SYSTEM_TIME (`ValidFrom`, `ValidTo`)",
            enableSql,
            StringComparison.Ordinal);
        Assert.True(periodActivation > finalPeriodColumn);
        Assert.Contains("ADD SYSTEM VERSIONING", enableSql, StringComparison.Ordinal);
        Assert.DoesNotContain("TemporalRecordsHistory", enableSql, StringComparison.Ordinal);
        Assert.DoesNotContain("TRIGGER", enableSql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that MariaDB temporal deactivation removes versioning before
    /// the period and its columns while retaining history explicitly.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_disables_native_temporal_contract_on_mariadb()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        using var nonTemporalContext = new NonTemporalSchemaContext(
            CreateOptions<NonTemporalSchemaContext>(serverVersion));

        using var temporalContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));

        // Act
        var disableOperations = GetDifferences(temporalContext, nonTemporalContext);
        var disableSql = GenerateMigrationSql(nonTemporalContext, disableOperations);
        var systemVersioningDeactivation = disableSql.IndexOf(
            "DROP SYSTEM VERSIONING",
            StringComparison.Ordinal);

        var periodDeactivation = disableSql.IndexOf(
            "DROP PERIOD FOR SYSTEM_TIME",
            StringComparison.Ordinal);

        var firstPeriodColumnDrop = disableSql.IndexOf(
            "DROP COLUMN `ValidFrom`",
            StringComparison.Ordinal);

        // Assert
        Assert.Contains(
            "SET STATEMENT system_versioning_alter_history=KEEP FOR "
            + "ALTER TABLE `TemporalRecords` DROP SYSTEM VERSIONING, DROP PERIOD FOR SYSTEM_TIME, "
            + "DROP COLUMN `ValidFrom`, DROP COLUMN `ValidTo`;",
            disableSql,
            StringComparison.Ordinal);
        Assert.True(
            disableOperations
                .OfType<AlterTableOperation>()
                .Single()
                .IsDestructiveChange);
        Assert.True(periodDeactivation > systemVersioningDeactivation);
        Assert.True(firstPeriodColumnDrop > periodDeactivation);
        Assert.Contains("DROP COLUMN `ValidFrom`", disableSql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN `ValidTo`", disableSql, StringComparison.Ordinal);
        Assert.Equal(
            firstPeriodColumnDrop,
            disableSql.LastIndexOf("DROP COLUMN `ValidFrom`", StringComparison.Ordinal));
        Assert.DoesNotContain("TemporalRecordsHistory", disableSql, StringComparison.Ordinal);
        Assert.DoesNotContain("TRIGGER", disableSql, StringComparison.Ordinal);
    }
}
