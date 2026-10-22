namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlNetTopologySuiteContractIntegrationTests
{
    private sealed record SpatialGeometrySet(
        Point Point,
        LineString LineString,
        Polygon Polygon,
        MultiPoint MultiPoint,
        MultiLineString MultiLineString,
        MultiPolygon MultiPolygon,
        GeometryCollection GeometryCollection,
        Geometry Geometry
    )
    {
        /// <summary>Uses the fixed property order shared by geometry-row assertions.</summary>
        public Geometry[] Values =>
        [
            Point,
            LineString,
            Polygon,
            MultiPoint,
            MultiLineString,
            MultiPolygon,
            GeometryCollection,
            Geometry,
        ];
    }

    internal sealed class SpatialMaterializationContext : DbContext
    {
        /// <summary>Uses the selected engine's spatial services and owned schema.</summary>
        public SpatialMaterializationContext(
            DbContextOptions<SpatialMaterializationContext> options
        ) : base(options) { }

        /// <summary>Provides the mixed-geometry rows and their related children.</summary>
        public DbSet<SpatialMaterializationEntity> Entities => Set<SpatialMaterializationEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SpatialMaterializationEntity>(entity =>
            {
                entity.ToTable(MaterializationTable);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Point)
                    .HasColumnType("point");
                entity
                    .Property(item => item.LineString)
                    .HasColumnType("linestring");
                entity
                    .Property(item => item.Polygon)
                    .HasColumnType("polygon");
                entity
                    .Property(item => item.MultiPoint)
                    .HasColumnType("multipoint");
                entity
                    .Property(item => item.MultiLineString)
                    .HasColumnType("multilinestring");
                entity
                    .Property(item => item.MultiPolygon)
                    .HasColumnType("multipolygon");
                entity
                    .Property(item => item.GeometryCollection)
                    .HasColumnType("geometrycollection");
                entity
                    .Property(item => item.Geometry)
                    .HasColumnType("geometry");
                entity
                    .HasMany(item => item.Children)
                    .WithOne()
                    .HasForeignKey(child => child.ParentId);
            });
            modelBuilder.Entity<SpatialMaterializationChild>(entity =>
            {
                entity.ToTable(MaterializationChildTable);
                entity.HasKey(item => item.Id);
            });
        }
    }

    internal sealed class SpatialMaterializationEntity
    {
        public int Id { get; set; }
        public Point Point { get; set; } = null!;
        public LineString LineString { get; set; } = null!;
        public Polygon Polygon { get; set; } = null!;
        public MultiPoint MultiPoint { get; set; } = null!;
        public MultiLineString MultiLineString { get; set; } = null!;
        public MultiPolygon MultiPolygon { get; set; } = null!;
        public GeometryCollection GeometryCollection { get; set; } = null!;
        public Geometry Geometry { get; set; } = null!;
        public List<SpatialMaterializationChild> Children { get; set; } = [];
    }

    internal sealed class SpatialMaterializationChild
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
    }

    internal sealed class SpatialCrossesContext : DbContext
    {
        /// <summary>Uses the selected engine's nullable spatial operand mapping.</summary>
        public SpatialCrossesContext(
            DbContextOptions<SpatialCrossesContext> options
        ) : base(options) { }

        /// <summary>Provides the dimension matrix or scalar/aggregate fixture row.</summary>
        public DbSet<SpatialCrossesRow> Rows => Set<SpatialCrossesRow>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SpatialCrossesRow>(entity =>
            {
                entity.ToTable(CrossesTable);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.LeftGeometry)
                    .HasColumnType("geometry");
                entity
                    .Property(item => item.RightGeometry)
                    .HasColumnType("geometry");
            });
        }
    }

    internal sealed class SpatialCrossesRow
    {
        public int Id { get; set; }
        public Geometry? LeftGeometry { get; set; }
        public Geometry? RightGeometry { get; set; }
    }

    private sealed class EmptySpatialContractContext : DbContext
    {
        /// <summary>Represents the source model before an SRID column is introduced.</summary>
        public EmptySpatialContractContext(
            DbContextOptions<EmptySpatialContractContext> options
        ) : base(options) { }
    }

    private sealed class SpatialSridContractContext : DbContext
    {
        /// <summary>Represents the target SRID contract to migrate and reverse engineer.</summary>
        public SpatialSridContractContext(
            DbContextOptions<SpatialSridContractContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SpatialSridRow>(entity =>
            {
                entity.ToTable(SridTable);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Location)
                    .HasColumnType("point")
                    .HasSrid(4326);
            });
        }
    }

    private sealed class SpatialSridRow
    {
        public int Id { get; set; }
        public Point Location { get; set; } = null!;
    }
}
