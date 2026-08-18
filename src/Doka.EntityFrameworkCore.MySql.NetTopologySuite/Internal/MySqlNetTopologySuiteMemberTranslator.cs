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
        [(typeof(Geometry), nameof(Geometry.Area))] = "ST_Area",
        [(typeof(Geometry), nameof(Geometry.Length))] = "ST_Length",
        [(typeof(Geometry), nameof(Geometry.IsEmpty))] = "ST_IsEmpty",
        [(typeof(Geometry), nameof(Geometry.IsSimple))] = "ST_IsSimple",
        [(typeof(Geometry), nameof(Geometry.IsValid))] = "ST_IsValid",
        [(typeof(Geometry), nameof(Geometry.NumPoints))] = "ST_NumPoints",
        [(typeof(Point), "X")] = "ST_X",
        [(typeof(Point), "Y")] = "ST_Y",
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
    private readonly bool _supportsMariaDbSpatialFunctions;
    private readonly bool _supportsIsValid;

    public MySqlNetTopologySuiteMemberTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource,
        ILogger logger,
        bool supportsMariaDbSpatialFunctions,
        bool supportsIsValid
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory ?? throw new ArgumentNullException(nameof(sqlExpressionFactory));
        _typeMappingSource = typeMappingSource ?? throw new ArgumentNullException(nameof(typeMappingSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _supportsMariaDbSpatialFunctions = supportsMariaDbSpatialFunctions;
        _supportsIsValid = supportsIsValid;
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

        if (member.Name == nameof(Geometry.GeometryType)
            && typeof(Geometry).IsAssignableFrom(member.DeclaringType))
        {
            return TranslateGeometryType(instance);
        }

        if (TryGetScalarFunction(member, out var scalarFunctionName, out var typeMapping))
        {
            if (scalarFunctionName == "ST_IsValid" && !_supportsIsValid)
            {
                MySqlLoggerMessages.MissingSpatialTranslation(
                    _logger,
                    "Geometry.IsValid requires MySQL 5.7.6 or MariaDB 12.0 or newer");

                return null;
            }

            if (_supportsMariaDbSpatialFunctions && scalarFunctionName == "ST_NumInteriorRing")
            {
                scalarFunctionName = "ST_NumInteriorRings";
            }

            var translated = TranslateFunction(scalarFunctionName, instance, returnType, typeMapping);

            if (_supportsMariaDbSpatialFunctions && scalarFunctionName == "ST_IsSimple")
            {
                // MariaDB 11.x returns -1 for ST_IsSimple(NULL), although its
                // documentation specifies NULL. Preserve the NTS nullable contract.
                return _sqlExpressionFactory.Case(
                    [
                        new CaseWhenClause(
                            _sqlExpressionFactory.IsNull(instance),
                            _sqlExpressionFactory.Constant(null, typeof(bool), s_boolMapping)),
                    ],
                    translated);
            }

            return translated;
        }

        if (TryGetGeometryFunction(member, out var geometryFunctionName))
        {
            if (!_supportsMariaDbSpatialFunctions
                && geometryFunctionName is "ST_Boundary" or "ST_PointOnSurface")
            {
                MySqlLoggerMessages.MissingSpatialTranslation(_logger, $"{member.DeclaringType.Name}.{member.Name}");
                return null;
            }

            var geometryTypeMapping = _typeMappingSource.FindMapping(returnType) ?? instance.TypeMapping;

            return TranslateFunction(geometryFunctionName, instance, returnType, geometryTypeMapping);
        }

        if (typeof(Geometry).IsAssignableFrom(member.DeclaringType))
        {
            MySqlLoggerMessages.MissingSpatialTranslation(_logger, $"{member.DeclaringType.Name}.{member.Name}");
        }

        return null;
    }

    private SqlExpression TranslateGeometryType(
        SqlExpression instance
    )
    {
        var databaseType = TranslateFunction(
            "ST_GeometryType",
            instance,
            typeof(string),
            s_stringMapping);
        var typeNames = new[]
        {
            "Point",
            "LineString",
            "Polygon",
            "MultiPoint",
            "MultiLineString",
            "MultiPolygon",
            "GeometryCollection",
        };
        var clauses = typeNames
            .Select(typeName => new CaseWhenClause(
                _sqlExpressionFactory.Equal(
                    databaseType,
                    _sqlExpressionFactory.Constant(typeName.ToUpperInvariant(), s_stringMapping)),
                _sqlExpressionFactory.Constant(typeName, s_stringMapping)))
            .ToArray();

        return _sqlExpressionFactory.Case(clauses, databaseType);
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
