using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

/// <summary>
/// Northwind-query fixture parameterized on the standard EF Core <see cref="ITestModelCustomizer"/>
/// surface. Routes the spec base class to <see cref="MySqlNorthwindTestStoreFactory"/>, turns on
/// detailed-errors, and applies MySQL-specific column-type and max-length overrides to the
/// upstream Northwind model so the provider's <c>MySqlModelValidator</c> accepts the schema.
/// </summary>
public class NorthwindQueryMySqlFixture<TModelCustomizer> : NorthwindQueryRelationalFixture<TModelCustomizer>
    where TModelCustomizer : ITestModelCustomizer, new()
{
    private const string StoreCategoryIdPropertyName = "StoreCategoryId";

    protected override ITestStoreFactory TestStoreFactory => MySqlNorthwindTestStoreFactory.Instance;

    protected override bool RecreateStore => true;

    public override DbContextOptionsBuilder AddOptions(
        DbContextOptionsBuilder builder
    ) => base
        .AddOptions(builder)
        .ConfigureWarnings(warnings => warnings.Log(RelationalEventId.MultipleCollectionIncludeWarning))
        .EnableDetailedErrors();

    protected override bool ShouldLogCategory(
        string logCategory
    ) => logCategory == DbLoggerCategory.Query.Name
        || logCategory == DbLoggerCategory.Database.Command.Name;

    protected override void OnModelCreating(
        ModelBuilder modelBuilder,
        DbContext context
    )
    {
        base.OnModelCreating(modelBuilder, context);

        // The upstream Northwind model is engine-agnostic and leaves text-key / text-FK / index
        // properties without an explicit max length. MySqlModelValidator rejects that shape per
        // the documented contract (keyed / indexed text properties must declare HasMaxLength) so
        // every spec-test would otherwise fail at model-build time. The values mirror the SqlServer
        // fixture's shapes: 5-char customer + employee + territory ids, 30-40 char name fields.
        modelBuilder.Entity<Customer>(b =>
        {
            b.Property(c => c.CustomerID).HasMaxLength(5);
            b.Property(c => c.CompanyName).HasMaxLength(40);
            b.Property(c => c.ContactName).HasMaxLength(30);
            b.Property(c => c.ContactTitle).HasMaxLength(30);
            b.Property(c => c.City).HasMaxLength(15);
            b.Property(c => c.Country).HasMaxLength(15);
            b.Property(c => c.Fax).HasMaxLength(24);
            b.Property(c => c.Phone).HasMaxLength(24);
            b.Property(c => c.PostalCode).HasMaxLength(10);
            b.Property(c => c.Region).HasMaxLength(15);
            b.Property(c => c.Address).HasMaxLength(60);
        });

        modelBuilder.Entity<Order>(b =>
        {
            b.Property(o => o.CustomerID).HasMaxLength(5);
            b.Property(o => o.OrderDate).HasColumnType("datetime");
        });

        modelBuilder.Entity<Employee>(b =>
        {
            b.Property(e => e.FirstName).HasMaxLength(10);
            b.Property(e => e.City).HasMaxLength(15);
            b.Property(e => e.Country).HasMaxLength(15);
        });

        modelBuilder.Entity<Product>(b =>
        {
            // CategoryID is intentionally ignored by the official query model. A shadow
            // property keeps the physical Northwind column available to the ProductView
            // contract without making the CLR property queryable by the provider.
            b.Property<int?>(StoreCategoryIdPropertyName)
                .HasColumnName("CategoryID");
            b.Property(p => p.ProductName).HasMaxLength(40);
            b.Property(p => p.UnitPrice).HasColumnType("decimal(19,4)");
        });

        modelBuilder.Entity<OrderDetail>()
            .Property(od => od.UnitPrice)
            .HasColumnType("decimal(19,4)");

        modelBuilder.Entity<MostExpensiveProduct>(b =>
        {
            b.HasKey(product => product.TenMostExpensiveProducts);
            b.Property(p => p.TenMostExpensiveProducts).HasMaxLength(40);
            b.Property(p => p.UnitPrice).HasColumnType("decimal(19,4)");
        });

        modelBuilder.Entity<CustomerOrderHistory>(b =>
        {
            b.HasKey(history => history.ProductName);
            b.Property(history => history.ProductName).HasMaxLength(40);
        });

        // CustomerQuery + OrderQuery are query-types whose schema mirrors the underlying
        // Customers / Orders tables. ToSqlQuery scopes the read path; the same text-property
        // max-length contract still applies because the base fixture registers these types
        // with the same CustomerID surface as their keyed siblings.
        modelBuilder.Entity<CustomerQuery>(b =>
        {
            b.ToSqlQuery(
                "SELECT `c`.`Address`, `c`.`City`, `c`.`CompanyName`, "
                + "`c`.`ContactName`, `c`.`ContactTitle` FROM `Customers` AS `c`");
            b.Property(c => c.CompanyName).HasMaxLength(40);
            b.Property(c => c.ContactName).HasMaxLength(30);
            b.Property(c => c.ContactTitle).HasMaxLength(30);
            b.Property(c => c.City).HasMaxLength(15);
            b.Property(c => c.Address).HasMaxLength(60);
        });

        modelBuilder.Entity<OrderQuery>(b =>
        {
            b.ToSqlQuery("SELECT * FROM `Orders`");
            b.Property(o => o.CustomerID).HasMaxLength(5);
        });

        modelBuilder.Entity<ProductView>()
            .ToView("Alphabetical list of products");

        modelBuilder.Entity<CustomerQueryWithQueryFilter>()
            .ToSqlQuery(
                """
                SELECT `c`.`CompanyName`, COUNT(`o`.`OrderID`) AS `OrderCount`, 'A' AS `SearchTerm`
                FROM `Customers` AS `c`
                LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
                GROUP BY `c`.`CustomerID`, `c`.`CompanyName`
                """);
    }

    protected override async Task SeedAsync(
        NorthwindContext context
    )
    {
        await base.SeedAsync(context);

        foreach (var productEntry in context.ChangeTracker.Entries<Product>())
        {
            productEntry.Property(StoreCategoryIdPropertyName).CurrentValue =
                productEntry.Entity.CategoryID;
        }

        await context.SaveChangesAsync();

        await CreateRawSqlCompatibilitySurfaceAsync(context);

        // EnsureCreated deliberately does not create objects mapped with ToView. Build the
        // Northwind projection after seeding so the database-view contract exercises an actual
        // server view instead of an empty keyless table.
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE VIEW `Alphabetical list of products` AS
            SELECT
                `ProductID`,
                `ProductName`,
                CASE `CategoryID`
                    WHEN 1 THEN 'Beverages'
                    WHEN 2 THEN 'Condiments'
                    WHEN 3 THEN 'Confections'
                    WHEN 4 THEN 'Dairy Products'
                    WHEN 5 THEN 'Grains/Cereals'
                    WHEN 6 THEN 'Meat/Poultry'
                    WHEN 7 THEN 'Produce'
                    WHEN 8 THEN 'Seafood'
                END AS `CategoryName`
            FROM `Products`
            WHERE NOT `Discontinued`;
            """);

        await context.Database.ExecuteSqlRawAsync(
            "DROP PROCEDURE IF EXISTS `Ten Most Expensive Products`");
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE PROCEDURE `Ten Most Expensive Products`()
            SELECT
                `ProductName` AS `TenMostExpensiveProducts`,
                `UnitPrice`
            FROM `Products`
            ORDER BY `UnitPrice` DESC
            LIMIT 10
            """);

        await context.Database.ExecuteSqlRawAsync(
            "DROP PROCEDURE IF EXISTS `CustOrderHist`");
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE PROCEDURE `CustOrderHist`(IN `requestedCustomerId` varchar(5))
            SELECT
                `p`.`ProductName`,
                CAST(SUM(`od`.`Quantity`) AS SIGNED) AS `Total`
            FROM `Products` AS `p`
            INNER JOIN `OrderDetails` AS `od`
                ON `od`.`ProductID` = `p`.`ProductID`
            INNER JOIN `Orders` AS `o`
                ON `o`.`OrderID` = `od`.`OrderID`
            WHERE `o`.`CustomerID` COLLATE utf8mb4_unicode_ci = `requestedCustomerId`
            GROUP BY `p`.`ProductName`
            """);
    }

    /// <summary>
    /// Restores the physical Northwind columns used by the official raw-SQL contracts.
    /// </summary>
    /// <remarks>
    /// The upstream query model deliberately ignores properties which are irrelevant to most
    /// LINQ scenarios. SQL Server runs the suite against a pre-existing full Northwind schema,
    /// whereas this fixture uses <c>EnsureCreated</c>, which only creates mapped properties.
    /// Keeping the compatibility columns physical-only preserves the official raw-SQL surface
    /// without changing the provider model or every generated query projection.
    /// </remarks>
    private static async Task CreateRawSqlCompatibilitySurfaceAsync(
        NorthwindContext context
    )
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE `Products`
                ADD COLUMN `QuantityPerUnit` varchar(20) NULL,
                ADD COLUMN `UnitsOnOrder` smallint unsigned NULL,
                ADD COLUMN `ReorderLevel` smallint unsigned NULL
            """);

        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE `Orders`
                ADD COLUMN `RequiredDate` datetime NULL,
                ADD COLUMN `ShippedDate` datetime NULL,
                ADD COLUMN `ShipVia` int NULL,
                ADD COLUMN `Freight` decimal(18,3) NULL,
                ADD COLUMN `ShipName` varchar(40) NULL,
                ADD COLUMN `ShipAddress` varchar(60) NULL,
                ADD COLUMN `ShipCity` varchar(15) NULL,
                ADD COLUMN `ShipRegion` varchar(15) NULL,
                ADD COLUMN `ShipPostalCode` varchar(10) NULL,
                ADD COLUMN `ShipCountry` varchar(15) NULL
            """);

        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE `Employees`
                ADD COLUMN `LastName` varchar(20) NULL,
                ADD COLUMN `TitleOfCourtesy` varchar(25) NULL,
                ADD COLUMN `BirthDate` datetime NULL,
                ADD COLUMN `HireDate` datetime NULL,
                ADD COLUMN `Address` varchar(60) NULL,
                ADD COLUMN `Region` varchar(15) NULL,
                ADD COLUMN `PostalCode` varchar(10) NULL,
                ADD COLUMN `HomePhone` varchar(24) NULL,
                ADD COLUMN `Extension` varchar(4) NULL,
                ADD COLUMN `Photo` longblob NULL,
                ADD COLUMN `Notes` longtext NULL,
                ADD COLUMN `PhotoPath` varchar(255) NULL
            """);

        await context.Database.OpenConnectionAsync();

        try
        {
            var connection = (MySqlConnection)context.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync();

            await UpdateProductCompatibilityColumnsAsync(
                connection,
                transaction,
                context.ChangeTracker.Entries<Product>().Select(entry => entry.Entity));
            await UpdateOrderCompatibilityColumnsAsync(
                connection,
                transaction,
                context.ChangeTracker.Entries<Order>().Select(entry => entry.Entity));
            await UpdateEmployeeCompatibilityColumnsAsync(
                connection,
                transaction,
                context.ChangeTracker.Entries<Employee>().Select(entry => entry.Entity));

            await transaction.CommitAsync();
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static async Task UpdateProductCompatibilityColumnsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<Product> products
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE `Products`
            SET `QuantityPerUnit` = @quantityPerUnit,
                `UnitsOnOrder` = @unitsOnOrder,
                `ReorderLevel` = @reorderLevel
            WHERE `ProductID` = @productId
            """;

        var quantityPerUnit = command.Parameters.Add(
            "@quantityPerUnit",
            MySqlDbType.VarChar,
            20);
        var unitsOnOrder = command.Parameters.Add("@unitsOnOrder", MySqlDbType.UInt16);
        var reorderLevel = command.Parameters.Add("@reorderLevel", MySqlDbType.UInt16);
        var productId = command.Parameters.Add("@productId", MySqlDbType.Int32);

        await command.PrepareAsync();

        foreach (var product in products)
        {
            quantityPerUnit.Value = (object?)product.QuantityPerUnit ?? DBNull.Value;
            unitsOnOrder.Value = (object?)product.UnitsOnOrder ?? DBNull.Value;
            reorderLevel.Value = (object?)product.ReorderLevel ?? DBNull.Value;
            productId.Value = product.ProductID;

            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task UpdateOrderCompatibilityColumnsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<Order> orders
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE `Orders`
            SET `RequiredDate` = @requiredDate,
                `ShippedDate` = @shippedDate,
                `ShipVia` = @shipVia,
                `Freight` = @freight,
                `ShipName` = @shipName,
                `ShipAddress` = @shipAddress,
                `ShipCity` = @shipCity,
                `ShipRegion` = @shipRegion,
                `ShipPostalCode` = @shipPostalCode,
                `ShipCountry` = @shipCountry
            WHERE `OrderID` = @orderId
            """;

        var requiredDate = command.Parameters.Add("@requiredDate", MySqlDbType.DateTime);
        var shippedDate = command.Parameters.Add("@shippedDate", MySqlDbType.DateTime);
        var shipVia = command.Parameters.Add("@shipVia", MySqlDbType.Int32);
        var freight = command.Parameters.Add("@freight", MySqlDbType.Decimal);
        var shipName = command.Parameters.Add("@shipName", MySqlDbType.VarChar, 40);
        var shipAddress = command.Parameters.Add("@shipAddress", MySqlDbType.VarChar, 60);
        var shipCity = command.Parameters.Add("@shipCity", MySqlDbType.VarChar, 15);
        var shipRegion = command.Parameters.Add("@shipRegion", MySqlDbType.VarChar, 15);
        var shipPostalCode = command.Parameters.Add("@shipPostalCode", MySqlDbType.VarChar, 10);
        var shipCountry = command.Parameters.Add("@shipCountry", MySqlDbType.VarChar, 15);
        var orderId = command.Parameters.Add("@orderId", MySqlDbType.Int32);

        freight.Precision = 18;
        freight.Scale = 3;

        await command.PrepareAsync();

        foreach (var order in orders)
        {
            requiredDate.Value = (object?)order.RequiredDate ?? DBNull.Value;
            shippedDate.Value = (object?)order.ShippedDate ?? DBNull.Value;
            shipVia.Value = (object?)order.ShipVia ?? DBNull.Value;
            freight.Value = (object?)order.Freight ?? DBNull.Value;
            shipName.Value = (object?)order.ShipName ?? DBNull.Value;
            shipAddress.Value = (object?)order.ShipAddress ?? DBNull.Value;
            shipCity.Value = (object?)order.ShipCity ?? DBNull.Value;
            shipRegion.Value = (object?)order.ShipRegion ?? DBNull.Value;
            shipPostalCode.Value = (object?)order.ShipPostalCode ?? DBNull.Value;
            shipCountry.Value = (object?)order.ShipCountry ?? DBNull.Value;
            orderId.Value = order.OrderID;

            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task UpdateEmployeeCompatibilityColumnsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<Employee> employees
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE `Employees`
            SET `LastName` = @lastName,
                `TitleOfCourtesy` = @titleOfCourtesy,
                `BirthDate` = @birthDate,
                `HireDate` = @hireDate,
                `Address` = @address,
                `Region` = @region,
                `PostalCode` = @postalCode,
                `HomePhone` = @homePhone,
                `Extension` = @extension,
                `Photo` = @photo,
                `Notes` = @notes,
                `PhotoPath` = @photoPath
            WHERE `EmployeeID` = @employeeId
            """;

        var lastName = command.Parameters.Add("@lastName", MySqlDbType.VarChar, 20);
        var titleOfCourtesy = command.Parameters.Add(
            "@titleOfCourtesy",
            MySqlDbType.VarChar,
            25);
        var birthDate = command.Parameters.Add("@birthDate", MySqlDbType.DateTime);
        var hireDate = command.Parameters.Add("@hireDate", MySqlDbType.DateTime);
        var address = command.Parameters.Add("@address", MySqlDbType.VarChar, 60);
        var region = command.Parameters.Add("@region", MySqlDbType.VarChar, 15);
        var postalCode = command.Parameters.Add("@postalCode", MySqlDbType.VarChar, 10);
        var homePhone = command.Parameters.Add("@homePhone", MySqlDbType.VarChar, 24);
        var extension = command.Parameters.Add("@extension", MySqlDbType.VarChar, 4);
        var photo = command.Parameters.Add("@photo", MySqlDbType.LongBlob);
        var notes = command.Parameters.Add("@notes", MySqlDbType.LongText);
        var photoPath = command.Parameters.Add("@photoPath", MySqlDbType.VarChar, 255);
        var employeeId = command.Parameters.Add("@employeeId", MySqlDbType.UInt32);

        await command.PrepareAsync();

        foreach (var employee in employees)
        {
            lastName.Value = (object?)employee.LastName ?? DBNull.Value;
            titleOfCourtesy.Value = (object?)employee.TitleOfCourtesy ?? DBNull.Value;
            birthDate.Value = (object?)employee.BirthDate ?? DBNull.Value;
            hireDate.Value = (object?)employee.HireDate ?? DBNull.Value;
            address.Value = (object?)employee.Address ?? DBNull.Value;
            region.Value = (object?)employee.Region ?? DBNull.Value;
            postalCode.Value = (object?)employee.PostalCode ?? DBNull.Value;
            homePhone.Value = (object?)employee.HomePhone ?? DBNull.Value;
            extension.Value = (object?)employee.Extension ?? DBNull.Value;
            photo.Value = (object?)employee.Photo ?? DBNull.Value;
            notes.Value = (object?)employee.Notes ?? DBNull.Value;
            photoPath.Value = (object?)employee.PhotoPath ?? DBNull.Value;
            employeeId.Value = employee.EmployeeID;

            await command.ExecuteNonQueryAsync();
        }
    }
}
