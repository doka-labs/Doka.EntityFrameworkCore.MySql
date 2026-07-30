using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.TestModels.SpatialModel;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Fixtures;

/// <summary>
/// Centralizes provider wiring for the official query-model fixtures. Each fixture keeps
/// its upstream model and seed data intact and changes only the relational test-store factory.
/// </summary>
public sealed class ComplexNavigationsMySqlFixture : ComplexNavigationsQueryRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

public sealed class ComplexNavigationsSharedTypeMySqlFixture : ComplexNavigationsSharedTypeQueryRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

public sealed class ComplexTypeQueryMySqlFixture : ComplexTypeQueryRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

public sealed class CompositeKeysQueryMySqlFixture : CompositeKeysQueryRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

public sealed class GearsOfWarQueryMySqlFixture : GearsOfWarQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

public sealed class ManyToManyQueryMySqlFixture : ManyToManyQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

public sealed class NullSemanticsQueryMySqlFixture : NullSemanticsQueryFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

public sealed class OptionalDependentQueryMySqlFixture : OptionalDependentQueryFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

public sealed class PrimitiveCollectionsQueryMySqlFixture :
    PrimitiveCollectionsQueryTestBase<PrimitiveCollectionsQueryMySqlFixture>.PrimitiveCollectionsQueryFixtureBase,
    ITestSqlLoggerFactory
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;
}

public sealed class SpatialQueryMySqlFixture : SpatialQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    protected override IServiceCollection AddServices(
        IServiceCollection serviceCollection
    ) => base
        .AddServices(serviceCollection)
        .AddEntityFrameworkDokaMySqlNetTopologySuite();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder,
        DbContext context
    )
    {
        base.OnModelCreating(modelBuilder, context);

        var typeMappingSource = context.GetService<IRelationalTypeMappingSource>();
        var pointMapping = typeMappingSource.FindMapping(typeof(Point))!;
        var geoPointMapping = (RelationalTypeMapping)pointMapping.WithComposedConverter(new GeoPointConverter());
        var functionBuilder = modelBuilder.HasDbFunction(
            typeof(GeoExtensions).GetMethod(nameof(GeoExtensions.Distance))!);
        functionBuilder.HasTranslation(arguments => new SqlFunctionExpression(
            "ST_Distance",
            arguments
                .Select(argument => argument.TypeMapping is null
                    ? ApplyTypeMapping(argument, geoPointMapping)
                    : argument)
                .ToArray(),
            nullable: true,
            argumentsPropagateNullability: arguments
                .Select(_ => true)
                .ToList(),
            typeof(double),
            typeMapping: null));

        functionBuilder.HasParameter("x")
            .Metadata.TypeMapping = geoPointMapping;
        functionBuilder.HasParameter("y")
            .Metadata.TypeMapping = geoPointMapping;
    }

    private static SqlExpression ApplyTypeMapping(
        SqlExpression expression,
        RelationalTypeMapping typeMapping
    ) => expression switch
    {
        ColumnExpression column => column.ApplyTypeMapping(typeMapping),
        SqlConstantExpression constant => constant.ApplyTypeMapping(typeMapping),
        SqlFunctionExpression function => function.ApplyTypeMapping(typeMapping),
        SqlParameterExpression parameter => parameter.ApplyTypeMapping(typeMapping),
        _ => expression,
    };
}
