namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Proves that every central spatial reader accepts legitimate boundary values
/// and rejects recursive inputs before NetTopologySuite can exhaust the stack.
/// </summary>
public sealed class MySqlSpatialInputGuardTests
{
    /// <summary>
    /// Preserves every MySQL and MariaDB geometry family through both central
    /// readers while exercising point, sequence, polygon, and collection layouts.
    /// </summary>
    [Theory]
    [InlineData("POINT (1 2)")]
    [InlineData("LINESTRING (0 0, 1 1)")]
    [InlineData("POLYGON ((0 0, 0 1, 1 1, 0 0))")]
    [InlineData("MULTIPOINT ((0 0), (1 1))")]
    [InlineData("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))")]
    [InlineData("MULTIPOLYGON (((0 0, 0 1, 1 1, 0 0)))")]
    [InlineData("GEOMETRYCOLLECTION (POINT (0 0), LINESTRING (0 0, 1 1))")]
    [InlineData("GEOMETRYCOLLECTION EMPTY")]
    public void ReadWkt_and_wkb_accept_every_supported_geometry_family(
        string wkt
    )
    {
        var expected = new WKTReader().Read(wkt);
        var wkb = new WKBWriter().Write(expected);

        var fromWkt = MySqlSpatialValueReader.ReadWkt(wkt);
        var fromWkb = MySqlSpatialValueReader.ReadWkb(wkb);

        Assert.True(expected.EqualsExact(fromWkt));
        Assert.True(expected.EqualsExact(fromWkb));
    }

    /// <summary>
    /// Keeps the documented WKT boundary usable while exercising the same reader
    /// used by JSON geometry materialization.
    /// </summary>
    [Fact]
    public void ReadWkt_accepts_the_maximum_supported_nesting_depth()
    {
        var wkt = CreateNestedWkt(MySqlSpatialInputGuard.MaximumNestingDepth - 1);

        var geometry = MySqlSpatialValueReader.ReadWkt(wkt);

        Assert.Equal("GeometryCollection", geometry.GeometryType);
    }

    /// <summary>
    /// Reproduces the recursive WKT attack class at a deterministic safe depth
    /// and requires a bounded provider exception before parser entry.
    /// </summary>
    [Fact]
    public void ReadWkt_rejects_nesting_above_the_supported_limit()
    {
        var wkt = CreateNestedWkt(MySqlSpatialInputGuard.MaximumNestingDepth);

        var exception = Assert.Throws<InvalidOperationException>(() => MySqlSpatialValueReader.ReadWkt(wkt));

        Assert.Equal(ExpectedLimitMessage(), exception.Message);
        Assert.DoesNotContain(wkt, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Re-runs the original 50,000-level WKT reproducer through the patched provider
    /// boundary and proves it becomes a bounded exception instead of process exit.
    /// </summary>
    [Fact]
    public void ReadWkt_rejects_the_original_stack_overflow_reproducer()
    {
        var wkt = CreateNestedWkt(depth: 50_000);

        var exception = Assert.Throws<InvalidOperationException>(() => MySqlSpatialValueReader.ReadWkt(wkt));

        Assert.Equal(ExpectedLimitMessage(), exception.Message);
    }

    /// <summary>
    /// Covers both byte orders at the accepted WKB boundary so the iterative guard
    /// cannot accidentally narrow valid MySQL or MariaDB materialization.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadWkb_accepts_the_maximum_supported_nesting_depth(
        bool littleEndian
    )
    {
        var wkb = CreateNestedWkb(MySqlSpatialInputGuard.MaximumNestingDepth - 1, littleEndian);

        var geometry = MySqlSpatialValueReader.ReadWkb(wkb);

        Assert.Equal("GeometryCollection", geometry.GeometryType);
    }

    /// <summary>
    /// Reproduces the recursive WKB attack class in both byte orders and requires
    /// rejection before the recursive reader receives the value.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadWkb_rejects_nesting_above_the_supported_limit(
        bool littleEndian
    )
    {
        var wkb = CreateNestedWkb(MySqlSpatialInputGuard.MaximumNestingDepth, littleEndian);

        var exception = Assert.Throws<InvalidOperationException>(() => MySqlSpatialValueReader.ReadWkb(wkb));

        Assert.Equal(ExpectedLimitMessage(), exception.Message);
    }

    /// <summary>
    /// Re-runs the original 50,000-level WKB reproducer through the patched provider
    /// boundary and proves it becomes a bounded exception instead of process exit.
    /// </summary>
    [Fact]
    public void ReadWkb_rejects_the_original_stack_overflow_reproducer()
    {
        var wkb = CreateNestedWkb(depth: 50_000, littleEndian: true);

        var exception = Assert.Throws<InvalidOperationException>(() => MySqlSpatialValueReader.ReadWkb(wkb));

        Assert.Equal(ExpectedLimitMessage(), exception.Message);
    }

