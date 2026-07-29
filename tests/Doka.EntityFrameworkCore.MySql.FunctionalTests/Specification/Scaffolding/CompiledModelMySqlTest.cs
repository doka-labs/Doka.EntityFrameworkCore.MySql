using System.Runtime.CompilerServices;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using NetTopologySuite;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Scaffolding;

/// <summary>
/// Generates, compiles, loads, and executes EF Core runtime models with the provider's
/// runtime, design-time, and NetTopologySuite service graphs.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class CompiledModelMySqlTest : CompiledModelRelationalTestBase
{
    public CompiledModelMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override TestHelpers TestHelpers => MySqlTestHelpers.Instance;

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    protected override void BuildBigModel(
        ModelBuilder modelBuilder,
        bool jsonColumns
    )
    {
        base.BuildBigModel(modelBuilder, jsonColumns);

        modelBuilder.Entity<Data>(entity =>
        {
            entity.Property<int>("Id");
            entity.HasKey("Id");
            entity
                .Property<Point>("Point")
                .HasSrid(4326);
        });

        var manyTypes = modelBuilder.Entity<ManyTypes>();

        foreach (var property in manyTypes.Metadata.GetProperties())
        {
            if (property.IsKey())
            {
                continue;
            }

            var providerType = property.GetValueConverter()
                    ?.ProviderClrType
                ?? property.GetProviderClrType() ?? property.ClrType;

            // MySQL 8.4 section 10.4.7 and the MariaDB InnoDB limitations page
            // (retrieved 2026-07-28) document the approximately half-page row
            // limit and the off-page storage available to BLOB/TEXT columns.
            // The upstream stress model combines many independent converter
            // size hints in one table; retaining them as varchar/varbinary
            // would exceed that physical record limit even for short values.
            if (providerType == typeof(string))
            {
                manyTypes
                    .Property(property.Name)
                    .HasColumnType("longtext");
            }
            else if (providerType == typeof(byte[]))
            {
                manyTypes
                    .Property(property.Name)
                    .HasColumnType("longblob");
            }
        }
    }

    protected override void AssertBigModel(
        IModel model,
        bool jsonColumns
    )
    {
        base.AssertBigModel(model, jsonColumns);

        var data = model.FindEntityType(typeof(Data));
        Assert.NotNull(data);

        var point = data.FindProperty("Point");
        Assert.NotNull(point);
        Assert.Equal(typeof(Point), point.ClrType);
        Assert.Equal("point", point.GetColumnType());
        Assert.Equal(4326, point.GetMySqlSpatialReferenceSystemId());
        Assert.NotNull(point.GetValueComparer());
        Assert.NotNull(point.GetKeyValueComparer());
    }

    protected override async Task UseBigModel(
        DbContext context,
        bool jsonColumns
    )
    {
        try
        {
            await base.UseBigModel(context, jsonColumns);

            var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            var expected = geometryFactory.CreatePoint(new Coordinate(12.5, -7.25));
            var data = new Data();
            var entry = context.Add(data);
            entry.Property("Id")
                .CurrentValue = 1;
            entry.Property("Point")
                .CurrentValue = expected;

            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var stored = await context
                .Set<Data>()
                .SingleAsync();
            var actual = Assert.IsType<Point>(
                context
                    .Entry(stored)
                    .Property("Point")
                    .CurrentValue);

            Assert.Equal(expected.X, actual.X);
            Assert.Equal(expected.Y, actual.Y);
            Assert.Equal(4326, actual.SRID);
        }
        finally
        {
            if (!jsonColumns)
            {
                await DropQualifiedDatabaseAsync();
            }
        }
    }

    protected override void AddDesignTimeServices(
        IServiceCollection services
    ) => new MySqlNetTopologySuiteDesignTimeServices().ConfigureDesignTimeServices(services);

    protected override BuildSource AddReferences(
        BuildSource build,
        [CallerFilePath] string filePath = ""
    )
    {
        base.AddReferences(build, filePath);
        build.References.Add(BuildReference.ByName("Doka.EntityFrameworkCore.MySql"));
        build.References.Add(BuildReference.ByName("Doka.EntityFrameworkCore.MySql.NetTopologySuite"));
        build.References.Add(BuildReference.ByName("NetTopologySuite"));
        return build;
    }

    private static async Task DropQualifiedDatabaseAsync()
    {
        var connectionString =
            new MySqlConnectionStringBuilder(MySqlTestEnvironment.ConnectionString)
            {
                Database = string.Empty,
            }.ConnectionString;

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        command.CommandTimeout = MySqlTestStore.DefaultCommandTimeout;

        // Both inherited non-JSON cases use the fixed "mySchema" qualifier in
        // one shared test container. A main-database split table references the
        // auxiliary database, so disable FK enforcement only for this cleanup
        // session and restore it even when the drop fails.
        command.CommandText = "SET FOREIGN_KEY_CHECKS = 0;";
        await command.ExecuteNonQueryAsync();

        try
        {
            command.CommandText = "DROP DATABASE IF EXISTS `mySchema`;";
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            command.CommandText = "SET FOREIGN_KEY_CHECKS = 1;";
            await command.ExecuteNonQueryAsync();
        }
    }
}
