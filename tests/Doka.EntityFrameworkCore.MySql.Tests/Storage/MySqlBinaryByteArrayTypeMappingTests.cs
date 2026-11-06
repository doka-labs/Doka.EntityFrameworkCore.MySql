using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies byte-preserving fixed-binary reads and the mapping contracts reused
/// by EF buffering, converters, and generated compiled models.
/// </summary>
public sealed class MySqlBinaryByteArrayTypeMappingTests
{
    /// <summary>Supplies GUID values with distinguishable byte-order boundaries.</summary>
    public static IEnumerable<object[]> GuidCases =>
    [
        [Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), "00112233445566778899AABBCCDDEEFF"],
        [Guid.Empty, "00000000000000000000000000000000"],
        [Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"],
    ];

    /// <summary>Supplies binary values that must not be copied or interpreted as GUIDs.</summary>
    public static IEnumerable<object[]> ByteArrayCases =>
    [
        [Array.Empty<byte>()],
        [
            new byte[]
            {
                0,
                1,
                127,
                128,
                255,
            },
        ],
        [Convert.FromHexString("00112233445566778899AABBCCDDEEFF")],
        [new byte[32]],
    ];

    /// <summary>Supplies invalid non-null provider values without disclosing their contents.</summary>
    public static IEnumerable<object[]> UnsupportedValueCases =>
    [
        ["secret-binary-payload", typeof(string).FullName!],
        [42, typeof(int).FullName!],
        [DBNull.Value, typeof(DBNull).FullName!],
    ];

    /// <summary>
    /// Ensures EF captures an object getter before applying the byte-array adapter.
    /// </summary>
    [Fact]
    public void Raw_reader_uses_object_to_allow_guid_values_during_buffering()
    {
        // Arrange
        var mapping = MySqlBinaryByteArrayTypeMapping.Default;

        // Act
        var method = mapping.GetDataReaderMethod();

        // Assert
        Assert.Equal(typeof(DbDataReader), method.DeclaringType);
        Assert.Equal(nameof(DbDataReader.GetValue), method.Name);
        Assert.Equal(typeof(object), method.ReturnType);
        Assert.Equal(
            typeof(int),
            Assert.Single(method.GetParameters())
                .ParameterType);
    }

    /// <summary>Reconstructs connector GUID values in their original wire byte order.</summary>
    [Theory]
    [MemberData(nameof(GuidCases))]
    public void Guid_reader_value_preserves_big_endian_bytes(
        Guid value,
        string expectedHex
    )
    {
        // Arrange
        var mapping = MySqlBinaryByteArrayTypeMapping.Default;

        // Act
        var actual = ReadProviderValue(mapping, value);

        // Assert
        Assert.Equal(expectedHex, Convert.ToHexString(actual));
    }

    /// <summary>Leaves ordinary binary provider values on the allocation-free passthrough path.</summary>
    [Theory]
    [MemberData(nameof(ByteArrayCases))]
    public void Byte_array_reader_value_is_returned_without_copying(
        byte[] value
    )
    {
        // Arrange
        var mapping = MySqlBinaryByteArrayTypeMapping.Default;

        // Act
        var actual = ReadProviderValue(mapping, value);

        // Assert
        Assert.Same(value, actual);
    }

