using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Shares the infrastructure responsibility without changing the test fixture.</summary>
public sealed partial class MySqlScaffoldingRoundTripTests
{
    private static void AssertDatabaseModel(
        DatabaseModel databaseModel
    )
    {
        Assert.Equal(3, databaseModel.Tables.Count);

        var parent = databaseModel.Tables.Single(table => table.Name == ParentTable);
        var child = databaseModel.Tables.Single(table => table.Name == ChildTable);

        var view = Assert.IsType<DatabaseView>(
            databaseModel.Tables.Single(table => table.Name == SummaryView));

        Assert.Equal("core table", parent.Comment);
        Assert.Equal("PRIMARY", parent.PrimaryKey?.Name);

        var optionalCount = parent.Columns.Single(column => column.Name == "OptionalCount");

        Assert.True(optionalCount.IsNullable);
        Assert.Equal("7", optionalCount.DefaultValueSql);
        Assert.Equal("optional count", optionalCount.Comment);

        var computedCount = parent.Columns.Single(column => column.Name == "ComputedCount");

        Assert.True(computedCount.IsStored);
        Assert.Contains("OptionalCount", computedCount.ComputedColumnSql, StringComparison.OrdinalIgnoreCase);

        var uniqueConstraint = Assert.Single(parent.UniqueConstraints);
        Assert.Equal(UniqueConstraint, uniqueConstraint.Name);
        Assert.Equal("Code", Assert.Single(uniqueConstraint.Columns).Name);

        var checkConstraints = Assert.IsAssignableFrom<IReadOnlyList<MySqlScaffoldedCheckConstraint>>(
            parent.FindAnnotation(MySqlAnnotationNames.ScaffoldingCheckConstraints)?.Value);

        var checkConstraint = Assert.Single(checkConstraints);
        Assert.Equal(CheckConstraint, checkConstraint.Name);
        Assert.Contains("OptionalCount", checkConstraint.Sql, StringComparison.OrdinalIgnoreCase);

        var foreignKey = Assert.Single(child.ForeignKeys);
        Assert.Equal(ForeignKey, foreignKey.Name);
        Assert.Equal(ParentTable, foreignKey.PrincipalTable.Name);
        Assert.Equal(ReferentialAction.Cascade, foreignKey.OnDelete);

        Assert.True(view.Columns.Single(column => column.Name == "OptionalCount").IsNullable);
    }

    private static void AssertGeneratedModel(
        ScaffoldedModel scaffoldedModel
    )
    {
        var contextCode = scaffoldedModel.ContextFile.Code;
        var parentCode = scaffoldedModel
            .AdditionalFiles.Single(
                file => file.Code.Contains(
                    "class DokaScaffoldCoreParent",
                    StringComparison.Ordinal))
            .Code;

        var viewCode = scaffoldedModel
            .AdditionalFiles.Single(
                file => file.Code.Contains(
                    "class DokaScaffoldCoreSummary",
                    StringComparison.Ordinal))
            .Code;

        Assert.Contains($"ToView(\"{SummaryView}\"", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasNoKey()", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasDefaultValueSql(\"7\")", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasComment(\"optional count\")", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasComment(\"core table\")", contextCode, StringComparison.Ordinal);
        Assert.Contains($"HasCheckConstraint(\"{CheckConstraint}\"", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasComputedColumnSql(", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasName(\"PRIMARY\")", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasAlternateKey(e => e.Code)", contextCode, StringComparison.Ordinal);
        Assert.Contains($"HasName(\"{UniqueConstraint}\")", contextCode, StringComparison.Ordinal);
        Assert.DoesNotContain("HasIndex(e => e.Code", contextCode, StringComparison.Ordinal);
        Assert.Contains($"HasConstraintName(\"{ForeignKey}\")", contextCode, StringComparison.Ordinal);
        Assert.Contains("public int? OptionalCount", parentCode, StringComparison.Ordinal);
        Assert.Contains("public int? OptionalCount", viewCode, StringComparison.Ordinal);
    }

    private static void AssertStoreType(
        DatabaseTable table,
        string columnName,
        string expectedStoreType
    )
    {
        Assert.Equal(
            expectedStoreType,
            table.Columns.Single(column => column.Name == columnName).StoreType,
            ignoreCase: true);
    }

    private static async Task CreateSchemaAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await ExecuteNonQueryAsync(
                connection,
                $"""
                DROP VIEW IF EXISTS `{SummaryView}`;
                DROP TABLE IF EXISTS `{ChildTable}`;
                DROP TABLE IF EXISTS `{ParentTable}`;
                CREATE TABLE `{ParentTable}` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `Code` varchar(32) NOT NULL,
                    `OptionalCount` int NULL DEFAULT 7 COMMENT 'optional count',
                    `ComputedCount` int GENERATED ALWAYS AS (IFNULL(`OptionalCount`, 0) + 1) STORED,
                    PRIMARY KEY (`Id`),
                    CONSTRAINT `{UniqueConstraint}` UNIQUE (`Code`),
                    CONSTRAINT `{CheckConstraint}`
                        CHECK (`OptionalCount` IS NULL OR `OptionalCount` >= 0)
                ) ENGINE=InnoDB COMMENT='core table';
                CREATE TABLE `{ChildTable}` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `ParentId` int NOT NULL,
                    PRIMARY KEY (`Id`),
                    CONSTRAINT `{ForeignKey}`
                        FOREIGN KEY (`ParentId`) REFERENCES `{ParentTable}` (`Id`) ON DELETE CASCADE
                ) ENGINE=InnoDB;
                CREATE VIEW `{SummaryView}` AS
                    SELECT `Id`, `OptionalCount`
                    FROM `{ParentTable}`;
                """)
            .ConfigureAwait(false);
    }

    private static async Task DropSchemaAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await ExecuteNonQueryAsync(
                connection,
                $"""
                DROP VIEW IF EXISTS `{SummaryView}`;
                DROP TABLE IF EXISTS `{ChildTable}`;
                DROP TABLE IF EXISTS `{ParentTable}`;
                """)
            .ConfigureAwait(false);
    }

    private static async Task<string> ReadServerVersionAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT VERSION();";

        return Convert.ToString(
                await command
                    .ExecuteScalarAsync()
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("The database did not return a server version.");
    }

    private static async Task<long> ExecuteScalarInt64Async(
        MySqlConnection connection,
        string commandText
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;

        return Convert.ToInt64(
            await command
                .ExecuteScalarAsync()
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteNonQueryAsync(
        MySqlConnection connection,
        string commandText
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;

        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }
}
