using System.ComponentModel.DataAnnotations;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// MariaDB 11.8 parity tests for EF Core modeling features.
/// Mirrors MySqlModelingIntegrationTests to ensure engine parity.
/// </summary>
public sealed class MariaDbModelingIntegrationTests
{
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Tph_crud_roundtrip_on_mariadb118()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        await using var context = new TphContext(CreateOptions<TphContext>(connectionString));
        await CleanupAsync(context, "MdbTphAnimals");

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS `MdbTphAnimals` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `Type` varchar(64) NOT NULL,
                `Name` varchar(100) NOT NULL,
                `Breed` varchar(100) NULL,
                `IsIndoor` tinyint(1) NULL,
                CONSTRAINT `PK_MdbTphAnimals` PRIMARY KEY (`Id`)
            ) CHARACTER SET utf8mb4;
            """);

        try
        {
            context.Animals.Add(new TphDog { Name = "Rex", Breed = "Shepherd" });
            context.Animals.Add(new TphCat { Name = "Mimi", IsIndoor = true });
            await context.SaveChangesAsync();

            var dogs = await context.Animals.OfType<TphDog>().ToListAsync();
            Assert.Single(dogs);
            Assert.Equal("Shepherd", dogs[0].Breed);
            Assert.Equal(2, await context.Animals.CountAsync());
        }
        finally
        {
            await CleanupAsync(context, "MdbTphAnimals");
        }
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Tpt_insert_on_mariadb118()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        await using var context = new TptContext(CreateOptions<TptContext>(connectionString));
        await CleanupAsync(context, "MdbTptCars", "MdbTptVehicles");

        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS `MdbTptVehicles` (`Id` int NOT NULL AUTO_INCREMENT, `Make` varchar(100) NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS `MdbTptCars` (`Id` int NOT NULL, `SeatCount` int NOT NULL, PRIMARY KEY (`Id`), FOREIGN KEY (`Id`) REFERENCES `MdbTptVehicles` (`Id`) ON DELETE CASCADE) CHARACTER SET utf8mb4;");

        try
        {
            context.Vehicles.Add(new TptCar { Make = "Audi", SeatCount = 4 });
            await context.SaveChangesAsync();
            var car = await context.Vehicles.OfType<TptCar>().FirstAsync();
            Assert.Equal("Audi", car.Make);
            Assert.Equal(4, car.SeatCount);
        }
        finally
        {
            await CleanupAsync(context, "MdbTptCars", "MdbTptVehicles");
        }
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Owns_one_on_mariadb118()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        await using var context = new OwnedContext(CreateOptions<OwnedContext>(connectionString));
        await CleanupAsync(context, "MdbOwnedCustomers");

        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS `MdbOwnedCustomers` (`Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(100) NOT NULL, `Address_Street` varchar(200) NULL, `Address_City` varchar(100) NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            context.Customers.Add(
                new OwnedCustomer
                {
                    Name = "Bob",
                    Address = new OwnedAddress
                    {
                        Street = "Hauptstr. 1",
                        City = "Munich",
                    },
                });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var c = await context.Customers.FirstAsync();
            Assert.Equal("Munich", c.Address!.City);
        }
        finally
        {
            await CleanupAsync(context, "MdbOwnedCustomers");
        }
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Many_to_many_on_mariadb118()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        await using var context = new M2MContext(CreateOptions<M2MContext>(connectionString));
        await CleanupAsync(context, "MdbM2MCourseStudent", "MdbStudents", "MdbCourses");

        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS `MdbStudents` (`Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(100) NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS `MdbCourses` (`Id` int NOT NULL AUTO_INCREMENT, `Title` varchar(200) NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS `MdbM2MCourseStudent` (`CoursesId` int NOT NULL, `StudentsId` int NOT NULL, PRIMARY KEY (`CoursesId`, `StudentsId`), FOREIGN KEY (`CoursesId`) REFERENCES `MdbCourses` (`Id`) ON DELETE CASCADE, FOREIGN KEY (`StudentsId`) REFERENCES `MdbStudents` (`Id`) ON DELETE CASCADE) CHARACTER SET utf8mb4;");

        try
        {
            var s = new M2MStudent { Name = "Eve" };
            s.Courses.Add(new M2MCourse { Title = "Math" });
            context.Students.Add(s);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var loaded = await context
                .Students.Include(x => x.Courses)
                .FirstAsync();
            Assert.Single(loaded.Courses);
        }
        finally
        {
            await CleanupAsync(context, "MdbM2MCourseStudent", "MdbStudents", "MdbCourses");
        }
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Concurrency_check_on_mariadb118()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        await using var context = new ConcurrencyContext(CreateOptions<ConcurrencyContext>(connectionString));
        await CleanupAsync(context, "MdbConcurrency");

        await context.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS `MdbConcurrency` (`Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(100) NOT NULL, `Version` int NOT NULL DEFAULT 0, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            context.Items.Add(new ConcurrencyItem { Name = "Original", Version = 1 });
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlRawAsync("UPDATE `MdbConcurrency` SET `Version` = 99 WHERE `Id` = 1;");
            var entity = await context.Items.FirstAsync();
            entity.Name = "Modified";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
        }
        finally
        {
            await CleanupAsync(context, "MdbConcurrency");
        }
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Value_converter_roundtrip_on_mariadb118()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        await using var context = new ConverterContext(CreateOptions<ConverterContext>(connectionString));
        await CleanupAsync(context, "MdbConverter");

        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS `MdbConverter` (`Id` int NOT NULL AUTO_INCREMENT, `IsActive` varchar(1) NOT NULL, `Price` decimal(18,2) NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            context.Items.Add(
                new ConverterItem
                {
                    IsActive = true,
                    Price = 99.95m
                });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var loaded = await context.Items.FirstAsync();
            Assert.True(loaded.IsActive);
            Assert.Equal(99.95m, loaded.Price);
        }
        finally
        {
            await CleanupAsync(context, "MdbConverter");
        }
    }

    // -- Helpers --

    private static DbContextOptions<T> CreateOptions<T>(
        string cs
    )
        where T : DbContext
    {
        var b = new DbContextOptionsBuilder<T>();
        b.UseMySql(cs, MySqlServerVersion.MariaDb(new Version(11, 8, 0)));
        return b.Options;
    }

    private static async Task CleanupAsync(
        DbContext ctx,
        params string[] tables
    )
    {
        foreach (var t in tables)
        {
            // Escape any backticks defensively so the helper is safe-by-construction
            // even if a future caller passes a non-literal identifier.
            var quoted = t.Replace("`", "``", StringComparison.Ordinal);
            await ctx.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{quoted}`;");
        }
    }

    // -- Entities --

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

    private sealed class TphContext : DbContext
    {
        public TphContext(
            DbContextOptions<TphContext> o
        ) : base(o) { }

        public DbSet<TphAnimal> Animals => Set<TphAnimal>();

        protected override void OnModelCreating(
            ModelBuilder m
        )
        {
            m.Entity<TphAnimal>(e =>
            {
                e.ToTable("MdbTphAnimals");
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
            m.Entity<TphDog>(e => e
                .Property(d => d.Breed)
                .HasMaxLength(100));
        }
    }

    private abstract class TptVehicle
    {
        public int Id { get; set; }
        public string Make { get; set; } = "";
    }

    private sealed class TptCar : TptVehicle
    {
        public int SeatCount { get; set; }
    }

    private sealed class TptContext : DbContext
    {
        public TptContext(
            DbContextOptions<TptContext> o
        ) : base(o) { }

        public DbSet<TptVehicle> Vehicles => Set<TptVehicle>();

        protected override void OnModelCreating(
            ModelBuilder m
        )
        {
            m
                .Entity<TptVehicle>()
                .UseTptMappingStrategy();
            m.Entity<TptVehicle>(e =>
            {
                e.ToTable("MdbTptVehicles");
                e
                    .Property(v => v.Make)
                    .HasMaxLength(100);
            });
            m
                .Entity<TptCar>()
                .ToTable("MdbTptCars");
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

    private sealed class OwnedContext : DbContext
    {
        public OwnedContext(
            DbContextOptions<OwnedContext> o
        ) : base(o) { }

        public DbSet<OwnedCustomer> Customers => Set<OwnedCustomer>();

        protected override void OnModelCreating(
            ModelBuilder m
        )
        {
            m.Entity<OwnedCustomer>(e =>
            {
                e.ToTable("MdbOwnedCustomers");
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

    private sealed class M2MContext : DbContext
    {
        public M2MContext(
            DbContextOptions<M2MContext> o
        ) : base(o) { }

        public DbSet<M2MStudent> Students => Set<M2MStudent>();

        protected override void OnModelCreating(
            ModelBuilder m
        )
        {
            m.Entity<M2MStudent>(e =>
            {
                e.ToTable("MdbStudents");
                e
                    .Property(s => s.Name)
                    .HasMaxLength(100);
            });
            m.Entity<M2MCourse>(e =>
            {
                e.ToTable("MdbCourses");
                e
                    .Property(c => c.Title)
                    .HasMaxLength(200);
            });
            m
                .Entity<M2MStudent>()
                .HasMany(s => s.Courses)
                .WithMany(c => c.Students)
                .UsingEntity(j => j.ToTable("MdbM2MCourseStudent"));
        }
    }

    private sealed class ConcurrencyItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        [ConcurrencyCheck]
        public int Version { get; set; }
    }

    private sealed class ConcurrencyContext : DbContext
    {
        public ConcurrencyContext(
            DbContextOptions<ConcurrencyContext> o
        ) : base(o) { }

        public DbSet<ConcurrencyItem> Items => Set<ConcurrencyItem>();

        protected override void OnModelCreating(
            ModelBuilder m
        )
        {
            m.Entity<ConcurrencyItem>(e =>
            {
                e.ToTable("MdbConcurrency");
                e
                    .Property(c => c.Name)
                    .HasMaxLength(100);
            });
        }
    }

    private sealed class ConverterItem
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public decimal Price { get; set; }
    }

    private sealed class ConverterContext : DbContext
    {
        public ConverterContext(
            DbContextOptions<ConverterContext> o
        ) : base(o) { }

        public DbSet<ConverterItem> Items => Set<ConverterItem>();

        protected override void OnModelCreating(
            ModelBuilder m
        )
        {
            m.Entity<ConverterItem>(e =>
            {
                e.ToTable("MdbConverter");
                e
                    .Property(c => c.IsActive)
                    .HasConversion(v => v ? "Y" : "N", v => v == "Y")
                    .HasMaxLength(1);
                e
                    .Property(c => c.Price)
                    .HasPrecision(18, 2);
            });
        }
    }
}