    /// <summary>Gives each GUID adaptation independently owned mutable bytes.</summary>
    [Fact]
    public void Guid_adaptations_do_not_share_mutable_arrays()
    {
        // Arrange
        var mapping = MySqlBinaryByteArrayTypeMapping.Default;
        var value = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        // Act
        var (first, second) = (ReadProviderValue(mapping, value), ReadProviderValue(mapping, value));

        // Assert
        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

    /// <summary>Rejects unsupported values rather than coercing or exposing their payload.</summary>
    [Theory]
    [MemberData(nameof(UnsupportedValueCases))]
    public void Unsupported_reader_values_are_rejected_with_type_only_diagnostics(
        object value,
        string expectedType
    )
    {
        // Arrange
        var mapping = MySqlBinaryByteArrayTypeMapping.Default;

        // Act
        var exception = Record.Exception(() => ReadProviderValue(mapping, value));

        // Assert
        var invalidValue = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains(expectedType, invalidValue.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-binary-payload", invalidValue.Message, StringComparison.Ordinal);
    }

    /// <summary>Retains fail-closed behavior for an unexpected CLR null provider value.</summary>
    [Fact]
    public void Null_provider_value_is_not_treated_as_an_empty_byte_array()
    {
        // Arrange
        var mapping = MySqlBinaryByteArrayTypeMapping.Default;
        var adapter = Expression
            .Lambda<Func<byte[]>>(
                mapping.CustomizeDataReaderExpression(Expression.Constant(null, typeof(object))))
            .Compile();

        // Act
        var exception = Record.Exception(() => adapter());

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>Rejects missing expression metadata before constructing an invalid expression tree.</summary>
    [Fact]
    public void Null_reader_expression_is_rejected()
    {
        // Arrange
        var mapping = MySqlBinaryByteArrayTypeMapping.Default;

        // Act
        var exception = Record.Exception(() => mapping.CustomizeDataReaderExpression(null!));

        // Assert
        Assert.Equal(
            "expression",
            Assert.IsType<ArgumentNullException>(exception)
                .ParamName);
    }

    /// <summary>Accepts an already buffered object expression without requiring a reader method call.</summary>
    [Fact]
    public void Buffered_object_expression_uses_the_same_byte_order_adapter()
    {
        // Arrange
        var mapping = MySqlBinaryByteArrayTypeMapping.Default;
        var value = Expression.Parameter(typeof(object), "value");
        var adapter = Expression
            .Lambda<Func<object, byte[]>>(
                mapping.CustomizeDataReaderExpression(value),
                value)
            .Compile();

        // Act
        var actual = adapter(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));

        // Assert
        Assert.Equal("00112233445566778899AABBCCDDEEFF", Convert.ToHexString(actual));
    }

    /// <summary>Preserves fixed SQL facets and inherited byte-array behavior when EF clones the mapping.</summary>
    [Fact]
    public void Store_type_clone_retains_reader_and_byte_array_contracts()
    {
        // Arrange
        var mapping = MySqlBinaryByteArrayTypeMapping.Default;
        var baseline = new ByteArrayTypeMapping("binary(32)", DbType.Binary, 32);
        byte[] value = [0, 1, 127, 128, 255];

        // Act
        var clone = mapping.WithStoreTypeAndSize("binary(32)", 32);

        // Assert
        Assert.IsType<MySqlBinaryByteArrayTypeMapping>(clone);
        Assert.Equal("binary(32)", clone.StoreType);
        Assert.Equal(32, clone.Size);
        Assert.Equal(DbType.Binary, clone.DbType);
        Assert.Equal(typeof(byte[]), clone.ClrType);
        Assert.Null(clone.Converter);
        Assert.Equal(mapping.GetDataReaderMethod(), clone.GetDataReaderMethod());
        Assert.Equal(baseline.GenerateSqlLiteral(value), clone.GenerateSqlLiteral(value));
        Assert.Equal(
            baseline.Comparer.Equals(value, value.ToArray()),
            clone.Comparer.Equals(value, value.ToArray()));

        Assert.Equal(
            baseline.KeyComparer.Equals(value, value.ToArray()),
            clone.KeyComparer.Equals(value, value.ToArray()));

        Assert.Equal(value, Assert.IsType<byte[]>(clone.Comparer.Snapshot(value)));
        Assert.NotNull(clone.JsonValueReaderWriter);
        Assert.Equal(
            baseline.JsonValueReaderWriter!.ToJsonString(value),
            clone.JsonValueReaderWriter.ToJsonString(value));
    }

    /// <summary>
    /// Keeps a model converter's byte order separate from connector transport adaptation.
    /// </summary>
    [Fact]
    public void Composed_converter_receives_original_provider_bytes()
    {
        // Arrange
        var value = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var converter = new ValueConverter<Guid, byte[]>(
            guid => guid.ToByteArray(),
            bytes => new Guid(bytes));

        var mapping = (RelationalTypeMapping)MySqlBinaryByteArrayTypeMapping.Default
            .WithComposedConverter(converter);

        var providerBytes = value.ToByteArray();
        var connectorValue = new Guid(providerBytes, bigEndian: true);

        // Act
        var actual = mapping.Converter!.ConvertFromProvider(ReadProviderValue(mapping, connectorValue));

        // Assert
        Assert.IsType<MySqlBinaryByteArrayTypeMapping>(mapping);
        Assert.Equal(value, Assert.IsType<Guid>(actual));
        Assert.Equal(typeof(Guid), mapping.ClrType);
        Assert.Equal("binary(16)", mapping.StoreType);
        Assert.Equal(16, mapping.Size);
        Assert.Equal(DbType.Binary, mapping.DbType);
    }

    /// <summary>Executes the raw reader expression followed by the provider's adapter.</summary>
    private static byte[] ReadProviderValue(
        RelationalTypeMapping mapping,
        object value
    )
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(value);
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());

        var readerParameter = Expression.Parameter(typeof(DbDataReader), "reader");
        var rawRead = Expression.Call(readerParameter, mapping.GetDataReaderMethod(), Expression.Constant(0));
        var read = Expression
            .Lambda<Func<DbDataReader, byte[]>>(
                mapping.CustomizeDataReaderExpression(rawRead),
                readerParameter)
            .Compile();

        return read(reader);
    }
}
