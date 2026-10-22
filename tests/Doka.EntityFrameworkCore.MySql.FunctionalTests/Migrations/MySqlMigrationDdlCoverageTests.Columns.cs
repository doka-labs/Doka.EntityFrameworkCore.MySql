namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the columns responsibility for MySqlMigrationDdlCoverageTests.</summary>
public sealed partial class MySqlMigrationDdlCoverageTests
{
    // -- RENAME COLUMN --

    [Fact]
    public void RenameColumn_generates_alter_table_rename_column_sql()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RenameColumnOperation
        {
            Table = "Products",
            Name = "OldCol",
            NewName = "NewCol",
        };

        // Act
        var commands = generator.Generate([operation], context.Model);
        var sql = JoinSql(commands);

        // Assert
        Assert.Contains("ALTER TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RENAME COLUMN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`OldCol`", sql, StringComparison.Ordinal);
        Assert.Contains("`NewCol`", sql, StringComparison.Ordinal);
    }

    // -- ROW VERSION --

    [Fact]
    public void Temporal_row_version_generates_current_timestamp_clauses()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AddColumnOperation
        {
            Table = "AuditEntries",
            Name = "Version",
            ClrType = typeof(byte[]),
            ColumnType = "timestamp(6)",
            IsRowVersion = true,
        };

        // Act
        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains("DEFAULT CURRENT_TIMESTAMP(6)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON UPDATE CURRENT_TIMESTAMP(6)", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Non_temporal_row_version_does_not_generate_current_timestamp_clauses()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AddColumnOperation
        {
            Table = "AuditEntries",
            Name = "Version",
            ClrType = typeof(string),
            ColumnType = "varchar(64)",
            IsRowVersion = true,
            DefaultValue = "initial",
        };

        // Act
        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.DoesNotContain("CURRENT_TIMESTAMP", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`Version` varchar(64)", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that an application-selected value repairs existing null rows
    /// before the provider makes the column required.
    /// </summary>
    [Theory]
    [InlineData(typeof(string), "varchar(64)", "ready", "'ready'")]
    [InlineData(typeof(int), "int", 1, "1")]
    [InlineData(typeof(string), "json", "{}", "'{}'")]
    [InlineData(typeof(string), "enum('alpha','beta')", "alpha", "'alpha'")]
    public void Nullable_column_becoming_required_uses_explicit_backfill(
        Type clrType,
        string columnType,
        object defaultValue,
        string expectedLiteral
    )
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterColumnOperation
        {
            Table = "Entries",
            Name = "Value",
            ClrType = clrType,
            ColumnType = columnType,
            IsNullable = false,
            DefaultValue = defaultValue,
            OldColumn =
            {
                ClrType = clrType,
                ColumnType = columnType,
                IsNullable = true,
            },
        };

        // Act
        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains(
            $"UPDATE `Entries` SET `Value` = {expectedLiteral} WHERE `Value` IS NULL;",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("MODIFY COLUMN `Value`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Prevents the provider from inventing data that may violate a store domain,
    /// a check constraint, or application semantics.
    /// </summary>
    [Theory]
    [InlineData(typeof(string), "varchar(64)")]
    [InlineData(typeof(int), "int")]
    [InlineData(typeof(byte[]), "varbinary(16)")]
    [InlineData(typeof(DateOnly), "date")]
    [InlineData(typeof(DateTime), "datetime(6)")]
    [InlineData(typeof(string), "json")]
    [InlineData(typeof(byte[]), "point")]
    [InlineData(typeof(string), "enum('alpha','beta')")]
    public void Nullable_column_becoming_required_rejects_an_implicit_backfill(
        Type clrType,
        string columnType
    )
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterColumnOperation
        {
            Table = "Entries",
            Name = "SecretColumn",
            ClrType = clrType,
            ColumnType = columnType,
            IsNullable = false,
            OldColumn =
            {
                ClrType = clrType,
                ColumnType = columnType,
                IsNullable = true,
            },
        };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains(nameof(AlterColumnOperation), exception.Message, StringComparison.Ordinal);
        Assert.Contains("explicit DefaultValue or DefaultValueSql", exception.Message, StringComparison.Ordinal);
        Assert.Contains("application contract", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Entries", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretColumn", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_timestamp_becoming_required_requires_explicit_backfill()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var operation = CreateNullableTimestampRepair();

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains(nameof(AlterColumnOperation), exception.Message, StringComparison.Ordinal);
        Assert.Contains("timestamp(6)", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Entries", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("OccurredAt", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_timestamp_uses_the_effective_model_store_type_when_the_operation_omits_it()
    {
        // Arrange
        using var context = CreateTimestampMappingContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var operation = CreateNullableTimestampRepair();
        operation.ColumnType = null;
        operation.OldColumn.ColumnType = null;

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate(
            [operation],
            context.GetService<IDesignTimeModel>()
                .Model));

        // Assert
        Assert.Contains(nameof(AlterColumnOperation), exception.Message, StringComparison.Ordinal);
        Assert.Contains("timestamp(6)", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Required_timestamp_that_was_already_required_needs_no_repair_backfill()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var operation = CreateNullableTimestampRepair();
        operation.OldColumn.IsNullable = false;

        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.DoesNotContain("UPDATE `Entries`", sql, StringComparison.Ordinal);
        Assert.Contains("MODIFY COLUMN `OccurredAt`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_timestamp_becoming_required_rejects_an_explicit_clr_value()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var operation = CreateNullableTimestampRepair();
        operation.DefaultValue = new DateTime(
            2026,
            8,
            21,
            12,
            34,
            56,
            789);

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains("requires DefaultValueSql", exception.Message, StringComparison.Ordinal);
        Assert.Contains("session time zone", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Entries", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("OccurredAt", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_timestamp_becoming_required_accepts_explicit_sql_expression()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var operation = CreateNullableTimestampRepair();
        operation.DefaultValueSql = "CURRENT_TIMESTAMP(6)";

        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains(
            "UPDATE `Entries` SET `OccurredAt` = CURRENT_TIMESTAMP(6) " + "WHERE `OccurredAt` IS NULL;",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_timestamp_becoming_required_rejects_whitespace_sql_expression()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var operation = CreateNullableTimestampRepair();
        operation.DefaultValueSql = "   ";

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains("explicit DefaultValue or DefaultValueSql", exception.Message, StringComparison.Ordinal);
    }
}
