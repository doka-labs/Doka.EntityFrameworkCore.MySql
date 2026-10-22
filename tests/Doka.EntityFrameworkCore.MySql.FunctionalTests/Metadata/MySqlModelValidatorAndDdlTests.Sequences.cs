namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the sequences cases and shared support.</summary>
public sealed partial class MySqlModelValidatorAndDdlTests
{
    // -- Sequence DDL: Drop --

    [Fact]
    public void DropSequence_generates_mysql_drop_table_for_emulated_sequence()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new DropSequenceOperation { Name = "TestSequence" };

        // Act
        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        // Assert
        Assert.Contains("DROP TABLE IF EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("__efsequence_TestSequence", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DropSequence_generates_mariadb_native_drop_sequence()
    {
        // Arrange
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new DropSequenceOperation { Name = "TestSequence" };

        // Act
        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        // Assert
        Assert.Contains("DROP SEQUENCE IF EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`TestSequence`", sql, StringComparison.Ordinal);
    }

    // -- Sequence DDL: Create --

    [Fact]
    public void CreateSequence_generates_mysql_table_emulation()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "OrderSequence",
            StartValue = 100,
            IncrementBy = 5,
            ClrType = typeof(long),
        };

        // Act
        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        // Assert
        Assert.Contains("__efsequence_OrderSequence", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateSequence_generates_mariadb_native_create_sequence()
    {
        // Arrange
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "OrderSequence",
            StartValue = 100,
            IncrementBy = 5,
            ClrType = typeof(long),
        };

        // Act
        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        // Assert
        Assert.Contains("CREATE SEQUENCE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`OrderSequence`", sql, StringComparison.Ordinal);
        Assert.Contains("START WITH 100", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INCREMENT BY 5", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- Sequence DDL: Rename --

    [Fact]
    public void RenameSequence_generates_mysql_rename_table()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RenameSequenceOperation
        {
            Name = "OldSeq",
            NewName = "NewSeq"
        };

        // Act
        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        // Assert
        Assert.Contains("RENAME TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("__efsequence_OldSeq", sql, StringComparison.Ordinal);
        Assert.Contains("__efsequence_NewSeq", sql, StringComparison.Ordinal);
    }
}
