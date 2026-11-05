namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlStraightJoinTests
{
    /// <summary>Preserves mapped scalar access for owned and ordinary join payloads.</summary>
    /// <param name="mariaDb">Whether to configure the MariaDB provider profile.</param>
    /// <param name="entityKind">The payload entity whose convention mapping is required.</param>
    [Theory]
    [InlineData(false, nameof(JoinDetails))]
    [InlineData(true, nameof(JoinDetails))]
    [InlineData(false, nameof(JoinTag))]
    [InlineData(true, nameof(JoinTag))]
    [InlineData(false, nameof(JoinItem))]
    [InlineData(true, nameof(JoinItem))]
    public void Join_payload_scalars_remain_mapped(
        bool mariaDb,
        string entityKind
    )
    {
        // Arrange
        const string expected = "mapped join payload";
        using var context = CreateContext(mariaDb);

        // WHY: Real writes preserve the fixture's mutable contract during future cleanup.
        object payload = entityKind switch
        {
            nameof(JoinDetails) => new JoinDetails { Text = expected },
            nameof(JoinTag) => new JoinTag { Text = expected },
            nameof(JoinItem) => new JoinItem { Text = expected },
            _ => throw new ArgumentOutOfRangeException(nameof(entityKind), entityKind, null),
        };

        // Act
        var property = context.Model.FindEntityType(payload.GetType())?.FindProperty(nameof(JoinDetails.Text));
        var mappedValue = property?.GetGetter().GetClrValue(payload);

        // Assert
        Assert.NotNull(property);
        Assert.Equal(typeof(string), property.ClrType);
        Assert.Equal(expected, mappedValue);
    }
}
