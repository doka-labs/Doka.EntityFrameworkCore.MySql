namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Proves that every central spatial reader accepts legitimate boundary values
/// and rejects recursive inputs before NetTopologySuite can exhaust the stack.
/// </summary>
public sealed class MySqlSpatialInputGuardTests
{
    /// <summary>
    /// Pins the untyped reader boundary required for driver-specific runtime
    /// shapes and EF Core buffered split-query readers.
    /// </summary>
    [Fact]
    public void Geometry_mapping_reads_the_untyped_provider_value()
    {
        // Arrange
        var mapping = new MySqlNetTopologySuiteGeometryTypeMapping<Geometry>(
            new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<Geometry, MySqlGeometry>(
                geometry => MySqlGeometry.FromWkb(geometry.SRID, new WKBWriter().Write(geometry)),
                providerValue => ReadProviderGeometry(providerValue)),
            "geometry",
            jsonValueReaderWriter: null,
            useLongLatAxisOrder: false);

        // Act
        var readerMethod = mapping.GetDataReaderMethod();

        // Assert
        Assert.Equal(typeof(System.Data.Common.DbDataReader), readerMethod.DeclaringType);
        Assert.Equal(nameof(System.Data.Common.DbDataReader.GetValue), readerMethod.Name);
        Assert.Equal(typeof(object), readerMethod.ReturnType);
    }

