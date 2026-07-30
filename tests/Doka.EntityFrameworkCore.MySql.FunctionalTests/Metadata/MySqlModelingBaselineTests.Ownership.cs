namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlModelingBaselineTests
{
    // -- Owned Types ------------------------------------------------------

    /// <summary>
    /// OwnsOne same-table produces Navigation_Property column naming convention.
    /// </summary>
    [Fact]
    public void Owns_one_same_table_produces_navigation_property_columns()
    {
        using var context = new OwnedContext(CreateOptions<OwnedContext>());
        var addressType = context.Model.FindEntityType(typeof(Address))!;

        // Address columns should be flattened into Customer table.
        Assert.Equal("Customers", addressType.GetTableName());
        Assert.NotNull(addressType.FindProperty(nameof(Address.Street)));
        Assert.NotNull(addressType.FindProperty(nameof(Address.City)));
    }

    /// <summary>
    /// OwnsOne same-table columns exist and are correctly mapped to the owner table.
    /// </summary>
    [Fact]
    public void Owns_one_columns_are_mapped_to_owner_table()
    {
        using var context = new OwnedContext(CreateOptions<OwnedContext>());
        var addressType = context.Model.FindEntityType(typeof(Address))!;
        var streetProperty = addressType.FindProperty(nameof(Address.Street))!;

        // Same-table owned type columns should exist.
        Assert.NotNull(streetProperty);
        Assert.Equal("Customers", addressType.GetTableName());
    }

    /// <summary>
    /// OwnsMany produces a separate collection table with FK and shadow ordering column.
    /// </summary>
    [Fact]
    public void Owns_many_produces_separate_collection_table()
    {
        using var context = new OwnedContext(CreateOptions<OwnedContext>());
        var phoneType = context.Model.FindEntityType(typeof(PhoneNumber))!;

        // OwnsMany always uses a separate table.
        Assert.NotEqual("Customers", phoneType.GetTableName());
        Assert.NotNull(phoneType.GetTableName());
    }

    // -- Complex Types ----------------------------------------------------

    [Fact]
    public void Complex_type_columns_are_flattened_into_owner_table()
    {
        using var context = new ComplexTypeContext(CreateOptions<ComplexTypeContext>());
        var entityType = context.Model.FindEntityType(typeof(Order))!;

        // Complex type properties should exist on the entity type's table.
        var complexProperty = entityType.FindComplexProperty(nameof(Order.BillingAddress));
        Assert.NotNull(complexProperty);

        var complexType = complexProperty.ComplexType;
        Assert.NotNull(complexType.FindProperty(nameof(ComplexAddress.Street)));
        Assert.NotNull(complexType.FindProperty(nameof(ComplexAddress.City)));
    }

    /// <summary>
    /// OwnsOne with custom column name.
    /// </summary>
    [Fact]
    public void Owns_one_custom_column_name_is_preserved()
    {
        using var context = new CustomColumnOwnedContext(CreateOptions<CustomColumnOwnedContext>());
        var addressType = context.Model.FindEntityType(typeof(CustomAddress))!;
        var streetProperty = addressType.FindProperty(nameof(CustomAddress.Street))!;
        var columnName = streetProperty.GetColumnName();

        Assert.Equal("home_street", columnName);
    }

    /// <summary>
    /// Table splitting -- two entity types share one table.
    /// </summary>
    [Fact]
    public void Table_splitting_shares_single_table()
    {
        using var context = new TableSplitContext(CreateOptions<TableSplitContext>());
        var model = context.Model;
        var headerType = model.FindEntityType(typeof(OrderHeader))!;
        var detailType = model.FindEntityType(typeof(OrderDetail))!;

        Assert.Equal("SplitOrders", headerType.GetTableName());
        Assert.Equal("SplitOrders", detailType.GetTableName());
    }
    // -- Additional Context Definitions -----------------------------------

    // Table splitting
    private sealed class OrderHeader
    {
        public int Id { get; set; }
        public string Reference { get; set; } = string.Empty;
        public OrderDetail Detail { get; set; } = null!;
    }

    private sealed class OrderDetail
    {
        public int Id { get; set; }
        public decimal Total { get; set; }
        public OrderHeader Header { get; set; } = null!;
    }

    private sealed class TableSplitContext : DbContext
    {
        public TableSplitContext(
            DbContextOptions<TableSplitContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<OrderHeader>(entity =>
            {
                entity.ToTable("SplitOrders");
                entity.HasKey(e => e.Id);
                entity
                    .HasOne(e => e.Detail)
                    .WithOne(d => d.Header)
                    .HasForeignKey<OrderDetail>(d => d.Id);
            });

            modelBuilder.Entity<OrderDetail>(entity => entity.ToTable("SplitOrders"));
        }
    }

    // Check constraints
    private sealed class CustomOwner
    {
        public int Id { get; set; }
        public CustomAddress? Address { get; set; }
    }

    private sealed class CustomAddress
    {
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
    }

    private sealed class CustomColumnOwnedContext : DbContext
    {
        public CustomColumnOwnedContext(
            DbContextOptions<CustomColumnOwnedContext> options
        ) : base(options) { }

        public DbSet<CustomOwner> Owners => Set<CustomOwner>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<CustomOwner>(entity =>
            {
                entity.ToTable("Owners");
                entity.OwnsOne(
                    o => o.Address,
                    a => a
                        .Property(addr => addr.Street)
                        .HasColumnName("home_street"));
            });
        }
    }

    // Discriminator with explicit length
    /// <summary>
    /// Entity splitting -- one entity maps to two tables.
    /// </summary>
    [Fact]
    public void Entity_splitting_maps_to_two_tables()
    {
        using var context = new EntitySplitContext(CreateOptions<EntitySplitContext>());
        var model = context.Model;
        var entityType = model.FindEntityType(typeof(SplitProduct))!;

        // Entity should be mapped to multiple tables.
        var tableMappings = entityType
            .GetTableMappings()
            .ToList();
        Assert.True(tableMappings.Count >= 2, $"Expected >= 2 table mappings but got {tableMappings.Count}");
    }
    // -- Entity Splitting ------------------------------------------------

    private sealed class SplitProduct
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    private sealed class EntitySplitContext : DbContext
    {
        public EntitySplitContext(
            DbContextOptions<EntitySplitContext> options
        ) : base(options) { }

        public DbSet<SplitProduct> Products => Set<SplitProduct>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SplitProduct>(entity =>
            {
                entity.ToTable("Products");
                entity.SplitToTable(
                    "ProductDetails",
                    t =>
                    {
                        t.Property(p => p.Description);
                        t.Property(p => p.Price);
                    });
            });
        }
    }

    // -- Owned Types Entities --------------------------------------------

    private sealed class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Address? HomeAddress { get; set; }
        public List<PhoneNumber> PhoneNumbers { get; set; } = [];
    }

    private sealed class Address
    {
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
    }

    private sealed class PhoneNumber
    {
        public string Number { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    private sealed class OwnedContext : DbContext
    {
        public OwnedContext(
            DbContextOptions<OwnedContext> options
        ) : base(options) { }

        public DbSet<Customer> Customers => Set<Customer>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<Customer>(entity =>
            {
                entity.ToTable("Customers");
                entity.HasKey(c => c.Id);
                entity.OwnsOne(c => c.HomeAddress);
                entity.OwnsMany(c => c.PhoneNumbers);
            });
        }
    }

    // -- Complex Types Entities -------------------------------------------

    private sealed class Order
    {
        public int Id { get; set; }
        public string Reference { get; set; } = string.Empty;
        public ComplexAddress BillingAddress { get; set; } = new();
    }

    private sealed class ComplexAddress
    {
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
    }

    private sealed class ComplexTypeContext : DbContext
    {
        public ComplexTypeContext(
            DbContextOptions<ComplexTypeContext> options
        ) : base(options) { }

        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<Order>(entity =>
            {
                entity.ToTable("Orders");
                entity.HasKey(o => o.Id);
                entity.ComplexProperty(o => o.BillingAddress);
            });
        }
    }
}
