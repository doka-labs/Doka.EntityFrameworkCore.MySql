using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlUpdateSqlGeneratorTests
{
    /// <summary>Preserves early, middle, late, and asymmetric shape checks for wide modification lists.</summary>
    /// <param name="difference">The relevant-column mismatch or ignored-column placement.</param>
    /// <param name="expected">Whether the commands may share an INSERT statement.</param>
    [Theory]
    [InlineData("same", true)]
    [InlineData("first", false)]
    [InlineData("middle", false)]
    [InlineData("last", false)]
    [InlineData("extra", false)]
    [InlineData("extra-reversed", false)]
    [InlineData("ignored-ends", true)]
    public void WideShapeComparisonPreservesRelevantColumnOrder(
        string difference,
        bool expected
    )
    {
        // Arrange
        var firstColumns = Enumerable.Range(0, 50)
            .Select(index => (Name: $"Column{index:D2}", Read: index % 2 != 0, Write: index % 2 == 0))
            .ToArray();

        var secondColumns = firstColumns.ToList();

        switch (difference)
        {
            case "same":
                break;
            case "first":
                secondColumns[0] = ("Other", false, true);
                break;
            case "middle":
                secondColumns[25] = ("Other", true, false);
                break;
            case "last":
                secondColumns[49] = ("Other", true, false);
                break;
            case "extra":
            case "extra-reversed":
                secondColumns.Add(("Extra", false, true));
                break;
            case "ignored-ends":
                secondColumns.Insert(0, ("IgnoredBefore", false, false));
                secondColumns.Add(("IgnoredAfter", false, false));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(difference));
        }

        IReadOnlyModificationCommand first = CreateCommand("Rows", schema: null, firstColumns);
        IReadOnlyModificationCommand second = CreateCommand("Rows", schema: null, secondColumns.ToArray());

        if (difference == "extra-reversed")
        {
            (first, second) = (second, first);
        }

        // Act
        var actual = MySqlModificationCommandBatch.CanBeInsertedInSameStatement(first, second);

        // Assert
        Assert.Equal(expected, actual);
    }
}
