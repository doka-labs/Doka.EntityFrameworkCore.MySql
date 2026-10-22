namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the sequences responsibility for MySqlMigrationDdlCoverageTests.</summary>
public sealed partial class MySqlMigrationDdlCoverageTests
{
    // -- ALTER SEQUENCE --

    [Fact]
    public void AlterSequence_mysql_updates_persisted_metadata()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterSequenceOperation
        {
            Name = "TestSeq",
            IncrementBy = 5,
        };

        // Act
        var commands = generator.Generate([operation], context.Model);
        var sql = JoinSql(commands);

        // Assert
        Assert.Contains("UPDATE `__efsequence_TestSeq`", sql, StringComparison.Ordinal);
        Assert.Contains("`increment_by` = 5", sql, StringComparison.Ordinal);
        Assert.Contains("`is_cyclic` = FALSE", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void AlterSequence_mariadb_generates_alter_sequence_sql()
    {
        // Arrange
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterSequenceOperation
        {
            Name = "TestSeq",
            IncrementBy = 5,
        };

        // Act
        var commands = generator.Generate([operation], context.Model);
        var sql = JoinSql(commands);

        // Assert
        Assert.Contains("ALTER SEQUENCE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`TestSeq`", sql, StringComparison.Ordinal);
        Assert.Contains("INCREMENT BY 5", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- CREATE SEQUENCE quoting (emulation + native) --

    [Fact]
    public void CreateSequence_emulation_quotes_table_name_with_backtick_prefix()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "OrderSeq",
            StartValue = 1,
            IncrementBy = 10,
            ClrType = typeof(long),
        };

        // Act
        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains("CREATE TABLE `__efsequence_OrderSeq`", sql, StringComparison.Ordinal);
        Assert.Contains("`id` TINYINT UNSIGNED NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`value` BIGINT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`start_value` BIGINT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`increment_by` INT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`min_value` BIGINT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`max_value` BIGINT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`is_cyclic` BOOLEAN NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`is_called` BOOLEAN NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (`id`)", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (`id` = 1)", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `__efsequence_OrderSeq`", sql, StringComparison.Ordinal);
        Assert.Contains(
            "VALUES (1, 1, 1, 10, 1, 9223372036854775806, FALSE, FALSE)",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSequence_emulation_doubles_embedded_backticks_in_table_name()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "evil`seq",
            StartValue = 1,
            IncrementBy = 1,
            ClrType = typeof(long),
        };

        // Act
        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains("`__efsequence_evil``seq`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("__efsequence_evil`seq`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSequence_native_quotes_sequence_name_with_backticks()
    {
        // Arrange
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "OrderSeq",
            StartValue = 100,
            IncrementBy = 5,
            ClrType = typeof(long),
        };

        // Act
        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains("CREATE SEQUENCE `OrderSeq`", sql, StringComparison.Ordinal);
        Assert.Contains("START WITH 100", sql, StringComparison.Ordinal);
        Assert.Contains("INCREMENT BY 5", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSequence_native_doubles_embedded_backticks_in_sequence_name()
    {
        // Arrange
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "evil`seq",
            StartValue = 1,
            IncrementBy = 1,
            ClrType = typeof(long),
        };

        // Act
        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains("CREATE SEQUENCE `evil``seq`", sql, StringComparison.Ordinal);
    }

    // -- DROP SEQUENCE quoting --

    [Fact]
    public void DropSequence_emulation_quotes_emulation_table_name()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new DropSequenceOperation { Name = "evil`seq" };

        // Act
        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains("DROP TABLE IF EXISTS `__efsequence_evil``seq`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DropSequence_native_quotes_sequence_name()
    {
        // Arrange
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new DropSequenceOperation { Name = "evil`seq" };

        // Act
        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains("DROP SEQUENCE IF EXISTS `evil``seq`", sql, StringComparison.Ordinal);
    }

    // -- RENAME SEQUENCE quoting --

    [Fact]
    public void RenameSequence_emulation_quotes_old_and_new_emulation_table_names()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RenameSequenceOperation
        {
            Name = "OldSeq",
            NewName = "evil`new",
        };

        // Act
        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains(
            "RENAME TABLE `__efsequence_OldSeq` TO `__efsequence_evil``new`",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RenameSequence_native_renames_the_sequence_table()
    {
        // Arrange
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RenameSequenceOperation
        {
            Name = "OldSeq",
            NewName = "NewSeq",
        };

        // Act
        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains("RENAME TABLE `OldSeq` TO `NewSeq`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartSequence_native_updates_start_metadata_and_next_value()
    {
        // Arrange
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RestartSequenceOperation
        {
            Name = "TestSeq",
            StartValue = 3,
        };

        // Act
        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains("START WITH 3 RESTART WITH 3", sql, StringComparison.Ordinal);
    }
}