    /// <summary>
    /// Proves both concrete data-reader representations cross the guard before the
    /// private materialization dispatcher reaches NetTopologySuite.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadSpatialColumn_rejects_the_original_stack_overflow_reproducer(
        bool useMySqlGeometry
    )
    {
        var wkb = CreateNestedWkb(depth: 50_000, littleEndian: true);
        object providerValue = useMySqlGeometry ? MySqlGeometry.FromWkb(0, wkb) : wkb;
        var table = new DataTable();
        _ = table.Columns.Add("Spatial", typeof(object));
        _ = table.Rows.Add(providerValue);

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());

        var method = typeof(MySqlNetTopologySuiteGeometryTypeMapping<Geometry>).GetMethod(
            "ReadSpatialColumn",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var invocationException =
            Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(
                null,
                [
                    reader,
                    0
                ]));
        var exception = Assert.IsType<InvalidOperationException>(invocationException.InnerException);

        Assert.Equal(ExpectedLimitMessage(), exception.Message);
    }

    /// <summary>
    /// Exercises alternate ISO and EWKB type encodings so an attacker cannot bypass
    /// the depth contract by changing only the collection header representation.
    /// </summary>
    [Theory]
    [InlineData(1007u, false)]
    [InlineData(2007u, false)]
    [InlineData(3007u, false)]
    [InlineData(0xa0000007u, true)]
    public void ValidateWkb_rejects_excessive_nesting_in_alternate_type_encodings(
        uint encodedCollectionType,
        bool includesSrid
    )
    {
        var wkb = CreateNestedCollectionHeaders(
            MySqlSpatialInputGuard.MaximumNestingDepth,
            encodedCollectionType,
            includesSrid);

        var exception = Assert.Throws<InvalidOperationException>(() => MySqlSpatialInputGuard.ValidateWkb(wkb));

        Assert.Equal(ExpectedLimitMessage(), exception.Message);
    }

    /// <summary>
    /// Leaves ordinary syntax failures with NetTopologySuite so the security guard
    /// does not reinterpret existing parser error semantics.
    /// </summary>
    [Fact]
    public void ReadWkt_preserves_existing_parse_errors_for_malformed_input()
    {
        Assert.Throws<ParseException>(() => MySqlSpatialValueReader.ReadWkt("POINT ("));
    }

    /// <summary>
    /// Leaves ordinary WKB syntax failures with NetTopologySuite so the guard does
    /// not mask or reinterpret the established parser contract.
    /// </summary>
    [Fact]
    public void ReadWkb_preserves_existing_parse_errors_for_malformed_input()
    {
        Assert.Throws<ParseException>(() => MySqlSpatialValueReader.ReadWkb([1]));
    }

    private static string CreateNestedWkt(
        int depth
    ) => string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION(", depth)) + "POINT (0 0)" + new string(')', depth);

    private static byte[] CreateNestedWkb(
        int depth,
        bool littleEndian
    )
    {
        var wkb = new byte[(9 * depth) + 21];
        var offset = 0;

        for (var collectionIndex = 0; collectionIndex < depth; collectionIndex++)
        {
            WriteHeader(wkb, ref offset, geometryType: 7, littleEndian);
            WriteUInt32(wkb, ref offset, value: 1, littleEndian);
        }

        WriteHeader(wkb, ref offset, geometryType: 1, littleEndian);
        offset += 2 * sizeof(double);

        Assert.Equal(wkb.Length, offset);
        return wkb;
    }

    private static byte[] CreateNestedCollectionHeaders(
        int depth,
        uint encodedCollectionType,
        bool includesSrid
    )
    {
        var headerLength = sizeof(byte) + sizeof(uint) + (includesSrid ? sizeof(uint) : 0) + sizeof(uint);
        var wkb = new byte[headerLength * depth];
        var offset = 0;

        for (var collectionIndex = 0; collectionIndex < depth; collectionIndex++)
        {
            WriteHeader(wkb, ref offset, encodedCollectionType, littleEndian: true);

            if (includesSrid)
            {
                WriteUInt32(wkb, ref offset, value: 4326, littleEndian: true);
            }

            WriteUInt32(wkb, ref offset, value: 1, littleEndian: true);
        }

        Assert.Equal(wkb.Length, offset);
        return wkb;
    }

    private static void WriteHeader(
        Span<byte> destination,
        ref int offset,
        uint geometryType,
        bool littleEndian
    )
    {
        destination[offset++] = littleEndian ? (byte)1 : (byte)0;
        WriteUInt32(destination, ref offset, geometryType, littleEndian);
    }

    private static void WriteUInt32(
        Span<byte> destination,
        ref int offset,
        uint value,
        bool littleEndian
    )
    {
        var valueBytes = destination.Slice(offset, sizeof(uint));

        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(valueBytes, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(valueBytes, value);
        }

        offset += sizeof(uint);
    }

    private static string ExpectedLimitMessage() =>
        $"The spatial value exceeds the provider nesting limit of {MySqlSpatialInputGuard.MaximumNestingDepth}.";
}
