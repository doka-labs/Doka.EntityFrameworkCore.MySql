namespace Doka.EntityFrameworkCore.MySql;

internal sealed class
    MySqlNetTopologySuiteGeometryTypeMapping<TGeometry> : RelationalGeometryTypeMapping<TGeometry, MySqlGeometry>
    where TGeometry : Geometry
{
    private static readonly MethodInfo s_convertFromProviderMethod =
        typeof(MySqlNetTopologySuiteGeometryTypeMapping<TGeometry>).GetMethod(
            nameof(ConvertFromProviderExpression),
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo s_readSpatialColumnMethod =
        typeof(MySqlNetTopologySuiteGeometryTypeMapping<TGeometry>).GetMethod(
            nameof(ReadSpatialColumn),
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo s_getValueMethod = typeof(System.Data.Common.DbDataReader).GetMethod(
        nameof(System.Data.Common.DbDataReader.GetValue),
        [typeof(int)])!;

    public MySqlNetTopologySuiteGeometryTypeMapping(
        ValueConverter<TGeometry, MySqlGeometry> converter,
        string storeType,
        JsonValueReaderWriter? jsonValueReaderWriter
    ) : base(converter, storeType, jsonValueReaderWriter) { }

    private MySqlNetTopologySuiteGeometryTypeMapping(
        RelationalTypeMappingParameters parameters,
        ValueConverter<TGeometry, MySqlGeometry> converter
    ) : base(parameters, converter) { }

    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlNetTopologySuiteGeometryTypeMapping<TGeometry>(parameters, SpatialConverter!);

    protected override Type WktReaderType => typeof(WKTReader);

    public override MethodInfo GetDataReaderMethod() => s_getValueMethod;

    protected override string AsText(
        object value
    )
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            Geometry geometry => geometry.AsText(),
            MySqlGeometry mySqlGeometry => ConvertFromProvider(mySqlGeometry)
                .AsText(),
            _ => throw new InvalidOperationException(
                $"Unsupported spatial literal value '{value.GetType().Name}' for '{typeof(TGeometry).Name}'."),
        };
    }

    protected override int GetSrid(
        object value
    )
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            Geometry geometry => geometry.SRID,
            MySqlGeometry mySqlGeometry => mySqlGeometry.SRID,
            _ => throw new InvalidOperationException(
                $"Unsupported spatial SRID source '{value.GetType().Name}' for '{typeof(TGeometry).Name}'."),
        };
    }

    protected override string GenerateNonNullSqlLiteral(
        object value
    )
    {
        var wkt = AsText(value).Replace("'", "''", StringComparison.Ordinal);
        var srid = GetSrid(value);

        return $"ST_GeomFromText('{wkt}', {srid.ToString(CultureInfo.InvariantCulture)})";
    }

    public override Expression CustomizeDataReaderExpression(
        Expression expression
    )
    {
        ArgumentNullException.ThrowIfNull(expression);

        // The spatial reader surfaces two runtime shapes: MySqlGeometry (the typical
        // MySQL path where MySqlConnector wraps the wire bytes) and raw byte[] (the
        // MariaDB path where the wrap does not happen). EF Core's default expression
        // calls GetFieldValue<T>(ordinal) with the provider type fixed at translation
        // time; on MariaDB that throws an InvalidCastException because byte[] cannot
        // be cast to MySqlGeometry or to the concrete geometry CLR type. We
        // pattern-match the standard GetFieldValue shape, extract the reader plus
        // ordinal, and route through ReadSpatialColumn which inspects the actual
        // runtime value and dispatches accordingly. When the input expression does
        // not match the recognized shape we fall back to the legacy paths so custom
        // pipelines that pre-process the reader value still work.
        if (TryExtractReaderAndOrdinal(expression, out var reader, out var ordinal))
        {
            return Expression.Call(s_readSpatialColumnMethod, reader, ordinal);
        }

        if (expression.Type == typeof(TGeometry))
        {
            return expression;
        }

        if (typeof(TGeometry).IsAssignableFrom(expression.Type))
        {
            return Expression.Convert(expression, typeof(TGeometry));
        }

        if (expression.Type == typeof(MySqlGeometry))
        {
            return Expression.Call(s_convertFromProviderMethod, expression);
        }

        return expression;
    }

    /// <summary>
    /// Pattern-matches the standard EF Core reader expression
    /// <c>reader.GetFieldValue&lt;MySqlGeometry&gt;(ordinal)</c> and returns the
    /// underlying reader + ordinal expressions so they can be re-bound to a
    /// custom dispatching call. Returns false when the input expression deviates
    /// from the recognized shape; the caller then falls back to the standard
    /// conversion path.
    /// </summary>
    private static bool TryExtractReaderAndOrdinal(
        Expression expression,
        out Expression reader,
        out Expression ordinal
    )
    {
        if (expression is MethodCallExpression
            {
                Method.Name: "GetFieldValue" or "GetValue",
                Object: { } readerInstance,
                Arguments: [{ } ordinalArg],
            })
        {
            reader = readerInstance;
            ordinal = ordinalArg;
            return true;
        }

        reader = null!;
        ordinal = null!;
        return false;
    }

    /// <summary>
    /// Reads a spatial column from the data reader and converts whatever shape
    /// the driver returned into the typed geometry. Two shapes are recognized:
    /// <see cref="MySqlGeometry"/> (the typical MySQL path) and raw <c>byte[]</c>
    /// (the MariaDB path where MySqlConnector does not wrap the value). MariaDB
    /// spatial column bytes either start with the byte-order indicator (canonical
    /// OGC WKB) or with a 4-byte little-endian SRID prefix followed by the
    /// byte-order indicator (MySQL-style); both layouts are accepted, and the
    /// extracted SRID lands on the materialized geometry.
    /// </summary>
    private static TGeometry ReadSpatialColumn(
        System.Data.Common.DbDataReader reader,
        int ordinal
    )
    {
        ArgumentNullException.ThrowIfNull(reader);

        var value = reader.GetValue(ordinal);

        return value switch
        {
            MySqlGeometry mySqlGeometry => ConvertFromProvider(mySqlGeometry),
            byte[] wkbBytes => ConvertFromWkbBytes(wkbBytes),
            _ => throw new InvalidOperationException(
                $"The data reader returned an unsupported spatial value type '{value?.GetType().FullName ?? "<null>"}' for '{typeof(TGeometry).Name}'."),
        };
    }

    private static TGeometry ConvertFromWkbBytes(
        byte[] wkbBytes
    )
    {
        if (wkbBytes.Length == 0)
        {
            throw new InvalidOperationException(
                $"Empty byte stream cannot be materialized as '{typeof(TGeometry).Name}'.");
        }

        if (TryReadExactWkb(wkbBytes, out var geometry))
        {
            return CastGeometry(geometry, srid: 0);
        }

        if (wkbBytes.Length > 4
            && TryReadExactWkb(wkbBytes.AsSpan(4), out geometry))
        {
            var srid = BinaryPrimitives.ReadInt32LittleEndian(wkbBytes.AsSpan(0, 4));
            return CastGeometry(geometry, srid);
        }

        throw new InvalidOperationException(
            $"Unrecognized WKB byte layout for '{typeof(TGeometry).Name}'. "
            + "Expected canonical OGC WKB or MySQL-style SRID-prefixed WKB.");
    }

    private static bool TryReadExactWkb(
        ReadOnlySpan<byte> candidate,
        out Geometry geometry
    )
    {
        geometry = null!;

        if (candidate.Length == 0
            || candidate[0] is not (0 or 1))
        {
            return false;
        }

        try
        {
            geometry = MySqlSpatialValueReader.ReadWkb(candidate);

            // A zero SRID prefix also starts with a valid WKB byte-order marker.
            // Re-serializing detects that ambiguous layout because the first,
            // incorrectly parsed geometry consumes only part of the byte stream.
            return new WKBWriter().Write(geometry).Length == candidate.Length;
        }
        catch (ParseException)
        {
            return false;
        }
    }

    private static TGeometry CastGeometry(
        Geometry geometry,
        int srid
    )
    {
        geometry.SRID = srid;

        return geometry as TGeometry
            ?? throw new InvalidOperationException(
                $"The data reader returned a geometry of type '{geometry.GetType().Name}' which cannot be materialized as '{typeof(TGeometry).Name}'.");
    }

    private static TGeometry ConvertFromProvider(
        MySqlGeometry providerValue
    )
    {
        var geometry = MySqlSpatialValueReader.ReadWkb(providerValue.WKB.ToArray());
        geometry.SRID = providerValue.SRID;

        return geometry as TGeometry
            ?? throw new InvalidOperationException(
                $"The provider returned a geometry of type '{geometry.GetType().Name}' which cannot be materialized as '{typeof(TGeometry).Name}'.");
    }

    private static TGeometry ConvertFromProviderExpression(
        MySqlGeometry providerValue
    ) => ConvertFromProvider(providerValue);
}
