namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlRecursiveDataReaderTests
{
    /// <summary>Supplies one independent getter and invalid ordinal per case.</summary>
    public static TheoryData<int, int> HiddenOrdinals
    {
        get
        {
            var cases = new TheoryData<int, int>();

            foreach (var operation in Enumerable.Range(0, 13))
            {
                foreach (var ordinal in new[] { -1, 1, 2 })
                {
                    cases.Add(operation, ordinal);
                }
            }

            return cases;
        }
    }

    /// <summary>Reports both hidden aliases with exact and case-insensitive native name matching.</summary>
    public static TheoryData<string, bool> HiddenColumnNames
    {
        get
        {
            var cases = new TheoryData<string, bool>();

            foreach (var name in new[] { Presence, InvalidLimit })
            {
                cases.Add(name, false);
                cases.Add(name, true);
                cases.Add(name.ToUpperInvariant(), false);
                cases.Add(name.ToUpperInvariant(), true);
            }

            return cases;
        }
    }

    /// <summary>Every visible accessor rejects both trailing controls and negative ordinals.</summary>
    /// <param name="operation">The individual accessor to exercise.</param>
    /// <param name="ordinal">The invalid payload ordinal.</param>
    [Theory]
    [MemberData(nameof(HiddenOrdinals))]
    public void Hidden_control_ordinals_are_not_accessible(
        int operation,
        int ordinal
    )
    {
        // Arrange
        using var raw = Rows(typeof(int), 7);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        Assert.True(reader.Read());

        // Act
        var exception = Record.Exception(() => Access(reader, operation, ordinal));

        // Assert
        Assert.IsType<IndexOutOfRangeException>(exception);
        Assert.Equal(0, raw.PayloadReads);
    }

    /// <summary>Control aliases cannot bypass ordinal hiding through name-based lookup.</summary>
    /// <param name="name">The hidden alias.</param>
    /// <param name="indexer">Whether to exercise the named value indexer.</param>
    [Theory]
    [MemberData(nameof(HiddenColumnNames))]
    public void Hidden_control_names_are_not_accessible(
        string name,
        bool indexer
    )
    {
        // Arrange
        using var raw = Rows(typeof(int), 7);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        Assert.True(reader.Read());

        // Act
        var exception = Record.Exception(() => indexer
            ? reader[name]
            : reader.GetOrdinal(name));

        // Assert
        var missing = Assert.IsType<IndexOutOfRangeException>(exception);
        Assert.Equal($"The column name '{name}' does not exist in the result set.", missing.Message);
        Assert.Equal(0, raw.PayloadReads);
    }

    /// <summary>A control-only result has no visible names, even before its first Read.</summary>
    /// <param name="name">The hidden name spelling.</param>
    /// <param name="indexer">Whether to exercise the named value indexer.</param>
    [Theory]
    [MemberData(nameof(HiddenColumnNames))]
    public void Empty_payload_does_not_expose_control_names(
        string name,
        bool indexer
    )
    {
        // Arrange
        var table = new DataTable();
        table.Columns.Add(Presence, typeof(long));
        table.Columns.Add(InvalidLimit, typeof(long));
        using var raw = new ReaderProbe(table);
        var policy = new MySqlRecursiveQueryPolicy([], Presence, InvalidLimit, 0);
        using var reader = new MySqlRecursiveDataReader(raw, policy);

        // Act
        var exception = Record.Exception(() => indexer
            ? reader[name]
            : reader.GetOrdinal(name));

        // Assert
        Assert.IsType<IndexOutOfRangeException>(exception);
        Assert.Equal(0, reader.FieldCount);
        Assert.Equal(0, raw.ReadCalls);
        Assert.Equal(0, raw.PayloadReads);
    }

    /// <summary>Visible name lookup retains the underlying reader's case matching and exact payload value.</summary>
    /// <param name="name">The visible column spelling.</param>
    /// <param name="indexer">Whether to read the value rather than its ordinal.</param>
    [Theory]
    [InlineData("payload", false)]
    [InlineData("PAYLOAD", false)]
    [InlineData("payload", true)]
    [InlineData("PAYLOAD", true)]
    public void Visible_column_names_preserve_native_lookup_and_values(
        string name,
        bool indexer
    )
    {
        // Arrange
        using var raw = Rows(typeof(int), 7);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        Assert.True(reader.Read());

        // Act
        var actual = indexer
            ? reader[name]
            : reader.GetOrdinal(name);

        // Assert
        Assert.Equal(indexer
                ? 7
                : 0,
            actual);

        Assert.Equal(indexer
                ? 1
                : 0,
            raw.PayloadReads);
    }

    /// <summary>A genuine driver lookup failure is propagated without wrapping or value access.</summary>
    /// <param name="indexer">Whether to exercise the named value indexer.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Named_lookup_preserves_the_underlying_failure(
        bool indexer
    )
    {
        // Arrange
        using var raw = Rows(typeof(int), 7);
        var failure = new InvalidOperationException("native-invalid-reader-state");
        raw.OrdinalFailure = failure;
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));

        // Act
        var actual = Record.Exception(() => indexer
            ? reader["missing"]
            : reader.GetOrdinal("missing"));

        // Assert
        Assert.Same(failure, actual);
        Assert.Equal(0, raw.PayloadReads);
        Assert.Equal(0, raw.ReadCalls);
    }

    /// <summary>Bulk getters fill only visible values and retain untouched caller-array slots.</summary>
    /// <param name="providerSpecific">Whether to use provider-specific bulk values.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bulk_getters_do_not_copy_control_values(
        bool providerSpecific
    )
    {
        // Arrange
        using var raw = Rows(typeof(int), 7);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        Assert.True(reader.Read());
        object[] values = ["untouched", "untouched", "untouched", "untouched"];

        // Act
        var count = providerSpecific
            ? reader.GetProviderSpecificValues(values)
            : reader.GetValues(values);

        // Assert
        Assert.Equal(1, count);
        Assert.Equal(7, values[0]);
        Assert.All(values.Skip(1), value => Assert.Equal("untouched", value));
    }

    /// <summary>Native typed getters retain their exact payload representation.</summary>
    /// <param name="operation">The individual typed getter.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void Typed_getters_preserve_payload_values(
        int operation
    )
    {
        // Arrange
        var expected = TypedValue(operation);
        using var raw = Rows(expected.GetType(), expected);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        Assert.True(reader.Read());

        // Act
        var actual = ReadTyped(reader, operation);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Generic asynchronous getters keep the provider value and caller token path.</summary>
    [Fact]
    public async Task Generic_async_getter_preserves_guid()
    {
        // Arrange
        var expected = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        using var raw = Rows(typeof(Guid), expected);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        Assert.True(reader.Read());
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual = await reader.GetFieldValueAsync<Guid>(0, cancellationToken);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Byte chunks are delegated without exposing controls or changing offsets.</summary>
    [Fact]
    public void Binary_chunks_preserve_offsets()
    {
        // Arrange
        using var raw = Rows(typeof(byte[]), new byte[] { 1, 2, 3, 4 });
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        Assert.True(reader.Read());
        var buffer = new byte[2];

        // Act
        var count = reader.GetBytes(0, 1, buffer, 0, buffer.Length);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(new byte[] { 2, 3 }, buffer);
    }

    /// <summary>Character chunks retain their original offsets.</summary>
    [Fact]
    public void Character_chunks_preserve_offsets()
    {
        // Arrange
        using var raw = Rows(typeof(char[]), "alpha".ToCharArray());
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        Assert.True(reader.Read());
        var buffer = new char[2];

        // Act
        var count = reader.GetChars(0, 1, buffer, 0, buffer.Length);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal("lp", new string(buffer));
    }

    /// <summary>The provider's binary stream remains available for visible payload.</summary>
    [Fact]
    public void Binary_stream_preserves_payload()
    {
        // Arrange
        using var raw = Rows(typeof(byte[]), new byte[] { 1, 2, 3 });
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        Assert.True(reader.Read());
        var bytes = new byte[3];

        // Act
        using var stream = reader.GetStream(0);
        var count = stream.Read(bytes);

        // Assert
        Assert.Equal(3, count);
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
    }

    /// <summary>The provider's text reader remains available for visible payload.</summary>
    [Fact]
    public void Text_reader_preserves_payload()
    {
        // Arrange
        using var raw = Rows(typeof(string), "payload");
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        Assert.True(reader.Read());

        // Act
        using var text = reader.GetTextReader(0);
        var actual = text.ReadToEnd();

        // Assert
        Assert.Equal("payload", actual);
    }

    /// <summary>Asynchronous getters cannot expose either hidden control value.</summary>
    /// <param name="ordinal">The inaccessible control ordinal.</param>
    /// <param name="nullCheck">Whether to invoke the asynchronous null getter.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task Async_getters_hide_control_values(
        int ordinal,
        bool nullCheck
    )
    {
        // Arrange
        using var raw = Rows(typeof(int), 7);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        Assert.True(reader.Read());
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = nullCheck
            ? await Record.ExceptionAsync(() => reader.IsDBNullAsync(ordinal, cancellationToken))
            : await Record.ExceptionAsync(() => reader.GetFieldValueAsync<long>(ordinal, cancellationToken));

        // Assert
        Assert.IsType<IndexOutOfRangeException>(exception);
        Assert.Equal(0, raw.PayloadReads);
    }

    private static object Access(
        MySqlRecursiveDataReader reader,
        int operation,
        int ordinal
    ) => operation switch
    {
        0 => reader.GetName(ordinal),
        1 => reader.GetFieldType(ordinal),
        2 => reader.GetValue(ordinal),
        3 => reader.GetProviderSpecificValue(ordinal),
        4 => reader.GetFieldValue<long>(ordinal),
        5 => reader.GetInt64(ordinal),
        6 => reader.IsDBNull(ordinal),
        7 => reader.GetDataTypeName(ordinal),
        8 => reader.GetBytes(ordinal, 0, null, 0, 0),
        9 => reader.GetChars(ordinal, 0, null, 0, 0),
        10 => reader.GetStream(ordinal),
        11 => reader.GetTextReader(ordinal),
        12 => reader.GetProviderSpecificFieldType(ordinal),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static object TypedValue(
        int operation
    ) => operation switch
    {
        0 => true,
        1 => (byte)7,
        2 => 'x',
        3 => new DateTime(
            2026,
            11,
            17,
            1,
            2,
            3,
            DateTimeKind.Utc),
        4 => 1.25m,
        5 => 1.25d,
        6 => 1.25f,
        7 => Guid.Parse("12345678-1234-1234-1234-123456789abc"),
        8 => (short)7,
        9 => 7,
        10 => 7L,
        11 => "payload",
        12 => DateTimeOffset.UnixEpoch,
        13 => TimeSpan.FromMinutes(2),
        14 => new byte[] { 1, 2, 3 },
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static object ReadTyped(
        MySqlRecursiveDataReader reader,
        int operation
    ) => operation switch
    {
        0 => reader.GetBoolean(0),
        1 => reader.GetByte(0),
        2 => reader.GetChar(0),
        3 => reader.GetDateTime(0),
        4 => reader.GetDecimal(0),
        5 => reader.GetDouble(0),
        6 => reader.GetFloat(0),
        7 => reader.GetGuid(0),
        8 => reader.GetInt16(0),
        9 => reader.GetInt32(0),
        10 => reader.GetInt64(0),
        11 => reader.GetString(0),
        12 => reader.GetFieldValue<DateTimeOffset>(0),
        13 => reader.GetFieldValue<TimeSpan>(0),
        14 => reader.GetFieldValue<byte[]>(0),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
