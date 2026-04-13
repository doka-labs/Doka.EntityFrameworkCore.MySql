namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlNetTopologySuiteMemberTranslator : IMemberTranslator
{
    private static readonly BoolTypeMapping s_boolMapping = new("tinyint(1)", DbType.Boolean);
    private static readonly DoubleTypeMapping s_doubleMapping = new("double", DbType.Double);
    private static readonly IntTypeMapping s_intMapping = new("int", DbType.Int32);
    private static readonly StringTypeMapping s_stringMapping = new("longtext", DbType.String, unicode: true);

    private static readonly Dictionary<(Type DeclaringType, string MemberName), string> s_scalarFunctions = new()
    {
        [(typeof(Geometry), nameof(Geometry.SRID))] = "ST_SRID",
        [(typeof(Geometry), nameof(Geometry.GeometryType))] = "ST_GeometryType",
        [(typeof(Geometry), nameof(Geometry.Area))] = "ST_Area",
        [(typeof(Geometry), nameof(Geometry.Length))] = "ST_Length",
        [(typeof(Geometry), nameof(Geometry.IsEmpty))] = "ST_IsEmpty",
        [(typeof(Geometry), nameof(Geometry.IsSimple))] = "ST_IsSimple",
        [(typeof(Geometry), nameof(Geometry.IsValid))] = "ST_IsValid",
        [(typeof(Geometry), nameof(Geometry.NumPoints))] = "ST_NumPoints",
        [(typeof(Point), "X")] = "ST_X",
        [(typeof(Point), "Y")] = "ST_Y",
        [(typeof(Point), "Z")] = "ST_Z",
        [(typeof(Point), "M")] = "ST_M",
        [(typeof(Polygon), nameof(Polygon.NumInteriorRings))] = "ST_NumInteriorRing",
    };

    private static readonly Dictionary<(Type DeclaringType, string MemberName), string> s_geometryFunctions = new()
    {
        [(typeof(Geometry), nameof(Geometry.Envelope))] = "ST_Envelope",
        [(typeof(Geometry), nameof(Geometry.Boundary))] = "ST_Boundary",
        [(typeof(Geometry), nameof(Geometry.Centroid))] = "ST_Centroid",
        [(typeof(Geometry), nameof(Geometry.ConvexHull))] = "ST_ConvexHull",
        [(typeof(Geometry), nameof(Geometry.PointOnSurface))] = "ST_PointOnSurface",
        [(typeof(Geometry), nameof(Geometry.InteriorPoint))] = "ST_PointOnSurface",
        [(typeof(LineString), nameof(LineString.StartPoint))] = "ST_StartPoint",
        [(typeof(LineString), nameof(LineString.EndPoint))] = "ST_EndPoint",
        [(typeof(Polygon), nameof(Polygon.ExteriorRing))] = "ST_ExteriorRing",
    };

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ILogger _logger;

    public MySqlNetTopologySuiteMemberTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource,
        ILogger logger
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory ?? throw new ArgumentNullException(nameof(sqlExpressionFactory));
        _typeMappingSource = typeMappingSource ?? throw new ArgumentNullException(nameof(typeMappingSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(logger);

        if (instance is null
            || member.DeclaringType is null)
        {
            return null;
        }

        if (TryGetScalarFunction(member, out var scalarFunctionName, out var typeMapping))
        {
            return TranslateFunction(scalarFunctionName, instance, returnType, typeMapping);
        }

        if (TryGetGeometryFunction(member, out var geometryFunctionName))
        {
            var geometryTypeMapping = _typeMappingSource.FindMapping(returnType) as RelationalTypeMapping
                ?? instance.TypeMapping as RelationalTypeMapping;

            return TranslateFunction(geometryFunctionName, instance, returnType, geometryTypeMapping);
        }

        if (typeof(Geometry).IsAssignableFrom(member.DeclaringType))
        {
            MySqlLoggerMessages.MissingSpatialTranslation(_logger, $"{member.DeclaringType.Name}.{member.Name}");
        }

        return null;
    }

    private SqlExpression TranslateFunction(
        string functionName,
        SqlExpression instance,
        Type returnType,
        RelationalTypeMapping? typeMapping
    ) => _sqlExpressionFactory.Function(
        functionName,
        [instance],
        nullable: true,
        argumentsPropagateNullability: [true],
        returnType,
        typeMapping);

    private static bool TryGetScalarFunction(
        MemberInfo member,
        out string functionName,
        out RelationalTypeMapping typeMapping
    )
    {
        foreach (var candidate in s_scalarFunctions)
        {
            if (candidate.Key.MemberName == member.Name
                && candidate.Key.DeclaringType.IsAssignableFrom(member.DeclaringType!))
            {
                functionName = candidate.Value;
                typeMapping = GetScalarTypeMapping(member);
                return true;
            }
        }

        functionName = string.Empty;
        typeMapping = s_intMapping;
        return false;
    }

    private static bool TryGetGeometryFunction(
        MemberInfo member,
        out string functionName
    )
    {
        foreach (var candidate in s_geometryFunctions)
        {
            if (candidate.Key.MemberName == member.Name
                && candidate.Key.DeclaringType.IsAssignableFrom(member.DeclaringType!))
            {
                functionName = candidate.Value;
                return true;
            }
        }

        functionName = string.Empty;
        return false;
    }

    private static RelationalTypeMapping GetScalarTypeMapping(
        MemberInfo member
    ) => member.Name switch
    {
        nameof(Geometry.GeometryType) => s_stringMapping,
        nameof(Geometry.SRID) or nameof(Geometry.NumPoints) or nameof(Polygon.NumInteriorRings) => s_intMapping,
        nameof(Geometry.IsEmpty) or nameof(Geometry.IsSimple) or nameof(Geometry.IsValid) => s_boolMapping,
        _ => s_doubleMapping
    };
}
