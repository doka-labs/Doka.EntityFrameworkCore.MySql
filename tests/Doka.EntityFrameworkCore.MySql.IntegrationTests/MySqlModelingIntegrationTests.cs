using System.ComponentModel.DataAnnotations;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Integration tests for EF Core modeling features against live MySQL/MariaDB.
/// Covers TPH CRUD, OwnsOne, cascade delete, self-referencing FK, M2M.
/// </summary>
public sealed class MySqlModelingIntegrationTests
{
    /// <summary>
    /// TPH CRUD round-trip -- insert, query with OfType, discriminator filtering.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Tph_crud_roundtrip_on_mysql84()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        await using var context = new TphCrudContext(CreateOptions<TphCrudContext>(connectionString));

        await CleanupAsync(context, "Phase5_TphAnimals");

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS `Phase5_TphAnimals` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `Type` varchar(64) NOT NULL,
                `Name` varchar(100) NOT NULL,
                `Breed` varchar(100) NULL,
                `IsIndoor` tinyint(1) NULL,
                CONSTRAINT `PK_Phase5_TphAnimals` PRIMARY KEY (`Id`)
            ) CHARACTER SET utf8mb4;
            """);

        try
        {
            context.Animals.Add(
                new TphDog
                {
                    Name = "Rex",
                    Breed = "Shepherd"
                });
            context.Animals.Add(
                new TphCat
                {
                    Name = "Whiskers",
                    IsIndoor = true
                });
            await context.SaveChangesAsync();

            var dogs = await context
                .Animals.OfType<TphDog>()
                .ToListAsync();
            Assert.Single(dogs);
            Assert.Equal("Shepherd", dogs[0].Breed);

            var allAnimals = await context.Animals.ToListAsync();
            Assert.Equal(2, allAnimals.Count);
        }
        finally
        {
            await CleanupAsync(context, "Phase5_TphAnimals");
        }
    }

    /// <summary>
    /// OwnsOne same-table CRUD round-trip.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Owns_one_same_table_crud_on_mysql84()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        await using var context = new OwnedCrudContext(CreateOptions<OwnedCrudContext>(connectionString));

        await CleanupAsync(context, "Phase5_OwnedCustomers");

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS `Phase5_OwnedCustomers` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `Name` varchar(100) NOT NULL,
                `Address_Street` varchar(200) NULL,
                `Address_City` varchar(100) NULL,
                CONSTRAINT `PK_Phase5_OwnedCustomers` PRIMARY KEY (`Id`)
            ) CHARACTER SET utf8mb4;
            """);

        try
        {
            context.Customers.Add(
                new OwnedCustomer
                {
                    Name = "Alice",
                    Address = new OwnedAddress
                    {
                        Street = "123 Main",
                        City = "Berlin"
                    },
                });
            await context.SaveChangesAsync();

            var customer = await context.Customers.FirstAsync();
            Assert.Equal("Alice", customer.Name);
            Assert.NotNull(customer.Address);
            Assert.Equal("Berlin", customer.Address!.City);
        }
        finally
        {
            await CleanupAsync(context, "Phase5_OwnedCustomers");
        }
    }

    /// <summary>
    /// Many-to-many CRUD via skip navigation.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Many_to_many_crud_on_mysql84()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        await using var context = new M2MCrudContext(CreateOptions<M2MCrudContext>(connectionString));

        await CleanupAsync(context, "M2MCourseM2MStudent", "Phase5_M2MStudents", "Phase5_M2MCourses");

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS `Phase5_M2MStudents` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `Name` varchar(100) NOT NULL,
                CONSTRAINT `PK_Phase5_M2MStudents` PRIMARY KEY (`Id`)
            ) CHARACTER SET utf8mb4;
            """);
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS `Phase5_M2MCourses` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `Title` varchar(200) NOT NULL,
                CONSTRAINT `PK_Phase5_M2MCourses` PRIMARY KEY (`Id`)
            ) CHARACTER SET utf8mb4;
            """);
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS `M2MCourseM2MStudent` (
                `CoursesId` int NOT NULL,
                `StudentsId` int NOT NULL,
                CONSTRAINT `PK_M2MCourseM2MStudent` PRIMARY KEY (`CoursesId`, `StudentsId`),
                CONSTRAINT `FK_M2MCourseM2MStudent_Courses` FOREIGN KEY (`CoursesId`) REFERENCES `Phase5_M2MCourses` (`Id`) ON DELETE CASCADE,
                CONSTRAINT `FK_M2MCourseM2MStudent_Students` FOREIGN KEY (`StudentsId`) REFERENCES `Phase5_M2MStudents` (`Id`) ON DELETE CASCADE
            ) CHARACTER SET utf8mb4;
            """);

        try
        {
            var student = new M2MStudent { Name = "Alice" };
            var course = new M2MCourse { Title = "Databases" };
            student.Courses.Add(course);
            context.Students.Add(student);
            await context.SaveChangesAsync();

            var loaded = await context
                .Students.Include(s => s.Courses)
                .FirstAsync();
            Assert.Single(loaded.Courses);
            Assert.Equal("Databases", loaded.Courses[0].Title);
        }
        finally
        {
            await CleanupAsync(context, "M2MCourseM2MStudent", "Phase5_M2MStudents", "Phase5_M2MCourses");
        }
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Tpt_insert_creates_rows_in_both_tables()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        await using var context = new TptCrudContext(CreateOptions<TptCrudContext>(connectionString));

        await CleanupAsync(context, "Phase5_TptCars", "Phase5_TptVehicles");

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS `Phase5_TptVehicles` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `Make` varchar(100) NOT NULL,
                CONSTRAINT `PK_TptVehicles` PRIMARY KEY (`Id`)
            ) CHARACTER SET utf8mb4;
            """);
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS `Phase5_TptCars` (
                `Id` int NOT NULL,
                `SeatCount` int NOT NULL,
                CONSTRAINT `PK_TptCars` PRIMARY KEY (`Id`),
                CONSTRAINT `FK_TptCars` FOREIGN KEY (`Id`) REFERENCES `Phase5_TptVehicles` (`Id`) ON DELETE CASCADE
            ) CHARACTER SET utf8mb4;
            """);

        try
        {
            context.Vehicles.Add(
                new TptCar
                {
                    Make = "BMW",
                    SeatCount = 5
                });
            await context.SaveChangesAsync();

            var vehicle = await context.Vehicles.FirstAsync();
            Assert.Equal("BMW", vehicle.Make);

            var car = await context
                .Vehicles.OfType<TptCar>()
                .FirstAsync();
            Assert.Equal(5, car.SeatCount);
        }
        finally
        {
            await CleanupAsync(context, "Phase5_TptCars", "Phase5_TptVehicles");
        }
    }

    /// <summary>
    /// Concurrency check -- UPDATE with original value in WHERE.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Concurrency_check_detects_conflict()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        await using var context1 = new ConcurrencyCrudContext(CreateOptions<ConcurrencyCrudContext>(connectionString));

        await CleanupAsync(context1, "Phase5_ConcurrencyEntities");

        await context1.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS `Phase5_ConcurrencyEntities` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `Name` varchar(100) NOT NULL,
                `Version` int NOT NULL DEFAULT 0,
                CONSTRAINT `PK_ConcurrencyEntities` PRIMARY KEY (`Id`)
            ) CHARACTER SET utf8mb4;
            """);

        try
        {
            context1.Items.Add(
                new ConcurrencyEntity
                {
                    Name = "Original",
                    Version = 1
                });
            await context1.SaveChangesAsync();

            // Simulate concurrent modification.
            await context1.Database.ExecuteSqlRawAsync(
                "UPDATE `Phase5_ConcurrencyEntities` SET `Version` = 99 WHERE `Id` = 1;");

            var entity = await context1.Items.FirstAsync();
            entity.Name = "Modified";

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context1.SaveChangesAsync());
        }
        finally
        {
            await CleanupAsync(context1, "Phase5_ConcurrencyEntities");
        }
    }

    /// <summary>
    /// Bool->string ValueConverter round-trip.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Bool_to_string_converter_roundtrip()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        await using var context = new ConverterCrudContext(CreateOptions<ConverterCrudContext>(connectionString));

        await CleanupAsync(context, "Phase5_ConverterEntities");

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS `Phase5_ConverterEntities` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `IsActive` varchar(1) NOT NULL,
                `Price` decimal(18,2) NOT NULL,
                CONSTRAINT `PK_ConverterEntities` PRIMARY KEY (`Id`)
            ) CHARACTER SET utf8mb4;
            """);

        try
        {
            context.Items.Add(
                new ConverterEntity
                {
                    IsActive = true,
                    Price = new Money(42.50m)
                });
            await context.SaveChangesAsync();

            // Clear tracker to force fresh read.
            context.ChangeTracker.Clear();

            var loaded = await context.Items.FirstAsync();
            Assert.True(loaded.IsActive);
            Assert.Equal(42.50m, loaded.Price.Amount);
        }
        finally
        {
            await CleanupAsync(context, "Phase5_ConverterEntities");
        }
    }

    // -- TPT entities ----------------------------------------------------

    private abstract class TptVehicle
    {
        public int Id { get; set; }
        public string Make { get; set; } = "";
    }

    private sealed class TptCar : TptVehicle
    {
        public int SeatCount { get; set; }
    }

    private sealed class TptCrudContext : DbContext
    {
        public TptCrudContext(
            DbContextOptions<TptCrudContext> options
        ) : base(options) { }

        public DbSet<TptVehicle> Vehicles => Set<TptVehicle>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<TptVehicle>()
                .UseTptMappingStrategy();
            modelBuilder.Entity<TptVehicle>(e =>
            {
                e.ToTable("Phase5_TptVehicles");
                e
                    .Property(v => v.Make)
                    .HasMaxLength(100);
            });
            modelBuilder
                .Entity<TptCar>()
                .ToTable("Phase5_TptCars");
        }
    }

    // -- Concurrency entities --------------------------------------------

    private sealed class ConcurrencyEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        [ConcurrencyCheck]
        public int Version { get; set; }
    }

    private sealed class ConcurrencyCrudContext : DbContext
    {
        public ConcurrencyCrudContext(
            DbContextOptions<ConcurrencyCrudContext> options
        ) : base(options) { }

        public DbSet<ConcurrencyEntity> Items => Set<ConcurrencyEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ConcurrencyEntity>(e =>
            {
                e.ToTable("Phase5_ConcurrencyEntities");
                e
                    .Property(c => c.Name)
                    .HasMaxLength(100);
            });
        }
    }

    // -- Converter entities ----------------------------------------------

    private readonly record struct Money(decimal Amount);

    private sealed class ConverterEntity
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public Money Price { get; set; }
    }

    private sealed class ConverterCrudContext : DbContext
    {
        public ConverterCrudContext(
            DbContextOptions<ConverterCrudContext> options
        ) : base(options) { }

        public DbSet<ConverterEntity> Items => Set<ConverterEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ConverterEntity>(e =>
            {
                e.ToTable("Phase5_ConverterEntities");
                e
                    .Property(c => c.IsActive)
                    .HasConversion(v => v ? "Y" : "N", v => v == "Y")
                    .HasMaxLength(1);
                e
                    .Property(c => c.Price)
                    .HasConversion(v => v.Amount, v => new Money(v))
                    .HasPrecision(18, 2);
            });
        }
    }

    // -- Helpers ----------------------------------------------------------

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        string connectionString
    )
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        builder.UseMySql(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return builder.Options;
    }

    private static async Task CleanupAsync(
        DbContext context,
        params string[] tables
    )
    {
        foreach (var table in tables)
        {
            // Escape any backticks defensively so the helper is safe-by-construction
            // even if a future caller passes a non-literal identifier.
            var quoted = table.Replace("`", "``", StringComparison.Ordinal);
            await context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{quoted}`;");
        }
    }

    // -- Entities ---------------------------------------------------------

    private abstract class TphAnimal
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class TphDog : TphAnimal
    {
        public string Breed { get; set; } = "";
    }

    private sealed class TphCat : TphAnimal
    {
        public bool IsIndoor { get; set; }
    }

    private sealed class TphCrudContext : DbContext
    {
        public TphCrudContext(
            DbContextOptions<TphCrudContext> options
        ) : base(options) { }

        public DbSet<TphAnimal> Animals => Set<TphAnimal>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TphAnimal>(e =>
            {
                e.ToTable("Phase5_TphAnimals");
                e
                    .HasDiscriminator<string>("Type")
                    .HasValue<TphDog>("Dog")
                    .HasValue<TphCat>("Cat");
                e
                    .Property("Type")
                    .HasMaxLength(64);
                e
                    .Property(a => a.Name)
                    .HasMaxLength(100);
            });
            modelBuilder.Entity<TphDog>(e => e
                .Property(d => d.Breed)
                .HasMaxLength(100));
        }
    }

    private sealed class OwnedCustomer
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public OwnedAddress? Address { get; set; }
    }

    private sealed class OwnedAddress
    {
        public string Street { get; set; } = "";
        public string City { get; set; } = "";
    }

    private sealed class OwnedCrudContext : DbContext
    {
        public OwnedCrudContext(
            DbContextOptions<OwnedCrudContext> options
        ) : base(options) { }

        public DbSet<OwnedCustomer> Customers => Set<OwnedCustomer>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<OwnedCustomer>(e =>
            {
                e.ToTable("Phase5_OwnedCustomers");
                e
                    .Property(c => c.Name)
                    .HasMaxLength(100);
                e.OwnsOne(
                    c => c.Address,
                    a =>
                    {
                        a
                            .Property(x => x.Street)
                            .HasMaxLength(200);
                        a
                            .Property(x => x.City)
                            .HasMaxLength(100);
                    });
            });
        }
    }

    private sealed class M2MStudent
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public List<M2MCourse> Courses { get; set; } = [];
    }

    private sealed class M2MCourse
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public List<M2MStudent> Students { get; set; } = [];
    }

    private sealed class M2MCrudContext : DbContext
    {
        public M2MCrudContext(
            DbContextOptions<M2MCrudContext> options
        ) : base(options) { }

        public DbSet<M2MStudent> Students => Set<M2MStudent>();
        public DbSet<M2MCourse> Courses => Set<M2MCourse>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<M2MStudent>(e =>
            {
                e.ToTable("Phase5_M2MStudents");
                e
                    .Property(s => s.Name)
                    .HasMaxLength(100);
            });
            modelBuilder.Entity<M2MCourse>(e =>
            {
                e.ToTable("Phase5_M2MCourses");
                e
                    .Property(c => c.Title)
                    .HasMaxLength(200);
            });
            modelBuilder
                .Entity<M2MStudent>()
                .HasMany(s => s.Courses)
                .WithMany(c => c.Students);
        }
    }
}
