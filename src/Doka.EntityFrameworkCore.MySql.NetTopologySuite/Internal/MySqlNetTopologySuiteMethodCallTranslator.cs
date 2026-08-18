namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlNetTopologySuiteMethodCallTranslator : IMethodCallTranslator
{
    private static readonly BoolTypeMapping s_boolMapping = new("tinyint(1)", DbType.Boolean);
    private static readonly DoubleTypeMapping s_doubleMapping = new("double", DbType.Double);
    private static readonly IntTypeMapping s_intMapping = new("int", DbType.Int32);
    private static readonly StringTypeMapping s_stringMapping = new("longtext", DbType.String, unicode: true);
    private static readonly ByteArrayTypeMapping s_binaryMapping = new("longblob", DbType.Binary);

    private static readonly string[] s_coversPatterns =
    [
        "T*****FF*",
        "*T****FF*",
        "***T**FF*",
        "****T*FF*",
    ];

    private static readonly MethodInfo s_asTextMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.AsText), Type.EmptyTypes)!;

    private static readonly MethodInfo s_asBinaryMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.AsBinary), Type.EmptyTypes)!;

    private static readonly MethodInfo s_toTextMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.ToText), Type.EmptyTypes)!;

    private static readonly MethodInfo s_toBinaryMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.ToBinary), Type.EmptyTypes)!;

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

    private static readonly MethodInfo s_coversMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Covers), [typeof(Geometry)])!;

    private static readonly MethodInfo s_coveredByMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.CoveredBy), [typeof(Geometry)])!;

    private static readonly MethodInfo s_distanceMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Distance), [typeof(Geometry)])!;

    private static readonly MethodInfo s_isWithinDistanceMethod =
        typeof(Geometry).GetRuntimeMethod(
            nameof(Geometry.IsWithinDistance),
            [
                typeof(Geometry),
                typeof(double),
            ])!;

    private static readonly MethodInfo s_bufferMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Buffer), [typeof(double)])!;

    private static readonly MethodInfo s_bufferWithQuadrantSegmentsMethod =
        typeof(Geometry).GetRuntimeMethod(
            nameof(Geometry.Buffer),
            [
                typeof(double),
                typeof(int),
            ])!;

    private static readonly MethodInfo s_convexHullMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.ConvexHull), Type.EmptyTypes)!;

    private static readonly MethodInfo s_intersectionMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Intersection), [typeof(Geometry)])!;

    private static readonly MethodInfo s_unionMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Union), [typeof(Geometry)])!;

    private static readonly MethodInfo s_unionVoidMethod =
        typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Union), Type.EmptyTypes)!;

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

    private static readonly MethodInfo s_getInteriorRingNMethod =
        typeof(Polygon).GetRuntimeMethod(nameof(Polygon.GetInteriorRingN), [typeof(int)])!;

    private static readonly MethodInfo s_enumerableElementAtMethod = typeof(Enumerable)
        .GetMethods()
        .Single(method => method is { Name: nameof(Enumerable.ElementAt), IsGenericMethodDefinition: true }
            && method.GetParameters() is [_, { ParameterType: { } indexType }]
            && indexType == typeof(int));

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
        [s_equalsTopologicallyMethod] = "ST_Equals",
    };

    private static readonly Dictionary<MethodInfo, string> s_geometryInstanceFunctions = new()
    {
        [s_bufferMethod] = "ST_Buffer",
        [s_convexHullMethod] = "ST_ConvexHull",
        [s_intersectionMethod] = "ST_Intersection",
        [s_unionMethod] = "ST_Union",
        [s_differenceMethod] = "ST_Difference",
        [s_symmetricDifferenceMethod] = "ST_SymDifference",
    };

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ILogger _logger;
    private readonly bool _supportsMariaDbSpatialFunctions;
    private readonly bool _supportsBufferStrategies;

    public MySqlNetTopologySuiteMethodCallTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource,
        ILogger logger,
        bool supportsMariaDbSpatialFunctions,
        bool supportsBufferStrategies
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory ?? throw new ArgumentNullException(nameof(sqlExpressionFactory));
        _typeMappingSource = typeMappingSource ?? throw new ArgumentNullException(nameof(typeMappingSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _supportsMariaDbSpatialFunctions = supportsMariaDbSpatialFunctions;
        _supportsBufferStrategies = supportsBufferStrategies;
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

        if (method is { } textMethod
            && (textMethod == s_asTextMethod || textMethod == s_toTextMethod)
            && instance is not null)
        {
            return TranslateFunction("ST_AsText", typeof(string), s_stringMapping, [instance]);
        }

        if (method is { } binaryMethod
            && (binaryMethod == s_asBinaryMethod || binaryMethod == s_toBinaryMethod)
            && instance is not null)
        {
            return TranslateFunction("ST_AsBinary", typeof(byte[]), s_binaryMapping, [instance]);
        }

        if (method == s_distanceMethod
            && instance is not null)
        {
            WarnIfStaticSridMismatch(instance, arguments[0]);
            var alignedArgument = AlignStaticGeometrySrid(instance, arguments[0]);

            return TranslateFunction(
                "ST_Distance",
                typeof(double),
                s_doubleMapping,
                [
                    instance,
                    alignedArgument,
                ]);
        }

        if (method == s_isWithinDistanceMethod
            && instance is not null)
        {
            var distance = TranslateFunction(
                "ST_Distance",
                typeof(double),
                s_doubleMapping,
                [
                    instance,
                    arguments[0],
                ]);

            return _sqlExpressionFactory.LessThanOrEqual(distance, arguments[1]);
        }

        if (method == s_bufferWithQuadrantSegmentsMethod
            && instance is not null)
        {
            if (!_supportsBufferStrategies)
            {
                MySqlLoggerMessages.MissingSpatialTranslation(
                    _logger,
                    "Geometry.Buffer(Double, Int32) requires MySQL ST_Buffer_Strategy");

                return null;
            }

            return TranslateBufferWithQuadrantSegments(instance, arguments);
        }

        if (method == s_unionVoidMethod
            && instance is not null)
        {
            // MySQL exposes only the binary ST_Union form. Unioning a geometry
            // with itself provides the same unary topological cleanup semantics.
            return TranslateGeometryFunction(
                "ST_Union",
                method.ReturnType,
                [
                    instance,
                    instance,
                ]);
        }

        if (method == s_getGeometryNMethod
            && instance is not null)
        {
            return TranslateOneBasedGeometryElement("ST_GeometryN", method.ReturnType, instance, arguments[0]);
        }

        if (method == s_getPointNMethod
            && instance is not null)
        {
            return TranslateOneBasedGeometryElement("ST_PointN", method.ReturnType, instance, arguments[0]);
        }

        if (method == s_getInteriorRingNMethod
            && instance is not null)
        {
            return TranslateOneBasedGeometryElement("ST_InteriorRingN", method.ReturnType, instance, arguments[0]);
        }

        if (method.IsGenericMethod
            && method.GetGenericMethodDefinition() == s_enumerableElementAtMethod
            && method.ReturnType == typeof(Geometry)
            && arguments is [var collection, var index])
        {
            return TranslateOneBasedGeometryElement("ST_GeometryN", method.ReturnType, collection, index);
        }

        if (method == s_coversMethod
            && instance is not null)
        {
            return TranslateCovers(instance, arguments[0]);
        }

        if (method == s_coveredByMethod
            && instance is not null)
        {
            return TranslateCovers(arguments[0], instance);
        }

        if (method == s_crossesMethod
            && instance is not null)
        {
            return _supportsMariaDbSpatialFunctions
                ? TranslateMariaDbCrosses(instance, arguments[0])
                : TranslateFunction(
                    "ST_Crosses",
                    typeof(bool),
                    s_boolMapping,
                    [
                        instance,
                        arguments[0],
                    ]);
        }

        if (method == s_relatePatternMethod
            && instance is not null)
        {
            if (!_supportsMariaDbSpatialFunctions)
            {
                MySqlLoggerMessages.MissingSpatialTranslation(_logger, "Geometry.Relate");
                return null;
            }

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

    private SqlExpression TranslateBufferWithQuadrantSegments(
        SqlExpression instance,
        IReadOnlyList<SqlExpression> arguments
    )
    {
        var pointsPerCircle = _sqlExpressionFactory.Multiply(
            arguments[1],
            _sqlExpressionFactory.Constant(4, s_intMapping));

        var strategyName = typeof(Point).IsAssignableFrom(instance.Type)
            || typeof(MultiPoint).IsAssignableFrom(instance.Type)
                ? "point_circle"
                : "join_round";

        var strategy = TranslateFunction(
            "ST_Buffer_Strategy",
            typeof(byte[]),
            s_binaryMapping,
            [
                _sqlExpressionFactory.Constant(strategyName, s_stringMapping),
                pointsPerCircle,
            ]);

        return TranslateGeometryFunction(
            "ST_Buffer",
            returnType: typeof(Geometry),
            [
                instance,
                arguments[0],
                strategy,
            ]);
    }

    private SqlExpression TranslateOneBasedGeometryElement(
        string functionName,
        Type returnType,
        SqlExpression instance,
        SqlExpression zeroBasedIndex
    ) => TranslateGeometryFunction(
        functionName,
        returnType,
        [
            instance,
            _sqlExpressionFactory.Add(zeroBasedIndex, _sqlExpressionFactory.Constant(1, s_intMapping)),
        ]);

    private SqlExpression TranslateCovers(
        SqlExpression coveringGeometry,
        SqlExpression coveredGeometry
    )
    {
        if (_supportsMariaDbSpatialFunctions)
        {
            // MariaDB exposes DE-9IM relation matching but no ST_Covers function.
            // The four OGC covers patterns include interior, boundary, and equality
            // cases without relying on MariaDB's incomplete mixed-dimension difference.
            return s_coversPatterns
                .Select(pattern => TranslateFunction(
                    "ST_Relate",
                    typeof(bool),
                    s_boolMapping,
                    [
                        coveringGeometry,
                        coveredGeometry,
                        _sqlExpressionFactory.Constant(pattern, s_stringMapping),
                    ]))
                .Aggregate(_sqlExpressionFactory.OrElse);
        }

        var difference = TranslateGeometryFunction(
            "ST_Difference",
            typeof(Geometry),
            [
                coveredGeometry,
                coveringGeometry,
            ]);

        return TranslateFunction("ST_IsEmpty", typeof(bool), s_boolMapping, [difference]);
    }

    private SqlExpression TranslateMariaDbCrosses(
        SqlExpression left,
        SqlExpression right
    )
    {
        var leftDimension = TranslateFunction("ST_Dimension", typeof(int), s_intMapping, [left]);
        var rightDimension = TranslateFunction("ST_Dimension", typeof(int), s_intMapping, [right]);

        // Keep all seven pairs explicit so this stays auditable one-for-one
        // against the version-pinned NetTopologySuite Crosses table in D-012.
        return _sqlExpressionFactory.Case(
            [
                new CaseWhenClause(HasDimensions(0, 1), Relates("T*T******")),
                new CaseWhenClause(HasDimensions(0, 2), Relates("T*T******")),
                new CaseWhenClause(HasDimensions(1, 0), Relates("T*****T**")),
                new CaseWhenClause(HasDimensions(1, 1), Relates("0********")),
                new CaseWhenClause(HasDimensions(1, 2), Relates("T*T******")),
                new CaseWhenClause(HasDimensions(2, 0), Relates("T*****T**")),
                new CaseWhenClause(HasDimensions(2, 1), Relates("T*****T**")),
            ],
            _sqlExpressionFactory.Constant(false, s_boolMapping));

        SqlExpression HasDimensions(
            int leftValue,
            int rightValue
        ) => _sqlExpressionFactory.AndAlso(
            _sqlExpressionFactory.Equal(
                leftDimension,
                _sqlExpressionFactory.Constant(leftValue, s_intMapping)),
            _sqlExpressionFactory.Equal(
                rightDimension,
                _sqlExpressionFactory.Constant(rightValue, s_intMapping)));

        SqlExpression Relates(
            string pattern
        ) => TranslateFunction(
            "ST_Relate",
            typeof(bool),
            s_boolMapping,
            [
                left,
                right,
                _sqlExpressionFactory.Constant(pattern, s_stringMapping),
            ]);
    }

    private SqlExpression AlignStaticGeometrySrid(
        SqlExpression referenceGeometry,
        SqlExpression geometry
    )
    {
        if (geometry is not SqlConstantExpression)
        {
            return geometry;
        }

        var referenceSrid = TranslateFunction("ST_SRID", typeof(int), s_intMapping, [referenceGeometry]);

        if (_supportsMariaDbSpatialFunctions)
        {
            // MariaDB's ST_SRID is getter-only. Reconstructing the same WKB with the
            // reference SRID preserves coordinates while using MariaDB's documented
            // optional-SRID constructor.
            var binary = TranslateFunction("ST_AsWKB", typeof(byte[]), s_binaryMapping, [geometry]);

            return TranslateGeometryFunction(
                "ST_GeomFromWKB",
                geometry.Type,
                [
                    binary,
                    referenceSrid,
                ]);
        }

        return TranslateGeometryFunction(
            "ST_SRID",
            geometry.Type,
            [
                geometry,
                referenceSrid,
            ]);
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

    /// <summary>
    /// Best-effort static SRID-mismatch detection. When BOTH operands of
    /// <c>Geometry.Distance</c> are SQL-time constants whose CLR value carries
    /// a non-zero SRID, the translator compares them and emits
    /// <see cref="MySqlEventId.SpatialSridMismatchDetected"/> on divergence.
    /// Mismatches between two columns, or between a column and a runtime-derived
    /// geometry, escape this check (the translator has no per-column SRID
    /// metadata at hand); D-012 documents the limitation alongside the warning.
    /// </summary>
    private void WarnIfStaticSridMismatch(
        SqlExpression first,
        SqlExpression second
    )
    {
        var firstSrid = TryReadStaticSrid(first);
        var secondSrid = TryReadStaticSrid(second);

        if (firstSrid is null || secondSrid is null || firstSrid == secondSrid)
        {
            return;
        }

        MySqlLoggerMessages.SpatialSridMismatchDetected(_logger, firstSrid.Value, secondSrid.Value);
    }

    private static int? TryReadStaticSrid(
        SqlExpression expression
    )
    {
        if (expression is SqlConstantExpression { Value: Geometry geometry })
        {
            return geometry.SRID;
        }

        if (expression is ColumnExpression { Column: { } column })
        {
            var property = column
                .PropertyMappings.Select(mapping => mapping.Property)
                .FirstOrDefault();

            if (property?.FindAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId)?.Value is int columnSrid)
            {
                return columnSrid;
            }
        }

        return null;
    }

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
