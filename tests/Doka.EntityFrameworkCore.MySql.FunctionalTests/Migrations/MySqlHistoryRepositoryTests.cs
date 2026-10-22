namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Tests the MySQL history repository: create script, idempotent scripting,
/// ExistsSql, and InterpretExistsResult.
/// </summary>
public sealed class MySqlHistoryRepositoryTests
{
    // -- GetCreateScript --

    [Fact]
    public void GetCreateScript_produces_valid_create_table_sql()
    {
        // Arrange
        var repo = CreateRepository();

        // Act
        var sql = repo.GetCreateScript();

        // Assert
        Assert.Contains("CREATE TABLE", sql, StringComparison.Ordinal);
        Assert.Contains("`__EFMigrationsHistory`", sql, StringComparison.Ordinal);
        Assert.Contains("`MigrationId` varchar(150) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`ProductVersion` varchar(32) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY", sql, StringComparison.Ordinal);
        Assert.Contains("CHARACTER SET utf8mb4", sql, StringComparison.Ordinal);
    }

    // -- GetCreateIfNotExistsScript --

    [Fact]
    public void GetCreateIfNotExistsScript_contains_if_not_exists()
    {
        // Arrange
        var repo = CreateRepository();

        // Act
        var sql = repo.GetCreateIfNotExistsScript();

        // Assert
        Assert.Contains("CREATE TABLE IF NOT EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("`__EFMigrationsHistory`", sql, StringComparison.Ordinal);
    }

    // -- Idempotent Scripting --

    [Fact]
    public void GetBeginIfNotExistsScript_wraps_in_stored_procedure()
    {
        // Arrange
        var repo = CreateRepository();

        // Act
        var sql = repo.GetBeginIfNotExistsScript("20260410_InitialCreate");

        // Assert
        Assert.Contains("DROP PROCEDURE IF EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("DELIMITER //", sql, StringComparison.Ordinal);
        Assert.Contains("`__ef_apply_migration`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE PROCEDURE", sql, StringComparison.Ordinal);
        Assert.Contains("IF NOT EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("'20260410_InitialCreate'", sql, StringComparison.Ordinal);
        Assert.Contains("`__EFMigrationsHistory`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void GetBeginIfExistsScript_wraps_in_stored_procedure_with_exists()
    {
        // Arrange
        var repo = CreateRepository();

        // Act
        var sql = repo.GetBeginIfExistsScript("20260410_AddColumn");

        // Assert
        Assert.Contains("DROP PROCEDURE IF EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("`__ef_apply_migration`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE PROCEDURE", sql, StringComparison.Ordinal);
        Assert.Contains("IF EXISTS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("IF NOT EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("'20260410_AddColumn'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void GetEndIfScript_calls_and_drops_procedure()
    {
        // Arrange
        var repo = CreateRepository();

        // Act
        var sql = repo.GetEndIfScript();

        // Assert
        Assert.Contains("END IF", sql, StringComparison.Ordinal);
        Assert.Contains("END //", sql, StringComparison.Ordinal);
        Assert.Contains("DELIMITER ;", sql, StringComparison.Ordinal);
        Assert.Contains("CALL `__ef_apply_migration`()", sql, StringComparison.Ordinal);
        Assert.Contains("DROP PROCEDURE IF EXISTS `__ef_apply_migration`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Idempotent_script_round_trip_produces_executable_structure()
    {
        // Arrange
        var repo = CreateRepository();
        var begin = repo.GetBeginIfNotExistsScript("20260410_Test");

        // Act
        var end = repo.GetEndIfScript();
        var full = begin + "        -- migration body\n" + end;

        // Assert
        Assert.Contains("DROP PROCEDURE IF EXISTS", full, StringComparison.Ordinal);
        Assert.Contains("CREATE PROCEDURE", full, StringComparison.Ordinal);
        Assert.Contains("IF NOT EXISTS", full, StringComparison.Ordinal);
        Assert.Contains("-- migration body", full, StringComparison.Ordinal);
        Assert.Contains("END IF", full, StringComparison.Ordinal);
        Assert.Contains("DELIMITER //", full, StringComparison.Ordinal);
        Assert.Contains("DELIMITER ;", full, StringComparison.Ordinal);
        Assert.Contains("CALL", full, StringComparison.Ordinal);
        Assert.Contains("DROP PROCEDURE IF EXISTS", full, StringComparison.Ordinal);
    }

    // -- ExistsSql --

    [Fact]
    public void ExistsSql_queries_information_schema()
    {
        // Arrange
        var repo = CreateRepository();

        // Act
        var existsSql = GetExistsSql(repo);

        // Assert
        Assert.Contains("information_schema.tables", existsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("table_schema = DATABASE()", existsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("__EFMigrationsHistory", existsSql, StringComparison.Ordinal);
        Assert.Contains("CASE", existsSql, StringComparison.Ordinal);
    }

    // -- LockReleaseBehavior --

    [Fact]
    public void LockReleaseBehavior_is_explicit()
    {
        // Arrange
        var repo = CreateRepository();

        // Act
        var behavior = repo.LockReleaseBehavior;

        // Assert
        Assert.Equal(LockReleaseBehavior.Explicit, behavior);
    }

    // -- Helpers --

    private static IHistoryRepository CreateRepository()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<HistoryTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        using var context = new HistoryTestContext(builder.Options);

        return context.GetService<IHistoryRepository>();
    }

    private static string GetExistsSql(
        IHistoryRepository repo
    )
    {
        var property = repo
            .GetType()
            .GetProperty(
                "ExistsSql",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        return (string)property!.GetValue(repo)!;
    }

    private sealed class HistoryTestContext : DbContext
    {
        public HistoryTestContext(
            DbContextOptions<HistoryTestContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<HistoryTestEntity>(e => e.HasKey(x => x.Id));
    }

    private sealed class HistoryTestEntity
    {
        public int Id { get; set; }
    }
}