    /// <summary>
    /// Proves every public geometry mapping materializes both driver shapes with
    /// the concrete CLR type and SRID intact.
    /// </summary>
    [Theory]
    [MemberData(nameof(GeometryFamilyCases))]
    public void ReadSpatialColumn_materializes_every_geometry_family_from_both_driver_shapes(
        Type mappingType,
        string wkt,
        bool nativeDriverShape
    )
    {
        // Arrange
        var expected = new WKTReader().Read(wkt);
        expected.SRID = 4326;
        var wkb = new WKBWriter().Write(expected);
        var sridPrefixedWkb = new byte[wkb.Length + sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(sridPrefixedWkb, 4326);
        wkb.CopyTo(sridPrefixedWkb, sizeof(int));
        var providerValue = nativeDriverShape
            ? (object)MySqlGeometry.FromWkb(4326, wkb)
            : sridPrefixedWkb;

        // Act
        var actual = InvokeReadSpatialColumn(mappingType, providerValue);

        // Assert
        Assert.IsAssignableFrom(mappingType, actual);
        Assert.Equal(4326, actual.SRID);
        Assert.True(expected.EqualsExact(actual));
    }

    /// <summary>Supplies concrete geometry families and both supported driver value shapes.</summary>
    public static IEnumerable<object[]> GeometryFamilyCases()
    {
        foreach (var (mappingType, wkt) in new (Type, string)[]
                 {
                     (typeof(Point), "POINT (1 2)"),
                     (typeof(LineString), "LINESTRING (0 0, 1 1)"),
                     (typeof(Polygon), "POLYGON ((0 0, 0 1, 1 1, 0 0))"),
                     (typeof(MultiPoint), "MULTIPOINT ((0 0), (1 1))"),
                     (typeof(MultiLineString), "MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))"),
                     (typeof(MultiPolygon), "MULTIPOLYGON (((0 0, 0 1, 1 1, 0 0)))"),
                     (typeof(GeometryCollection), "GEOMETRYCOLLECTION (POINT (0 0), LINESTRING (0 0, 1 1))"),
                     (typeof(Geometry), "POINT (3 4)"),
                 })
        {
            yield return [mappingType, wkt, false];
            yield return [mappingType, wkt, true];
        }
    }

    /// <summary>
    /// Requires a deterministic provider error when the wire geometry cannot be
    /// assigned to the model's concrete spatial CLR type.
    /// </summary>
    [Fact]
    public void ReadSpatialColumn_rejects_a_geometry_family_mismatch()
    {
        // Arrange
        var line = new WKTReader().Read("LINESTRING (0 0, 1 1)");
        var providerValue = MySqlGeometry.FromWkb(4326, new WKBWriter().Write(line));
        Action act = () =>
            InvokeReadSpatialColumn(typeof(Point), providerValue);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Contains("cannot be materialized as 'Point'", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rejects driver values outside the two explicitly supported wire shapes.
    /// </summary>
    [Fact]
    public void ReadSpatialColumn_rejects_an_unknown_driver_shape()
    {
        // Arrange
        Action act = () =>
            InvokeReadSpatialColumn(typeof(Geometry), "POINT (1 2)");

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Contains("System.String", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("POINT (1 2)", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Preserves every MySQL and MariaDB geometry family through both central
    /// readers while exercising point, sequence, polygon, and collection layouts.
    /// </summary>
    [Theory]
    [MemberData(nameof(SpatialReaderCases))]
    public void ReadWkt_and_wkb_accept_every_supported_geometry_family(
        string wkt,
        bool binaryReader
    )
    {
        // Arrange
        var expected = new WKTReader().Read(wkt);
        var wkb = new WKBWriter().Write(expected);

        // Act
        var actual = binaryReader
            ? MySqlSpatialValueReader.ReadWkb(wkb)
            : MySqlSpatialValueReader.ReadWkt(wkt);

        // Assert
        Assert.True(expected.EqualsExact(actual));
    }

    /// <summary>Supplies each geometry payload independently to the WKT and WKB reader boundaries.</summary>
    public static IEnumerable<object[]> SpatialReaderCases()
    {
        foreach (var wkt in new[]
                 {
                     "POINT (1 2)",
                     "LINESTRING (0 0, 1 1)",
                     "POLYGON ((0 0, 0 1, 1 1, 0 0))",
                     "MULTIPOINT ((0 0), (1 1))",
                     "MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))",
                     "MULTIPOLYGON (((0 0, 0 1, 1 1, 0 0)))",
                     "GEOMETRYCOLLECTION (POINT (0 0), LINESTRING (0 0, 1 1))",
                     "GEOMETRYCOLLECTION EMPTY"
                 })
        {
            yield return [wkt, false];
            yield return [wkt, true];
        }
    }

    /// <summary>
    /// Keeps the documented WKT boundary usable while exercising the same reader
    /// used by JSON geometry materialization.
    /// </summary>
    [Fact]
    public void ReadWkt_accepts_the_maximum_supported_nesting_depth()
    {
        // Arrange
        var wkt = CreateNestedWkt(MySqlSpatialInputGuard.MaximumNestingDepth - 1);

        // Act
        var geometry = MySqlSpatialValueReader.ReadWkt(wkt);

        // Assert
        Assert.Equal("GeometryCollection", geometry.GeometryType);
    }

    /// <summary>
    /// Reproduces the recursive WKT attack class at a deterministic safe depth
    /// and requires a bounded provider exception before parser entry.
    /// </summary>
    [Fact]
    public void ReadWkt_rejects_nesting_above_the_supported_limit()
    {
        // Arrange
        var wkt = CreateNestedWkt(MySqlSpatialInputGuard.MaximumNestingDepth);
        Action act = () => MySqlSpatialValueReader.ReadWkt(wkt);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
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
        // Arrange
        var wkt = CreateNestedWkt(depth: 50_000);
        Action act = () => MySqlSpatialValueReader.ReadWkt(wkt);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
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
        // Arrange
        var wkb = CreateNestedWkb(MySqlSpatialInputGuard.MaximumNestingDepth - 1, littleEndian);

        // Act
        var geometry = MySqlSpatialValueReader.ReadWkb(wkb);

        // Assert
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
        // Arrange
        var wkb = CreateNestedWkb(MySqlSpatialInputGuard.MaximumNestingDepth, littleEndian);
        Action act = () => MySqlSpatialValueReader.ReadWkb(wkb);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Equal(ExpectedLimitMessage(), exception.Message);
    }

    /// <summary>
    /// Re-runs the original 50,000-level WKB reproducer through the patched provider
    /// boundary and proves it becomes a bounded exception instead of process exit.
    /// </summary>
    [Fact]
    public void ReadWkb_rejects_the_original_stack_overflow_reproducer()
    {
        // Arrange
        var wkb = CreateNestedWkb(depth: 50_000, littleEndian: true);
        Action act = () => MySqlSpatialValueReader.ReadWkb(wkb);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
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
        // Arrange
        var wkb = CreateNestedWkb(depth: 50_000, littleEndian: true);
        object providerValue = useMySqlGeometry ? MySqlGeometry.FromWkb(0, wkb) : wkb;
        Action act = () =>
            InvokeReadSpatialColumn(typeof(Geometry), providerValue);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
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
        // Arrange
        var wkb = CreateNestedCollectionHeaders(
            MySqlSpatialInputGuard.MaximumNestingDepth,
            encodedCollectionType,
            includesSrid);

        Action act = () => MySqlSpatialInputGuard.ValidateWkb(wkb);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Equal(ExpectedLimitMessage(), exception.Message);
    }

    /// <summary>
    /// Leaves ordinary syntax failures with NetTopologySuite so the security guard
    /// does not reinterpret existing parser error semantics.
    /// </summary>
    [Fact]
    public void ReadWkt_preserves_existing_parse_errors_for_malformed_input()
    {
        // Arrange
        Action act = () => MySqlSpatialValueReader.ReadWkt("POINT (");

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ParseException>(recordedException);
    }

    /// <summary>
    /// Leaves ordinary WKB syntax failures with NetTopologySuite so the guard does
    /// not mask or reinterpret the established parser contract.
    /// </summary>
    [Fact]
    public void ReadWkb_preserves_existing_parse_errors_for_malformed_input()
    {
        // Arrange
        Action act = () => MySqlSpatialValueReader.ReadWkb([1]);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ParseException>(recordedException);
    }

    private static string CreateNestedWkt(
        int depth
    ) => string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION(", depth)) + "POINT (0 0)" + new string(')', depth);

    private static Geometry InvokeReadSpatialColumn(
        Type geometryType,
        object providerValue
    )
    {
        var table = new DataTable();
        _ = table.Columns.Add("Spatial", typeof(object));
        _ = table.Rows.Add(providerValue);

        using var reader = table.CreateDataReader();

        Assert.True(reader.Read());

        var mappingType = typeof(MySqlNetTopologySuiteGeometryTypeMapping<>).MakeGenericType(geometryType);
        var method = mappingType.GetMethod(
            "ReadSpatialColumn",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        try
        {
            return Assert.IsType<Geometry>(method.Invoke(null, [reader, 0]), exactMatch: false);
        }
        catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System
                .Runtime
                .ExceptionServices
                .ExceptionDispatchInfo
                .Capture(exception.InnerException)
                .Throw();
            throw;
        }
    }

    private static Geometry ReadProviderGeometry(
        MySqlGeometry providerValue
    )
    {
        var geometry = MySqlSpatialValueReader.ReadWkb(providerValue.WKB.ToArray());
        geometry.SRID = providerValue.SRID;

        return geometry;
    }

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
