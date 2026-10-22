namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Tests for newly implemented features: HiLo, DateOnly/TimeOnly arithmetic,
/// INVISIBLE columns, and JSON functions.
/// </summary>
public sealed partial class MySqlNewFeatureTests
{
    // -- HiLo Value Generation --

    /// <summary>
    /// UseHiLo creates a sequence in the model and sets the HiLo value generation strategy.
    /// </summary>
    [Fact]
    public void UseHiLo_creates_sequence_and_sets_strategy()
    {
        // Arrange
        using var context = CreateContext<HiLoContext>();

        // Act
        var property = context.Model.FindEntityType(typeof(HiLoEntity))!.FindProperty(nameof(HiLoEntity.Id))!;

        var sequence = context.Model.FindSequence("HiLoTestSequence");

        // Assert
        Assert.Equal(MySqlValueGenerationStrategy.HiLo, property.GetMySqlValueGenerationStrategy());
        Assert.Equal(ValueGenerated.OnAdd, property.ValueGenerated);
        Assert.NotNull(sequence);
        Assert.Equal(10, sequence.IncrementBy);
    }

    /// <summary>
    /// UseHiLo with default name creates a convention-based sequence name.
    /// </summary>
    [Fact]
    public void UseHiLo_with_default_name_creates_convention_sequence()
    {
        // Arrange
        using var context = CreateContext<HiLoDefaultContext>();

        // Act
        var property =
            context.Model.FindEntityType(typeof(HiLoDefaultEntity))!.FindProperty(nameof(HiLoDefaultEntity.Id))!;

        var sequences = context
            .Model.GetSequences()
            .ToList();

        // Assert
        Assert.Equal(MySqlValueGenerationStrategy.HiLo, property.GetMySqlValueGenerationStrategy());
        Assert.Single(sequences);
        Assert.Contains("HiLoDefaultEntity", sequences[0].Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// HiLo sequence generates the correct DDL for MySQL (table-based emulation).
    /// </summary>
    [Fact]
    public void HiLo_sequence_generates_mysql_table_based_ddl()
    {
        // Arrange
        using var context = CreateContext<HiLoContext>();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "HiLoTestSequence",
            StartValue = 1,
            IncrementBy = 10,
            ClrType = typeof(long),
        };

        // Act
        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        // Assert
        Assert.Contains("__efsequence_HiLoTestSequence", sql, StringComparison.Ordinal);
    }
}
