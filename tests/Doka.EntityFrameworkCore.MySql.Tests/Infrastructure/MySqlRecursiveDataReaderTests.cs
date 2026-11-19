namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies recursive guard policies, streaming state and command ownership without server timing.</summary>
public sealed partial class MySqlRecursiveDataReaderTests
{
    private const string Presence = "__doka_present";
    private const string InvalidLimit = "__doka_invalid";

    /// <summary>All valid public Int32 depth values are checked from current invocation values.</summary>
    /// <param name="depth">A valid non-negative depth.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Depth_policy_accepts_non_negative_int32(
        int depth
    )
    {
        // Arrange
        var policy = Policy(["depth"]);
        IReadOnlyDictionary<string, object?> values = new Dictionary<string, object?> { ["depth"] = depth };

        // Act
        var exception = Record.Exception(() => policy.Validate(values));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>Wrong depth representations fail without including their actual values in diagnostics.</summary>
    /// <param name="depth">An invalid value for the public Int32 argument.</param>
    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(1L)]
    [InlineData(1.0)]
    [InlineData(true)]
    [InlineData("depth-secret-canary")]
    public void Depth_policy_rejects_invalid_values_without_disclosing_them(
        object? depth
    )
    {
        // Arrange
        var policy = Policy(["depth"]);
        IReadOnlyDictionary<string, object?> values = new Dictionary<string, object?> { ["depth"] = depth };

        // Act
        var exception = Record.Exception(() => policy.Validate(values));

        // Assert
        var argument = Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Null(argument.ActualValue);
        Assert.DoesNotContain("depth-secret-canary", argument.Message, StringComparison.Ordinal);
    }

    /// <summary>Names are captured once so a compiler builder cannot mutate an already cached policy.</summary>
    [Fact]
    public void Policy_owns_its_parameter_name_copy()
    {
        // Arrange
        var names = new[] { "depth" };
        var policy = Policy(names);
        names[0] = "changed";
        IReadOnlyDictionary<string, object?> values = new Dictionary<string, object?> { ["depth"] = 2 };

        // Act
        var exception = Record.Exception(() => policy.Validate(values));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>Constant-only policies do not require a parameter dictionary.</summary>
    [Fact]
    public void Constant_depth_policy_accepts_a_null_dictionary()
    {
        // Arrange
        var policy = Policy([]);

        // Act
        var exception = Record.Exception(() => policy.Validate(null));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>Named parameters cannot disappear from a later cached execution.</summary>
    [Fact]
    public void Named_depth_policy_rejects_a_missing_dictionary_entry()
    {
        // Arrange
        var policy = Policy(["depth"]);

        // Act
        var exception = Record.Exception(() => policy.Validate(new Dictionary<string, object?>()));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>Priming checks one row without reading payload or consuming the logical first row.</summary>
    [Fact]
    public void Priming_reads_exactly_one_control_row()
    {
        // Arrange
        using var raw = Rows(typeof(int), 7, 8, 9);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));

        // Act
        reader.Prime();

        // Assert
        Assert.Equal(1, raw.ReadCalls);
        Assert.Equal(0, raw.PayloadReads);
        Assert.True(reader.HasRows);
        Assert.Throws<InvalidOperationException>(() => reader.GetInt32(0));
    }

    /// <summary>The primed first row is replayed once and later reads continue streaming.</summary>
    /// <param name="asynchronous">Whether to read asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Primed_first_payload_is_neither_lost_nor_duplicated(
        bool asynchronous
    )
    {
        // Arrange
        using var raw = Rows(typeof(int), 7, 8, 9);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        await reader.PrimeAsync(cancellationToken);
        var values = new List<int>();

        // Act
        while (asynchronous
            ? await reader.ReadAsync(cancellationToken)
            : reader.Read())
        {
            values.Add(reader.GetInt32(0));
        }

        // Assert
        Assert.Equal([7, 8, 9], values);
        Assert.Equal(4, raw.ReadCalls);
    }

    /// <summary>The synthetic empty row never becomes a null payload row.</summary>
    /// <param name="asynchronous">Whether to read asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Valid_empty_payload_skips_the_synthetic_row(
        bool asynchronous
    )
    {
        // Arrange
        using var raw = GuardRow(typeof(int), DBNull.Value, DBNull.Value, 0L);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual = asynchronous
            ? await reader.ReadAsync(cancellationToken)
            : reader.Read();

        // Assert
        Assert.False(actual);
        Assert.False(reader.HasRows);
        Assert.Equal(0, raw.PayloadReads);
        Assert.Equal(1, raw.ReadCalls);
    }

    /// <summary>Presence retains a legitimate all-null payload instead of treating it as empty.</summary>
    [Fact]
    public void Real_null_payload_remains_a_visible_row()
    {
        // Arrange
        using var raw = GuardRow(typeof(string), DBNull.Value, 1L, 0L);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));

        // Act
        var actual = reader.Read();

        // Assert
        Assert.True(actual);
        Assert.True(reader.IsDBNull(0));
        Assert.True(reader.HasRows);
    }

    /// <summary>An invalid server budget fails before any payload getter or successful visible row.</summary>
    /// <param name="asynchronous">Whether to check the first row asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_native_budget_is_rejected_before_payload_access(
        bool asynchronous
    )
    {
        // Arrange
        using var raw = GuardRow(typeof(int), DBNull.Value, DBNull.Value, 1L);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => reader.PrimeAsync(cancellationToken))
            : Record.Exception(reader.Prime);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(1, raw.ReadCalls);
        Assert.Equal(0, raw.PayloadReads);
    }

    /// <summary>Missing or invalid control values cannot be mistaken for successful recursion.</summary>
    /// <param name="invalid">A malformed native control value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    [InlineData(3L)]
    [InlineData("control-secret-canary")]
    public void Malformed_control_values_fail_closed(
        object? invalid
    )
    {
        // Arrange
        using var raw = GuardRow(typeof(int), 7, 1L, invalid ?? DBNull.Value);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));

        // Act
        var exception = Record.Exception(reader.Prime);

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("The recursive query returned an invalid statement-local guard shape.", failure.Message);
        Assert.DoesNotContain("control-secret-canary", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, raw.PayloadReads);
    }

    /// <summary>A cancelled prime does not consume the underlying first row.</summary>
    [Fact]
    public async Task Cancelled_prime_does_not_read_the_driver()
    {
        // Arrange
        using var raw = Rows(typeof(int), 7);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var exception = await Record.ExceptionAsync(() => reader.PrimeAsync(cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(0, raw.ReadCalls);
        Assert.Equal(0, raw.PayloadReads);
    }

    /// <summary>Impossible projection counts fail before ordinal arithmetic can overflow.</summary>
    /// <param name="payloadCount">The invalid payload projection count.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Policy_rejects_invalid_projection_counts(
        int payloadCount
    )
    {
        // Arrange

        // Act
        var exception = Record.Exception(() => Policy([], payloadCount));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>Ambiguous control aliases cannot be used by a query policy.</summary>
    [Fact]
    public void Policy_rejects_identical_control_aliases()
    {
        // Arrange

        // Act
        var exception = Record.Exception(() => new MySqlRecursiveQueryPolicy([], Presence, Presence, 1));

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>A cancelled first visible read cannot replay a primed payload row.</summary>
    [Fact]
    public async Task Cancellation_after_prime_does_not_replay_the_first_row()
    {
        // Arrange
        using var raw = Rows(typeof(int), 7);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        reader.Prime();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var exception = await Record.ExceptionAsync(() => reader.ReadAsync(cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(1, raw.ReadCalls);
        Assert.Equal(0, raw.PayloadReads);
    }

    private static MySqlRecursiveQueryPolicy Policy(
        IReadOnlyList<string> names,
        int payloadCount = 1
    ) => new(names, Presence, InvalidLimit, payloadCount);
}
