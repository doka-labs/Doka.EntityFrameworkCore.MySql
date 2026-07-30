using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.TestModels.SpatialModel;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Spatial;

public sealed class MySqlSpatialFixture : SpatialFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    protected override IServiceCollection AddServices(
        IServiceCollection serviceCollection
    ) => base
        .AddServices(serviceCollection)
        .AddEntityFrameworkDokaMySqlNetTopologySuite();

    public override DbContextOptionsBuilder AddOptions(
        DbContextOptionsBuilder builder
    )
    {
        var optionsBuilder = base.AddOptions(builder);
        new MySqlDbContextOptionsBuilder(optionsBuilder).UseNetTopologySuite();

        return optionsBuilder;
    }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder,
        DbContext context
    )
    {
        base.OnModelCreating(modelBuilder, context);

        modelBuilder
            .Entity<PointEntity>()
            .Property(entity => entity.PointZ)
            .HasColumnType("POINT");
        modelBuilder
            .Entity<PointEntity>()
            .Property(entity => entity.PointM)
            .HasColumnType("POINT");
        modelBuilder
            .Entity<PointEntity>()
            .Property(entity => entity.PointZM)
            .HasColumnType("POINT");
    }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class MySqlSpatialTest : SpatialTestBase<MySqlSpatialFixture>
{
    public MySqlSpatialTest(
        MySqlSpatialFixture fixture
    ) : base(fixture) { }

    protected override void UseTransaction(
        DatabaseFacade facade,
        IDbContextTransaction transaction
    ) => facade.UseTransaction(transaction.GetDbTransaction());

    [SpecEngineLimitationFact("MYSQL84-POINT-EMPTY", "mysql84")]
    public override void Translators_handle_static_members() => base.Translators_handle_static_members();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SPATIAL-ZM-ORDINATES", "mysql84", "mariadb114", "mariadb118")]
    public override void Can_roundtrip_Z_and_M() => base.Can_roundtrip_Z_and_M();
}
