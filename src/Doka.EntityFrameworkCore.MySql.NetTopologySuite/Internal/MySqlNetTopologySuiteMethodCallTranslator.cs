namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlNetTopologySuiteMethodCallTranslator : IMethodCallTranslator
{
    private static readonly BoolTypeMapping s_boolMapping = new("tinyint(1)", DbType.Boolean);
    private static readonly DoubleTypeMapping s_doubleMapping = new("double", DbType.Double);
    private static readonly IntTypeMapping s_intMapping = new("int", DbType.Int32);
    private static readonly StringTypeMapping s_stringMapping = new("longtext", DbType.String, unicode: true);
    private static readonly ByteArrayTypeMapping s_binaryMapping = new("longblob", DbType.Binary);

    private static readonly MethodInfo s_asTextMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.AsText), Type.EmptyTypes)!;

    private static readonly MethodInfo s_asBinaryMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.AsBinary), Type.EmptyTypes)!;

    private static readonly MethodInfo s_containsMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Contains), [typeof(Geometry)])!;

    private static readonly MethodInfo s_withinMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Within), [typeof(Geometry)])!;

    private static readonly MethodInfo s_intersectsMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Intersects), [typeof(Geometry)])!;

    private static readonly MethodInfo s_disjointMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Disjoint), [typeof(Geometry)])!;

    private static readonly MethodInfo s_overlapsMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Overlaps), [typeof(Geometry)])!;

    private static readonly MethodInfo s_touchesMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Touches), [typeof(Geometry)])!;

    private static readonly MethodInfo s_crossesMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Crosses), [typeof(Geometry)])!;

    private static readonly MethodInfo s_equalsTopologicallyMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.EqualsTopologically), [typeof(Geometry)])!;

    private static readonly MethodInfo s_distanceMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Distance), [typeof(Geometry)])!;

    private static readonly MethodInfo s_bufferMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Buffer), [typeof(double)])!;

    private static readonly MethodInfo s_intersectionMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Intersection), [typeof(Geometry)])!;

    private static readonly MethodInfo s_unionMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Union), [typeof(Geometry)])!;

    private static readonly MethodInfo s_differenceMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Difference), [typeof(Geometry)])!;

    private static readonly MethodInfo s_symmetricDifferenceMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.SymmetricDifference), [typeof(Geometry)])!;

    private static readonly MethodInfo s_relatePatternMethod =
        typeof(Geometry).GetRuntimeMethod(
            nameof(Geometry.Relate),
            [
                typeof(Geometry),
                typeof(string),
            ])!;

    private static readonly MethodInfo s_getGeometryNMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.GetGeometryN), [typeof(int)])!;

    private static readonly MethodInfo s_getPointNMethod =
        typeof(LineString).GetRuntimeMethod(nameof(LineString.GetPointN), [typeof(int)])!;

    private static readonly MethodInfo s_distanceSphereMethod =
        typeof(MySqlNetTopologySuiteDbFunctionsExtensions).GetRuntimeMethod(
            nameof(MySqlNetTopologySuiteDbFunctionsExtensions.DistanceSphere),
            [
                typeof(DbFunctions),
                typeof(Point),
                typeof(Point),
            ])!;

    private static readonly MethodInfo s_mbrContainsMethod =
        typeof(MySqlNetTopologySuiteDbFunctionsExtensions).GetRuntimeMethod(
            nameof(MySqlNetTopologySuiteDbFunctionsExtensions.MbrContains),
            [
                typeof(DbFunctions),
                typeof(Geometry),
                typeof(Geometry),
            ])!;

    private static readonly MethodInfo s_mbrWithinMethod =
        typeof(MySqlNetTopologySuiteDbFunctionsExtensions).GetRuntimeMethod(
            nameof(MySqlNetTopologySuiteDbFunctionsExtensions.MbrWithin),
            [
                typeof(DbFunctions),
                typeof(Geometry),
                typeof(Geometry),
            ])!;

    private static readonly MethodInfo s_mbrIntersectsMethod =
        typeof(MySqlNetTopologySuiteDbFunctionsExtensions).GetRuntimeMethod(
            nameof(MySqlNetTopologySuiteDbFunctionsExtensions.MbrIntersects),
            [
                typeof(DbFunctions),
                typeof(Geometry),
                typeof(Geometry),
            ])!;

    private static readonly MethodInfo s_mbrOverlapsMethod =
        typeof(MySqlNetTopologySuiteDbFunctionsExtensions).GetRuntimeMethod(
            nameof(MySqlNetTopologySuiteDbFunctionsExtensions.MbrOverlaps),
            [
                typeof(DbFunctions),
                typeof(Geometry),
                typeof(Geometry),
            ])!;

    private static readonly MethodInfo s_mbrDisjointMethod =
        typeof(MySqlNetTopologySuiteDbFunctionsExtensions).GetRuntimeMethod(
            nameof(MySqlNetTopologySuiteDbFunctionsExtensions.MbrDisjoint),
            [
                typeof(DbFunctions),
                typeof(Geometry),
                typeof(Geometry),
            ])!;

    private static readonly Dictionary<MethodInfo, string> s_booleanInstanceFunctions = new()
    {
        [s_containsMethod] = "ST_Contains",
        [s_withinMethod] = "ST_Within",
        [s_intersectsMethod] = "ST_Intersects",
        [s_disjointMethod] = "ST_Disjoint",
        [s_overlapsMethod] = "ST_Overlaps",
        [s_touchesMethod] = "ST_Touches",
        [s_crossesMethod] = "ST_Crosses",
        [s_equalsTopologicallyMethod] = "ST_Equals",
    };

    private static readonly Dictionary<MethodInfo, string> s_geometryInstanceFunctions = new()
    {
        [s_bufferMethod] = "ST_Buffer",
        [s_intersectionMethod] = "ST_Intersection",
        [s_unionMethod] = "ST_Union",
        [s_differenceMethod] = "ST_Difference",
        [s_symmetricDifferenceMethod] = "ST_SymDifference",
        [s_getGeometryNMethod] = "ST_GeometryN",
        [s_getPointNMethod] = "ST_PointN",
    };

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ILogger _logger;

    public MySqlNetTopologySuiteMethodCallTranslator(
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
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(logger);

        if (method == s_asTextMethod
            && instance is not null)
        {
            return TranslateFunction("ST_AsText", typeof(string), s_stringMapping, [instance]);
        }

        if (method == s_asBinaryMethod
            && instance is not null)
        {
            return TranslateFunction("ST_AsBinary", typeof(byte[]), s_binaryMapping, [instance]);
        }

        if (method == s_distanceMethod
            && instance is not null)
        {
            return TranslateFunction(
                "ST_Distance",
                typeof(double),
                s_doubleMapping,
                [
                    instance,
                    arguments[0],
                ]);
        }

        if (method == s_relatePatternMethod
            && instance is not null)
        {
            return TranslateFunction(
                "ST_Relate",
                typeof(bool),
                s_boolMapping,
                [
                    instance,
                    arguments[0],
                    arguments[1],
                ]);
        }

        if (s_booleanInstanceFunctions.TryGetValue(method, out var booleanFunctionName)
            && instance is not null)
        {
            return TranslateFunction(
                booleanFunctionName,
                typeof(bool),
                s_boolMapping,
                [
                    instance,
                    arguments[0],
                ]);
        }

        if (s_geometryInstanceFunctions.TryGetValue(method, out var geometryFunctionName)
            && instance is not null)
        {
            return TranslateGeometryFunction(
                geometryFunctionName,
                method.ReturnType,
                [
                    instance,
                    .. arguments,
                ]);
        }

        if (method == s_distanceSphereMethod)
        {
            return TranslateFunction(
                "ST_Distance_Sphere",
                typeof(double),
                s_doubleMapping,
                [
                    arguments[1],
                    arguments[2],
                ]);
        }

        if (method == s_mbrContainsMethod)
        {
            return TranslateFunction(
                "MBRContains",
                typeof(bool),
                s_boolMapping,
                [
                    arguments[1],
                    arguments[2],
                ]);
        }

        if (method == s_mbrWithinMethod)
        {
            return TranslateFunction(
                "MBRWithin",
                typeof(bool),
                s_boolMapping,
                [
                    arguments[1],
                    arguments[2],
                ]);
        }

        if (method == s_mbrIntersectsMethod)
        {
            return TranslateFunction(
                "MBRIntersects",
                typeof(bool),
                s_boolMapping,
                [
                    arguments[1],
                    arguments[2],
                ]);
        }

        if (method == s_mbrOverlapsMethod)
        {
            return TranslateFunction(
                "MBROverlaps",
                typeof(bool),
                s_boolMapping,
                [
                    arguments[1],
                    arguments[2],
                ]);
        }

        if (method == s_mbrDisjointMethod)
        {
            return TranslateFunction(
                "MBRDisjoint",
                typeof(bool),
                s_boolMapping,
                [
                    arguments[1],
                    arguments[2],
                ]);
        }

        if (IsSpatialMethod(method))
        {
            MySqlLoggerMessages.MissingSpatialTranslation(_logger, $"{method.DeclaringType?.Name}.{method.Name}");
        }

        return null;
    }

    private SqlExpression TranslateGeometryFunction(
        string functionName,
        Type returnType,
        IReadOnlyList<SqlExpression> arguments
    )
    {
        var typeMapping = _typeMappingSource.FindMapping(returnType) as RelationalTypeMapping
            ?? arguments
                .Select(argument => argument.TypeMapping)
                .OfType<RelationalTypeMapping>()
                .FirstOrDefault();

        return TranslateFunction(functionName, returnType, typeMapping, arguments);
    }

    private SqlExpression TranslateFunction(
        string functionName,
        Type returnType,
        RelationalTypeMapping? typeMapping,
        IReadOnlyList<SqlExpression> arguments
    ) => _sqlExpressionFactory.Function(
        functionName,
        arguments,
        nullable: true,
        argumentsPropagateNullability: Enumerable
            .Repeat(true, arguments.Count)
            .ToArray(),
        returnType,
        typeMapping);

    private static bool IsSpatialMethod(
        MethodInfo method
    )
    {
        if (method.DeclaringType is null)
        {
            return false;
        }

        return typeof(Geometry).IsAssignableFrom(method.DeclaringType)
            || method.DeclaringType == typeof(MySqlNetTopologySuiteDbFunctionsExtensions);
    }
}
