namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    /// <summary>
    /// Entity splitting keeps AUTO_INCREMENT on the principal table while the
    /// secondary shared key remains only a primary and cascading foreign key.
    /// </summary>
    [Fact]
    public void Entity_splitting_emits_auto_increment_only_for_the_principal_table()
    {
        // Arrange
        using var source = new EmptyMigrationDslContext(CreateOptions<EmptyMigrationDslContext>());
        using var target = new GeneratedEntitySplitContext(CreateOptions<GeneratedEntitySplitContext>());

        // Act
        var operations = GetDifferences(source, target);
        var tables = operations
            .OfType<CreateTableOperation>()
            .ToDictionary(operation => operation.Name, StringComparer.Ordinal);

        var principal = tables["SplitInventory"];
        var secondary = tables["SplitInventoryDetails"];

        var principalSql = GenerateMigrationSql(target, [principal]);
        var secondarySql = GenerateMigrationSql(target, [secondary]);

        // Assert
        var principalId = Assert.Single(principal.Columns, column => column.Name == "Id");
        var secondaryId = Assert.Single(secondary.Columns, column => column.Name == "Id");
        var secondaryForeignKey = Assert.Single(secondary.ForeignKeys);

        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            principalId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
                ?.Value);
        Assert.Null(secondaryId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy));
        Assert.Equal(["Id"], principal.PrimaryKey!.Columns);
        Assert.Equal(["Id"], secondary.PrimaryKey!.Columns);
        Assert.Equal(["Id"], secondaryForeignKey.Columns);
        Assert.Equal("SplitInventory", secondaryForeignKey.PrincipalTable);
        Assert.Equal(ReferentialAction.Cascade, secondaryForeignKey.OnDelete);

        Assert.Contains("`Id` int NOT NULL AUTO_INCREMENT", principalSql, StringComparison.Ordinal);
        Assert.DoesNotContain("AUTO_INCREMENT", secondarySql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Suppressing the secondary split-table generator must not remove generation
    /// from ordinary keys or invent generation for explicitly non-generated keys.
    /// </summary>
    [Fact]
    public void Entity_splitting_preserves_non_split_and_non_generated_key_contracts()
    {
        // Arrange
        using var source = new EmptyMigrationDslContext(CreateOptions<EmptyMigrationDslContext>());
        using var target = new NonGeneratedEntitySplitContext(CreateOptions<NonGeneratedEntitySplitContext>());

        // Act
        var operations = GetDifferences(source, target);
        var tables = operations
            .OfType<CreateTableOperation>()
            .ToDictionary(operation => operation.Name, StringComparer.Ordinal);

        var principalSql = GenerateMigrationSql(target, [tables["ManualSplitInventory"]]);
        var secondarySql = GenerateMigrationSql(target, [tables["ManualSplitInventoryDetails"]]);

        // Assert
        var ordinaryId = Assert.Single(tables["OrdinaryGeneratedEntities"].Columns, column => column.Name == "Id");
        var principalId = Assert.Single(tables["ManualSplitInventory"].Columns, column => column.Name == "Id");
        var secondaryId = Assert.Single(tables["ManualSplitInventoryDetails"].Columns, column => column.Name == "Id");

        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            ordinaryId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
                ?.Value);
        Assert.NotEqual(
            MySqlValueGenerationStrategy.AutoIncrement,
            principalId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
                ?.Value);
        Assert.NotEqual(
            MySqlValueGenerationStrategy.AutoIncrement,
            secondaryId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
                ?.Value);
        Assert.DoesNotContain(
            "AUTO_INCREMENT",
            principalSql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AUTO_INCREMENT",
            secondarySql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Changing a split key from generated to caller-supplied keeps the old
    /// generator annotation on the principal alteration only. The secondary
    /// shared key must not regain it through removal metadata.
    /// </summary>
    [Fact]
    public void Entity_splitting_alter_history_keeps_generation_on_the_principal_only()
    {
        // Arrange
        using var source = new GeneratedEntitySplitContext(CreateOptions<GeneratedEntitySplitContext>());
        using var target = new NonGeneratedSameTableEntitySplitContext(
            CreateOptions<NonGeneratedSameTableEntitySplitContext>());

        // Act
        var operations = GetDifferences(source, target);
        var idAlterations = operations
            .OfType<AlterColumnOperation>()
            .Where(operation => operation.Name == "Id")
            .ToDictionary(operation => operation.Table, StringComparer.Ordinal);

        var principalId = idAlterations["SplitInventory"];
        var secondaryId = idAlterations["SplitInventoryDetails"];

        // Assert
        Assert.Equal(
            MySqlValueGenerationStrategy.None,
            principalId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
                ?.Value);
        Assert.Equal(
            MySqlValueGenerationStrategy.None,
            principalId.GetMySqlMigrationMetadata().ValueGenerationStrategy);
        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            principalId.OldColumn.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
                ?.Value);
        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            principalId.OldColumn.GetMySqlMigrationMetadata().ValueGenerationStrategy);
        Assert.Equal(
            MySqlValueGenerationStrategy.None,
            secondaryId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
                ?.Value);
        Assert.Equal(
            MySqlValueGenerationStrategy.None,
            secondaryId.GetMySqlMigrationMetadata().ValueGenerationStrategy);
        Assert.Null(secondaryId.OldColumn.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy));
        Assert.Null(secondaryId.OldColumn.GetMySqlMigrationMetadata().ValueGenerationStrategy);
    }
}
