namespace Doka.EntityFrameworkCore.MySql;

internal sealed class
    MySqlNetTopologySuiteGeometryTypeMapping<TGeometry> : RelationalGeometryTypeMapping<TGeometry, MySqlGeometry>
    where TGeometry : Geometry
{
    private static readonly MethodInfo s_convertFromProviderMethod =
        typeof(MySqlNetTopologySuiteGeometryTypeMapping<TGeometry>).GetMethod(
            nameof(ConvertFromProviderExpression),
            BindingFlags.NonPublic | BindingFlags.Static)!;

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

    public override Expression CustomizeDataReaderExpression(
        Expression expression
    )
    {
        ArgumentNullException.ThrowIfNull(expression);

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

    private static TGeometry ConvertFromProvider(
        MySqlGeometry providerValue
    )
    {
        var geometry = new WKBReader().Read(providerValue.WKB.ToArray());
        geometry.SRID = providerValue.SRID;

        return geometry as TGeometry
            ?? throw new InvalidOperationException(
                $"The provider returned a geometry of type '{geometry.GetType().Name}' which cannot be materialized as '{typeof(TGeometry).Name}'.");
    }

    private static TGeometry ConvertFromProviderExpression(
        MySqlGeometry providerValue
    ) => ConvertFromProvider(providerValue);
}
