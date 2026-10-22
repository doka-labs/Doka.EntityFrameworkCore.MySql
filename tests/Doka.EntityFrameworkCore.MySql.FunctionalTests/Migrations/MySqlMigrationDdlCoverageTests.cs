namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Coverage tests for migration DDL operations: Rename Table/Column, AlterSequence,
/// Spatial Index, INVISIBLE, identifier normalization, and argument validation.
/// </summary>
public sealed partial class MySqlMigrationDdlCoverageTests
{
    private static readonly int[] s_mixedPrefixLengths = [32, 0];
    private static readonly int[] s_singlePrefixLength = [16];
    private static readonly int[] s_invalidPrefixLengths = [-1, 0];
    private static readonly int[] s_fullTextPrefixLengths = [16, 0];

    // -- RENAME TABLE --

    [Fact]
    public void RenameTable_generates_rename_table_sql()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RenameTableOperation
        {
            Name = "OldTable",
            NewName = "NewTable",
        };

        // Act
        var commands = generator.Generate([operation], context.Model);
        var sql = JoinSql(commands);

        // Assert
        Assert.Contains("RENAME TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`OldTable`", sql, StringComparison.Ordinal);
        Assert.Contains("`NewTable`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Qualified_table_operations_preserve_database_name()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        MigrationOperation[] operations =
        [
            new DropIndexOperation
            {
                Name = "IX_Entries_Code",
                Schema = "tenant_database",
                Table = "Entries",
            },
            new RenameIndexOperation
            {
                Name = "IX_Entries_Old",
                NewName = "IX_Entries_New",
                Schema = "tenant_database",
                Table = "Entries",
            },
            new DropForeignKeyOperation
            {
                Name = "FK_Entries_Parents",
                Schema = "tenant_database",
                Table = "Entries",
            },
            new RenameColumnOperation
            {
                Name = "OldCode",
                NewName = "Code",
                Schema = "tenant_database",
                Table = "Entries",
            },
        ];

        // Act
        var sql = JoinSql(generator.Generate(operations, context.Model));

        // Assert
        Assert.Equal(
            4,
            sql.Split("ALTER TABLE `tenant_database`.`Entries`", StringSplitOptions.None).Length - 1);
    }

    /// <summary>
    /// Verifies that convention-generated foreign-key names are normalized to the
    /// cross-engine 64-character identifier limit before model validation.
    /// </summary>
    [Fact]
    public void Model_with_long_fk_name_builds_with_normalized_constraint_name()
    {
        // Arrange
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<ConstraintTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        using var context = new ConstraintTestContext(builder.Options);
        var childEntity = context.Model.FindEntityType(typeof(ChildEntity))
            ?? throw new InvalidOperationException("ChildEntity metadata was not created.");

        var foreignKey = Assert.Single(childEntity.GetForeignKeys());

        // Act
        var constraintName = foreignKey.GetConstraintName()
            ?? throw new InvalidOperationException("Foreign-key constraint name was not generated.");

        // Assert
        Assert.True(
            constraintName.Length <= 64,
            $"Convention-generated constraint name '{constraintName}' exceeds 64 characters.");
    }

    /// <summary>
    /// Verifies that every migration path carrying user text uses the same
    /// SQL-mode-independent UTF-8 literal contract.
    /// </summary>
    [Fact]
    public void Migration_text_payloads_use_mode_independent_utf8_literals()
    {
        // Arrange
        const string tableComment = "table\\comment 'quoted'";
        const string columnComment = "column\\comment 'quoted'";
        const string insertedValue = "inserted\\value";
        const string updatedValue = "updated\\value";
        const string updateKey = "update\\key";
        const string deleteKey = "delete\\key";
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var createTable = new CreateTableOperation
        {
            Name = "LiteralContracts",
            Comment = tableComment,
        };

        createTable.Columns.Add(
            new AddColumnOperation
            {
                Table = createTable.Name,
                Name = "Value",
                ClrType = typeof(string),
                ColumnType = "longtext",
                IsNullable = false,
                Comment = columnComment,
            });
        var insertData = new InsertDataOperation
        {
            Table = createTable.Name,
            Columns = ["Value"],
            ColumnTypes = ["longtext"],
            Values = new object[,]
            {
                { insertedValue },
            },
        };

        var updateData = new UpdateDataOperation
        {
            Table = createTable.Name,
            Columns = ["Value"],
            ColumnTypes = ["longtext"],
            Values = new object[,]
            {
                { updatedValue },
            },
            KeyColumns = ["Value"],
            KeyColumnTypes = ["longtext"],
            KeyValues = new object[,]
            {
                { updateKey },
            },
        };

        var deleteData = new DeleteDataOperation
        {
            Table = createTable.Name,
            KeyColumns = ["Value"],
            KeyColumnTypes = ["longtext"],
            KeyValues = new object[,]
            {
                { deleteKey },
            },
        };

        // Act
        var sql = JoinSql(generator.Generate([createTable, insertData, updateData, deleteData], context.Model));

        // Assert
        Assert.Contains(
            "/*! SET @__doka_previous_sql_mode = @@SESSION.sql_mode */;",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("'NO_BACKSLASH_ESCAPES'", sql, StringComparison.Ordinal);
        Assert.Contains(
            "/*! SET SESSION sql_mode = @__doka_previous_sql_mode */;",
            sql,
            StringComparison.Ordinal);
        Assert.Contains($"COMMENT = {GenerateExpectedDdlComment(tableComment)}", sql, StringComparison.Ordinal);
        Assert.Contains($"COMMENT {GenerateExpectedDdlComment(columnComment)}", sql, StringComparison.Ordinal);
        Assert.Contains(GenerateExpectedHexLiteral(insertedValue), sql, StringComparison.Ordinal);
        Assert.Contains(GenerateExpectedHexLiteral(updatedValue), sql, StringComparison.Ordinal);
        Assert.Contains(GenerateExpectedHexLiteral(updateKey), sql, StringComparison.Ordinal);
        Assert.Contains(GenerateExpectedHexLiteral(deleteKey), sql, StringComparison.Ordinal);
    }

    // -- HiLo for short type --

    [Fact]
    public void UseHiLo_on_short_property_sets_strategy()
    {
        // Arrange
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<HiLoShortContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        using var context = new HiLoShortContext(builder.Options);

        // Act
        var property = context.Model.FindEntityType(typeof(HiLoShortEntity))!.FindProperty(nameof(HiLoShortEntity.Id))!;

        // Assert
        Assert.Equal(MySqlValueGenerationStrategy.HiLo, property.GetMySqlValueGenerationStrategy());
    }
}
