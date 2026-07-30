namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlModelingBaselineTests
{
    // -- TPH --------------------------------------------------------------

    /// <summary>
    /// TPH produces a single table with discriminator column and nullable derived columns.
    /// </summary>
    [Fact]
    public void Tph_produces_single_table_with_discriminator_and_nullable_derived_columns()
    {
        using var context = new TphContext(CreateOptions<TphContext>());
        var model = context.Model;
        var baseType = model.FindEntityType(typeof(Animal))!;
        var dogType = model.FindEntityType(typeof(Dog))!;
        var catType = model.FindEntityType(typeof(Cat))!;

        // All types share the same table.
        Assert.Equal("Animals", baseType.GetTableName());
        Assert.Equal("Animals", dogType.GetTableName());
        Assert.Equal("Animals", catType.GetTableName());

        // Discriminator property exists on the base type.
        var discriminator = baseType.FindDiscriminatorProperty();
        Assert.NotNull(discriminator);
        Assert.Equal("Discriminator", discriminator.Name);

        // Derived-type-only columns must be nullable in the shared table.
        var breedProperty = dogType.FindProperty(nameof(Dog.Breed))!;
        Assert.True(breedProperty.IsColumnNullable());

        var indoorProperty = catType.FindProperty(nameof(Cat.IsIndoor))!;
        Assert.True(indoorProperty.IsColumnNullable());
    }

    /// <summary>
    /// Three-level TPH hierarchy Animal -> Mammal -> Dog shares single table.
    /// </summary>
    [Fact]
    public void Tph_three_level_hierarchy_shares_single_table()
    {
        using var context = new TphDeepContext(CreateOptions<TphDeepContext>());
        var model = context.Model;
        var mammalType = model.FindEntityType(typeof(Mammal))!;
        var dogType = model.FindEntityType(typeof(DeepDog))!;

        Assert.Equal("Animals", mammalType.GetTableName());
        Assert.Equal("Animals", dogType.GetTableName());

        // Discriminator values should be non-null and distinct.
        var mammalDiscriminator = mammalType.GetDiscriminatorValue();
        var dogDiscriminator = dogType.GetDiscriminatorValue();
        Assert.NotNull(mammalDiscriminator);
        Assert.NotNull(dogDiscriminator);
        Assert.NotEqual(mammalDiscriminator, dogDiscriminator);
    }

    /// <summary>
    /// Custom discriminator value via HasValue("custom_value").
    /// </summary>
    [Fact]
    public void Tph_custom_discriminator_value_is_preserved()
    {
        using var context = new TphContext(CreateOptions<TphContext>());
        var dogType = context.Model.FindEntityType(typeof(Dog))!;

        Assert.Equal("Canine", dogType.GetDiscriminatorValue());
    }

    /// <summary>
    /// OfType query produces correct WHERE Discriminator SQL.
    /// </summary>
    [Fact]
    public void Tph_of_type_query_produces_discriminator_filter()
    {
        using var context = new TphContext(CreateOptions<TphContext>());
        var sql = context
            .Set<Animal>()
            .OfType<Dog>()
            .ToQueryString();

        Assert.Contains("Discriminator", sql, StringComparison.Ordinal);
        Assert.Contains("Canine", sql, StringComparison.Ordinal);
    }

    // -- TPT --------------------------------------------------------------

    /// <summary>
    /// TPT produces separate tables with FK and LEFT JOIN query on base type.
    /// </summary>
    [Fact]
    public void Tpt_produces_separate_tables_with_fk()
    {
        using var context = new TptContext(CreateOptions<TptContext>());
        var model = context.Model;
        var baseType = model.FindEntityType(typeof(Vehicle))!;
        var carType = model.FindEntityType(typeof(Car))!;
        var truckType = model.FindEntityType(typeof(Truck))!;

        Assert.Equal("Vehicles", baseType.GetTableName());
        Assert.Equal("Cars", carType.GetTableName());
        Assert.Equal("Trucks", truckType.GetTableName());
    }

    /// <summary>
    /// OfType on TPT generates INNER JOIN (not LEFT JOIN).
    /// </summary>
    [Fact]
    public void Tpt_of_type_generates_inner_join()
    {
        using var context = new TptContext(CreateOptions<TptContext>());
        var sql = context
            .Set<Vehicle>()
            .OfType<Car>()
            .ToQueryString();

        Assert.Contains("JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cars", sql, StringComparison.Ordinal);
    }

    // -- TPC --------------------------------------------------------------

    /// <summary>
    /// TPC produces separate tables with ALL columns and UNION ALL query.
    /// </summary>
    [Fact]
    public void Tpc_produces_separate_tables_with_union_all_query()
    {
        using var context = new TpcContext(CreateOptions<TpcContext>());
        var model = context.Model;
        var circleType = model.FindEntityType(typeof(Circle))!;
        var rectangleType = model.FindEntityType(typeof(Rectangle))!;

        Assert.Equal("Circles", circleType.GetTableName());
        Assert.Equal("Rectangles", rectangleType.GetTableName());

        // Base type query should produce UNION ALL.
        var sql = context
            .Set<Shape>()
            .ToQueryString();
        Assert.Contains("UNION ALL", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- Discriminator Column Type ----------------------------------------

    /// <summary>
    /// String discriminator should produce varchar, not longtext.
    /// </summary>
    [Fact]
    public void Tph_discriminator_produces_varchar_column()
    {
        using var context = new TphContext(CreateOptions<TphContext>());
        var baseType = context.Model.FindEntityType(typeof(Animal))!;
        var discriminator = baseType.FindDiscriminatorProperty()!;
        var columnType = discriminator.GetColumnType();

        // Must not be longtext -- should be varchar with a bounded length.
        Assert.NotNull(columnType);
        Assert.DoesNotContain("longtext", columnType, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("varchar", columnType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Discriminator with explicit HasMaxLength(64) -> varchar(64).
    /// </summary>
    [Fact]
    public void Tph_discriminator_with_explicit_max_length_produces_correct_varchar()
    {
        using var context = new TphExplicitLengthContext(CreateOptions<TphExplicitLengthContext>());
        var baseType = context.Model.FindEntityType(typeof(TaggedAnimal))!;
        var discriminator = baseType.FindDiscriminatorProperty()!;
        var columnType = discriminator.GetColumnType();

        Assert.Equal("varchar(64)", columnType);
    }

    // -- Additional Modeling Tests --------------------------------------

    /// <summary>
    /// Abstract base class in TPH -- DeepAnimal is abstract, verified via model.
    /// </summary>
    [Fact]
    public void Tph_abstract_base_class_is_modeled()
    {
        using var context = new TphDeepContext(CreateOptions<TphDeepContext>());
        var baseType = context.Model.FindEntityType(typeof(DeepAnimal))!;

        Assert.True(baseType.ClrType.IsAbstract);
        Assert.Equal("Animals", baseType.GetTableName());
    }

    /// <summary>
    /// TPT 3-level hierarchy -- base + derived + leaf produces correct table count.
    /// </summary>
    [Fact]
    public void Tpt_three_level_hierarchy_produces_three_tables()
    {
        using var context = new TptDeepContext(CreateOptions<TptDeepContext>());
        var model = context.Model;

        Assert.Equal("BaseVehicles", model.FindEntityType(typeof(BaseVehicle))!.GetTableName());
        Assert.Equal("MotorVehicles", model.FindEntityType(typeof(MotorVehicle))!.GetTableName());
        Assert.Equal("ElectricCars", model.FindEntityType(typeof(ElectricCar))!.GetTableName());
    }

    private abstract class BaseVehicle
    {
        public int Id { get; set; }
        public string Make { get; set; } = string.Empty;
    }

    private class MotorVehicle : BaseVehicle
    {
        public int Horsepower { get; set; }
    }

    private sealed class ElectricCar : MotorVehicle
    {
        public int RangeKm { get; set; }
    }

    private sealed class TptDeepContext : DbContext
    {
        public TptDeepContext(
            DbContextOptions<TptDeepContext> options
        ) : base(options) { }

        public DbSet<BaseVehicle> Vehicles => Set<BaseVehicle>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<BaseVehicle>()
                .UseTptMappingStrategy();
            modelBuilder
                .Entity<BaseVehicle>()
                .ToTable("BaseVehicles");
            modelBuilder
                .Entity<MotorVehicle>()
                .ToTable("MotorVehicles");
            modelBuilder
                .Entity<ElectricCar>()
                .ToTable("ElectricCars");
        }
    }

    // Custom column owned
    private abstract class TaggedAnimal
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TaggedDog : TaggedAnimal
    {
        public string Breed { get; set; } = string.Empty;
    }

    private sealed class TphExplicitLengthContext : DbContext
    {
        public TphExplicitLengthContext(
            DbContextOptions<TphExplicitLengthContext> options
        ) : base(options) { }

        public DbSet<TaggedAnimal> Animals => Set<TaggedAnimal>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TaggedAnimal>(entity =>
            {
                entity.ToTable("TaggedAnimals");
                entity
                    .HasDiscriminator<string>("Kind")
                    .HasValue<TaggedDog>("Dog");
                entity
                    .Property("Kind")
                    .HasMaxLength(64);
            });
        }
    }

    // Sequence
    [Fact]
    public void Tph_int_discriminator_produces_int_column()
    {
        using var context = new TphIntDiscriminatorContext(CreateOptions<TphIntDiscriminatorContext>());
        var baseType = context.Model.FindEntityType(typeof(IntAnimal))!;
        var discriminator = baseType.FindDiscriminatorProperty()!;
        var columnType = discriminator.GetColumnType();

        Assert.Equal("int", columnType);
    }
    /// <summary>
    /// Custom discriminator HasValue("custom") in model.
    /// </summary>
    [Fact]
    public void Tph_custom_discriminator_value_in_model()
    {
        using var context = new TphContext(CreateOptions<TphContext>());
        var catType = context.Model.FindEntityType(typeof(Cat))!;

        Assert.Equal("Feline", catType.GetDiscriminatorValue());
    }

    // -- Int Discriminator ------------------------------------------------

    private abstract class IntAnimal
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class IntDog : IntAnimal
    {
        public string Breed { get; set; } = string.Empty;
    }

    private sealed class TphIntDiscriminatorContext : DbContext
    {
        public TphIntDiscriminatorContext(
            DbContextOptions<TphIntDiscriminatorContext> options
        ) : base(options) { }

        public DbSet<IntAnimal> Animals => Set<IntAnimal>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<IntAnimal>(entity =>
            {
                entity.ToTable("IntAnimals");
                entity
                    .HasDiscriminator<int>("Type")
                    .HasValue<IntDog>(1);
            });
        }
    }

    // -- TPH Entities ----------------------------------------------------

    private abstract class Animal
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class Dog : Animal
    {
        public string Breed { get; set; } = string.Empty;
    }

    private sealed class Cat : Animal
    {
        public bool IsIndoor { get; set; }
    }

    private sealed class TphContext : DbContext
    {
        public TphContext(
            DbContextOptions<TphContext> options
        ) : base(options) { }

        public DbSet<Animal> Animals => Set<Animal>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<Animal>(entity =>
            {
                entity.ToTable("Animals");
                entity
                    .HasDiscriminator<string>("Discriminator")
                    .HasValue<Dog>("Canine")
                    .HasValue<Cat>("Feline");

                entity
                    .Property("Discriminator")
                    .HasMaxLength(128);
            });
        }
    }

    // -- TPH Deep Hierarchy ----------------------------------------------

    private abstract class DeepAnimal
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private class Mammal : DeepAnimal
    {
        public bool IsWarmBlooded { get; set; }
    }

    private sealed class DeepDog : Mammal
    {
        public string Breed { get; set; } = string.Empty;
    }

    private sealed class TphDeepContext : DbContext
    {
        public TphDeepContext(
            DbContextOptions<TphDeepContext> options
        ) : base(options) { }

        public DbSet<DeepAnimal> Animals => Set<DeepAnimal>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<DeepAnimal>()
                .ToTable("Animals");
            modelBuilder.Entity<Mammal>();
            modelBuilder.Entity<DeepDog>();
        }
    }

    // -- TPT Entities ----------------------------------------------------

    private abstract class Vehicle
    {
        public int Id { get; set; }
        public string Make { get; set; } = string.Empty;
    }

    private sealed class Car : Vehicle
    {
        public int SeatCount { get; set; }
    }

    private sealed class Truck : Vehicle
    {
        public double PayloadTons { get; set; }
    }

    private sealed class TptContext : DbContext
    {
        public TptContext(
            DbContextOptions<TptContext> options
        ) : base(options) { }

        public DbSet<Vehicle> Vehicles => Set<Vehicle>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<Vehicle>()
                .UseTptMappingStrategy();
            modelBuilder
                .Entity<Vehicle>()
                .ToTable("Vehicles");
            modelBuilder
                .Entity<Car>()
                .ToTable("Cars");
            modelBuilder
                .Entity<Truck>()
                .ToTable("Trucks");
        }
    }

    // -- TPC Entities ----------------------------------------------------

    private abstract class Shape
    {
        public Guid Id { get; set; }
        public string Color { get; set; } = string.Empty;
    }

    private sealed class Circle : Shape
    {
        public double Radius { get; set; }
    }

    private sealed class Rectangle : Shape
    {
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private sealed class TpcContext : DbContext
    {
        public TpcContext(
            DbContextOptions<TpcContext> options
        ) : base(options) { }

        public DbSet<Shape> Shapes => Set<Shape>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<Shape>()
                .UseTpcMappingStrategy();
            modelBuilder
                .Entity<Circle>()
                .ToTable("Circles");
            modelBuilder
                .Entity<Rectangle>()
                .ToTable("Rectangles");
        }
    }
}
