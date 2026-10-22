namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the JSON transport contract for MySQL-family <c>TIME</c> values.
/// </summary>
public sealed class MySqlTimeSpanJsonValueReaderWriterTests
{
    private static readonly TimeSpan s_maximum = TimeSpan.FromHours(838)
        + TimeSpan.FromMinutes(59)
        + TimeSpan.FromSeconds(59);

    /// <summary>
    /// Verifies ordinary positive and negative elapsed values round-trip
    /// without losing hours above one day.
    /// </summary>
    [Theory]
    [InlineData(27)]
    [InlineData(-27)]
    public void Supported_elapsed_values_round_trip(
        int hours
    )
    {
        // Arrange
        var readerWriter = MySqlTimeSpanJsonValueReaderWriter.Instance;
        var expected = TimeSpan.FromHours(hours);

        // Act
        var json = readerWriter.ToJsonString(expected);
        var actual = readerWriter.FromJsonString(json);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies both exact signed server boundaries remain transportable.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Exact_server_boundaries_are_accepted(
        int direction
    )
    {
        // Arrange
        var readerWriter = MySqlTimeSpanJsonValueReaderWriter.Instance;
        var expected = direction > 0 ? s_maximum : -s_maximum;

        // Act
        var json = readerWriter.ToJsonString(expected);
        var actual = readerWriter.FromJsonString(json);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies values beyond either signed server boundary fail before they
    /// can be saturated by a server-side <c>TIME</c> cast.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Values_outside_server_boundaries_are_rejected(
        int direction
    )
    {
        // Arrange
        var readerWriter = MySqlTimeSpanJsonValueReaderWriter.Instance;
        var value = direction > 0
            ? s_maximum + TimeSpan.FromTicks(1)
            : -s_maximum - TimeSpan.FromTicks(1);

        Action act = () => readerWriter.ToJsonString(value);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Contains("exceeds the MySQL TIME range", exception.Message, StringComparison.Ordinal);
    }

}
