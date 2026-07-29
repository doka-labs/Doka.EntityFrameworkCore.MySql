using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
            b.Property(e => e.LastName).HasMaxLength(20);
            b.Property(e => e.Title).HasMaxLength(30);
            b.Property(e => e.TitleOfCourtesy).HasMaxLength(25);
            b.Property(e => e.City).HasMaxLength(15);
            b.Property(e => e.Country).HasMaxLength(15);
        });

        modelBuilder.Entity<Product>(b =>
        {
            b.Property(p => p.CategoryID);
            b.Property(p => p.ProductName).HasMaxLength(40);
            b.Property(p => p.UnitPrice).HasColumnType("decimal(19,4)");
        });

        modelBuilder.Entity<OrderDetail>()
            .Property(od => od.UnitPrice)
            .HasColumnType("decimal(19,4)");

        // MostExpensiveProduct intentionally has no explicit registration here: the base
        // NorthwindQueryRelationalFixture registers it as a function-result type with
        // HasNoKey + ToFunction; reaching for modelBuilder.Entity<MostExpensiveProduct>()
        // in this fixture would override that registration with a keyed entity surface and
        // surface a "requires primary key" model-validation error.

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
    }
}
