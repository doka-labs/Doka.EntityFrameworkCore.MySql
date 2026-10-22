namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the invisiblecolumns cases and shared support.</summary>
public sealed partial class MySqlNewFeatureTests
{
    // -- MySQL-family INVISIBLE Columns --

    /// <summary>
    /// IsInvisible annotation generates INVISIBLE keyword in column DDL.
    /// </summary>
    [Fact]
    public void IsInvisible_generates_invisible_ddl()
    {
        // Arrange
        using var source = CreateContext<EmptyInvisibleContext>(isMariaDb: true);
        using var context = CreateContext<InvisibleContext>(isMariaDb: true);

        // Act
        var operations = GetDifferences(source, context);

        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());
        var invisibleColumn = Assert.Single(
            createTable.Columns,
            column => column.Name == nameof(InvisibleEntity.InternalData));

        var generator = context.GetService<IMigrationsSqlGenerator>();
        var commands = generator.Generate(operations, context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        // Assert
        Assert.Equal(true, invisibleColumn.FindAnnotation(MySqlAnnotationNames.Invisible)?.Value);
        Assert.Contains("INVISIBLE", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Adding an invisible property to an existing table carries the model annotation
    /// into the standalone column operation.
    /// </summary>
    [Fact]
    public void IsInvisible_propagates_to_standalone_add_column()
    {
        // Arrange
        using var source = CreateContext<InvisibleWithoutColumnContext>(isMariaDb: true);
        using var target = CreateContext<InvisibleContext>(isMariaDb: true);

        // Act
        var operation = Assert.Single(GetDifferences(source, target).OfType<AddColumnOperation>());

        // Assert
        Assert.Equal(nameof(InvisibleEntity.InternalData), operation.Name);
        Assert.Equal(true, operation.FindAnnotation(MySqlAnnotationNames.Invisible)?.Value);
    }

    /// <summary>
    /// Visibility changes preserve both sides of the migration so generated up and
    /// down operations deterministically add or remove the INVISIBLE attribute.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsInvisible_propagates_through_bidirectional_alter_column(
        bool targetIsInvisible
    )
    {
        // Arrange
        using var invisible = CreateContext<InvisibleContext>(isMariaDb: true);
        using var visible = CreateContext<VisibleColumnContext>(isMariaDb: true);
        DbContext source = targetIsInvisible ? visible : invisible;
        DbContext target = targetIsInvisible ? invisible : visible;

        // Act
        var operation = GetDifferences(source, target).OfType<AlterColumnOperation>().Single();
        var sql = string.Join(
            "\n",
            target
                .GetService<IMigrationsSqlGenerator>()
                .Generate([operation], target.Model)
                .Select(command => command.CommandText));

        // Assert
        Assert.Equal(targetIsInvisible, operation.FindAnnotation(MySqlAnnotationNames.Invisible)?.Value);
        Assert.Equal(!targetIsInvisible, operation.OldColumn.FindAnnotation(MySqlAnnotationNames.Invisible)?.Value);
        Assert.Equal(targetIsInvisible, sql.Contains("INVISIBLE", StringComparison.Ordinal));
    }

    /// <summary>
    /// IsInvisible fluent API sets the annotation correctly.
    /// </summary>
    [Fact]
    public void IsInvisible_fluent_api_sets_annotation()
    {
        // Arrange
        using var context = CreateContext<InvisibleContext>(isMariaDb: true);

        // Act
        var property =
            context.Model.FindEntityType(typeof(InvisibleEntity))!.FindProperty(nameof(InvisibleEntity.InternalData))!;

        var annotation = property.FindAnnotation(MySqlAnnotationNames.Invisible);

        // Assert
        Assert.NotNull(annotation);
        Assert.Equal(true, annotation.Value);
    }
}
