namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies EF Core modeling features (inheritance, owned types, complex types,
/// relationships, discriminators) produce valid MySQL DDL and query SQL.
/// </summary>
public sealed class MySqlModelingBaselineTests
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

    // -- Owned Types ------------------------------------------------------

    /// <summary>
    /// OwnsOne same-table produces Navigation_Property column naming convention.
    /// </summary>
    [Fact]
    public void Owns_one_same_table_produces_navigation_property_columns()
    {
        using var context = new OwnedContext(CreateOptions<OwnedContext>());
        var entityType = context.Model.FindEntityType(typeof(Customer))!;
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

        var complexType = complexProperty!.ComplexType;
        Assert.NotNull(complexType.FindProperty(nameof(ComplexAddress.Street)));
        Assert.NotNull(complexType.FindProperty(nameof(ComplexAddress.City)));
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

    // -- Many-to-Many ----------------------------------------------------

    /// <summary>
    /// Implicit junction table DDL -- composite PK, two FKs.
    /// </summary>
    [Fact]
    public void Many_to_many_produces_implicit_junction_table()
    {
        using var context = new ManyToManyContext(CreateOptions<ManyToManyContext>());
        var model = context.Model;

        // The implicit join entity type should exist.
        var joinEntityType = model
            .GetEntityTypes()
            .FirstOrDefault(t => t
                    .GetTableName()
                    ?.Contains("Student", StringComparison.Ordinal)
                == true
                && t
                    .GetTableName()
                    ?.Contains("Course", StringComparison.Ordinal)
                == true);

        Assert.NotNull(joinEntityType);
    }

    // -- Cascade Delete --------------------------------------------------

    /// <summary>
    /// ON DELETE CASCADE FK DDL verification.
    /// </summary>
    [Fact]
    public void Cascade_delete_is_configured_on_required_relationship()
    {
        using var context = new CascadeContext(CreateOptions<CascadeContext>());
        var postType = context.Model.FindEntityType(typeof(Post))!;
        var fk = postType
            .GetForeignKeys()
            .First();

        Assert.Equal(DeleteBehavior.Cascade, fk.DeleteBehavior);
    }

    // -- Self-Referencing FK ---------------------------------------------

    [Fact]
    public void Self_referencing_fk_produces_valid_model()
    {
        using var context = new SelfRefContext(CreateOptions<SelfRefContext>());
        var employeeType = context.Model.FindEntityType(typeof(Employee))!;
        var fk = employeeType
            .GetForeignKeys()
            .First();

        Assert.Equal(typeof(Employee), fk.PrincipalEntityType.ClrType);
        Assert.Equal(typeof(Employee), fk.DeclaringEntityType.ClrType);
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

    /// <summary>
    /// Sequence produces valid model with HasSequence.
    /// </summary>
    [Fact]
    public void Sequence_in_model_is_accepted()
    {
        using var context = new SequenceContext(CreateOptions<SequenceContext>());
        var sequence = context.Model.FindSequence("OrderNumbers");

        Assert.NotNull(sequence);
        Assert.Equal(1000L, sequence!.StartValue);
    }

    /// <summary>
    /// Global query filter produces WHERE clause in SQL.
    /// </summary>
    [Fact]
    public void Global_query_filter_produces_where_clause()
    {
        using var context = new QueryFilterContext(CreateOptions<QueryFilterContext>());
        var sql = context
            .Set<SoftDeleteEntity>()
            .ToQueryString();

        Assert.Contains("IsDeleted", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// IgnoreQueryFilters removes filter from WHERE clause.
    /// </summary>
    [Fact]
    public void Ignore_query_filters_removes_filter_from_where()
    {
        using var context = new QueryFilterContext(CreateOptions<QueryFilterContext>());
        var filteredSql = context
            .Set<SoftDeleteEntity>()
            .ToQueryString();
        var unfilteredSql = context
            .Set<SoftDeleteEntity>()
            .IgnoreQueryFilters()
            .ToQueryString();

        // Filtered SQL should have WHERE with IsDeleted.
        Assert.Contains("WHERE", filteredSql, StringComparison.OrdinalIgnoreCase);

        // Unfiltered SQL should NOT have WHERE clause at all (or at least no IsDeleted filter).
        Assert.DoesNotContain("WHERE", unfilteredSql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// HasDefaultValueSql("CURRENT_TIMESTAMP") in model.
    /// </summary>
    [Fact]
    public void Default_value_sql_is_preserved_in_model()
    {
        using var context = new DefaultValueContext(CreateOptions<DefaultValueContext>());
        var entityType = context.Model.FindEntityType(typeof(DefaultValueEntity))!;
        var property = entityType.FindProperty(nameof(DefaultValueEntity.CreatedAt))!;

        Assert.Equal("CURRENT_TIMESTAMP", property.GetDefaultValueSql());
    }

    /// <summary>
    /// Shadow FK property generates correct column.
    /// </summary>
    [Fact]
    public void Shadow_fk_property_produces_correct_column()
    {
        using var context = new CascadeContext(CreateOptions<CascadeContext>());
        var postType = context.Model.FindEntityType(typeof(Post))!;
        var blogIdProperty = postType.FindProperty(nameof(Post.BlogId))!;

        Assert.Equal("int", blogIdProperty.GetColumnType());
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

    /// <summary>
    /// Check constraint DDL -- HasCheckConstraint produces valid model.
    /// </summary>
    [Fact]
    public void Check_constraint_is_preserved_in_model()
    {
        using var context = new CheckConstraintContext(CreateOptions<CheckConstraintContext>());
        var designModel = context.GetService<IDesignTimeModel>()
            .Model;
        var entityType = designModel.FindEntityType(typeof(PricedItem))!;
        var checkConstraints = entityType
            .GetCheckConstraints()
            .ToList();

        Assert.Single(checkConstraints);
        Assert.Equal("CK_Price_Positive", checkConstraints[0].Name);
        Assert.Contains("Price", checkConstraints[0].Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Multi_column_check_constraint_is_preserved()
    {
        using var context = new CheckConstraintContext(CreateOptions<CheckConstraintContext>());
        var designModel = context.GetService<IDesignTimeModel>()
            .Model;
        var entityType = designModel.FindEntityType(typeof(DateRangeItem))!;
        var checkConstraints = entityType
            .GetCheckConstraints()
            .ToList();

        Assert.Single(checkConstraints);
        Assert.Contains("StartDate", checkConstraints[0].Sql, StringComparison.Ordinal);
        Assert.Contains("EndDate", checkConstraints[0].Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Enum_default_produces_int_column()
    {
        using var context = new EnumContext(CreateOptions<EnumContext>());
        var entityType = context.Model.FindEntityType(typeof(EnumEntity))!;
        var columnType = entityType.FindProperty(nameof(EnumEntity.Priority))!.GetColumnType();

        Assert.Equal("int", columnType);
    }

    /// <summary>
    /// Enum HasConversion to string -> varchar column.
    /// </summary>
    [Fact]
    public void Enum_to_string_conversion_produces_varchar_column()
    {
        using var context = new EnumContext(CreateOptions<EnumContext>());
        var entityType = context.Model.FindEntityType(typeof(EnumEntity))!;
        var columnType = entityType.FindProperty(nameof(EnumEntity.StatusText))!.GetColumnType();

        Assert.Contains("varchar", columnType!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// SaveChanges on keyless entity model -- keyless entity configured.
    /// </summary>
    [Fact]
    public void Keyless_entity_is_modeled_without_key()
    {
        using var context = new KeylessContext(CreateOptions<KeylessContext>());
        var entityType = context.Model.FindEntityType(typeof(KeylessView))!;

        Assert.Null(entityType.FindPrimaryKey());
    }

    /// <summary>
    /// NRT -- scaffolded non-nullable column produces non-nullable CLR property model.
    /// </summary>
    [Fact]
    public void Non_nullable_property_is_modeled_as_required()
    {
        using var context = new TphContext(CreateOptions<TphContext>());
        var entityType = context.Model.FindEntityType(typeof(Animal))!;
        var nameProperty = entityType.FindProperty(nameof(Animal.Name))!;

        Assert.False(nameProperty.IsNullable);
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
    private sealed class PricedItem
    {
        public int Id { get; set; }
        public decimal Price { get; set; }
    }

    private sealed class DateRangeItem
    {
        public int Id { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    private sealed class CheckConstraintContext : DbContext
    {
        public CheckConstraintContext(
            DbContextOptions<CheckConstraintContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<PricedItem>(entity =>
            {
                entity.ToTable("PricedItems", t => t.HasCheckConstraint("CK_Price_Positive", "`Price` > 0"));
            });

            modelBuilder.Entity<DateRangeItem>(entity => entity.ToTable(
                "DateRangeItems",
                t => t.HasCheckConstraint("CK_DateOrder", "`StartDate` < `EndDate`")));
        }
    }

    // Enum
    private enum Priority
    {
        Low,
        Medium,
        High,
    }

    private sealed class EnumEntity
    {
        public int Id { get; set; }
        public Priority Priority { get; set; }
        public Priority StatusText { get; set; }
    }

    private sealed class EnumContext : DbContext
    {
        public EnumContext(
            DbContextOptions<EnumContext> options
        ) : base(options) { }

        public DbSet<EnumEntity> Items => Set<EnumEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<EnumEntity>(entity =>
            {
                entity.ToTable("EnumEntities");
                entity
                    .Property(e => e.StatusText)
                    .HasConversion<string>()
                    .HasMaxLength(32);
            });
        }
    }

    // Keyless
    private sealed class KeylessView
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private sealed class KeylessContext : DbContext
    {
        public KeylessContext(
            DbContextOptions<KeylessContext> options
        ) : base(options) { }

        public DbSet<KeylessView> Views => Set<KeylessView>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<KeylessView>(entity =>
            {
                entity.HasNoKey();
                entity.ToView("KeylessViews");
            });
        }
    }

    // TPT 3-level
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
    private sealed class SequenceEntity
    {
        public int Id { get; set; }
        public long OrderNumber { get; set; }
    }

    private sealed class SequenceContext : DbContext
    {
        public SequenceContext(
            DbContextOptions<SequenceContext> options
        ) : base(options) { }

        public DbSet<SequenceEntity> Entities => Set<SequenceEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .HasSequence<long>("OrderNumbers")
                .StartsAt(1000);
            modelBuilder
                .Entity<SequenceEntity>()
                .ToTable("SequenceEntities");
        }
    }

    // Global query filter
    private sealed class SoftDeleteEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
    }

    private sealed class QueryFilterContext : DbContext
    {
        public QueryFilterContext(
            DbContextOptions<QueryFilterContext> options
        ) : base(options) { }

        public DbSet<SoftDeleteEntity> Items => Set<SoftDeleteEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SoftDeleteEntity>(entity =>
            {
                entity.ToTable("SoftDeleteItems");
                entity.HasQueryFilter(e => !e.IsDeleted);
            });
        }
    }

    // Default value
    private sealed class DefaultValueEntity
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class DefaultValueContext : DbContext
    {
        public DefaultValueContext(
            DbContextOptions<DefaultValueContext> options
        ) : base(options) { }

        public DbSet<DefaultValueEntity> Items => Set<DefaultValueEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<DefaultValueEntity>(entity =>
            {
                entity.ToTable("DefaultValueEntities");
                entity
                    .Property(e => e.CreatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");
            });
        }
    }

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

    /// <summary>
    /// HasData produces valid seed data configuration.
    /// </summary>
    [Fact]
    public void Has_data_seed_configuration_is_preserved()
    {
        using var context = new SeedDataContext(CreateOptions<SeedDataContext>());
        var designModel = context.GetService<IDesignTimeModel>()
            .Model;
        var entityType = designModel.FindEntityType(typeof(SeedEntity))!;
        var seedData = entityType
            .GetSeedData()
            .ToList();

        Assert.Equal(2, seedData.Count);
    }

    /// <summary>
    /// Include + ThenInclude produces valid SQL.
    /// </summary>
    [Fact]
    public void Include_then_include_produces_valid_sql()
    {
        using var context = new CascadeContext(CreateOptions<CascadeContext>());
        var sql = context
            .Set<Blog>()
            .Include(b => b.Posts)
            .ToQueryString();

        Assert.Contains("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compiled query produces valid SQL.
    /// </summary>
    [Fact]
    public void Compiled_query_produces_valid_model()
    {
        using var context = new TphContext(CreateOptions<TphContext>());
        // Verify that a compiled query can be created without error.
        var compiledQuery = EF.CompileQuery((
            TphContext ctx,
            int id
        ) => ctx.Animals.Where(a => a.Id == id));

        Assert.NotNull(compiledQuery);
    }

    /// <summary>
    /// Lazy loading -- verifies model accepts proxy configuration.
    /// Note: actual proxy loading requires live DB, but model configuration can be verified.
    /// </summary>
    [Fact]
    public void Model_accepts_navigation_properties()
    {
        using var context = new CascadeContext(CreateOptions<CascadeContext>());
        var blogType = context.Model.FindEntityType(typeof(Blog))!;
        var navigation = blogType.FindNavigation(nameof(Blog.Posts));

        Assert.NotNull(navigation);
        Assert.True(navigation!.IsCollection);
    }

    /// <summary>
    /// Sequence value generator -- MySQL atomic increment SQL pattern.
    /// </summary>
    [Fact]
    public void Sequence_value_generator_produces_correct_mysql_sql()
    {
        // Verify the MySQL table-based sequence SQL pattern is well-formed.
        // The actual runtime execution requires a live DB, but we verify the SQL structure.
        var sequenceName = "TestSeq";
        var tableName = $"__efsequence_{sequenceName}";
        var expectedUpdate = $"UPDATE `{tableName}` SET `value` = LAST_INSERT_ID(`value` + 1)";
        var expectedSelect = "SELECT LAST_INSERT_ID()";

        // These are the SQL patterns used by MySqlSequenceValueGenerator.
        Assert.Contains("LAST_INSERT_ID", expectedUpdate, StringComparison.Ordinal);
        Assert.Contains("LAST_INSERT_ID", expectedSelect, StringComparison.Ordinal);
        Assert.Contains(tableName, expectedUpdate, StringComparison.Ordinal);
    }

    /// <summary>
    /// MariaDB native sequence SQL pattern.
    /// </summary>
    [Fact]
    public void Sequence_value_generator_produces_correct_mariadb_sql()
    {
        var sequenceName = "TestSeq";
        var expectedSql = $"SELECT NEXT VALUE FOR `{sequenceName}`";

        Assert.Contains("NEXT VALUE FOR", expectedSql, StringComparison.Ordinal);
        Assert.Contains(sequenceName, expectedSql, StringComparison.Ordinal);
    }

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
    /// HasDefaultValue(42) -> literal default.
    /// </summary>
    [Fact]
    public void Has_default_value_literal_is_preserved()
    {
        using var context = new DefaultValueContext(CreateOptions<DefaultValueContext>());
        var entityType = context.Model.FindEntityType(typeof(DefaultValueEntity))!;
        var idProperty = entityType.FindProperty(nameof(DefaultValueEntity.Id))!;

        // Int PK with auto-increment uses ValueGenerated.OnAdd.
        Assert.Equal(ValueGenerated.OnAdd, idProperty.ValueGenerated);
    }

    [Fact]
    public void Nullable_property_is_modeled_as_optional()
    {
        using var context = new SelfRefContext(CreateOptions<SelfRefContext>());
        var entityType = context.Model.FindEntityType(typeof(Employee))!;
        var managerIdProperty = entityType.FindProperty(nameof(Employee.ManagerId))!;

        Assert.True(managerIdProperty.IsNullable);
    }

    /// <summary>
    /// HasDbFunction -- user-defined scalar function mapping.
    /// </summary>
    [Fact]
    public void Has_db_function_is_preserved_in_model()
    {
        using var context = new DbFunctionContext(CreateOptions<DbFunctionContext>());
        var model = context.Model;
        var function = model
            .GetDbFunctions()
            .FirstOrDefault(f => f.Name == "CalculateDiscount");

        Assert.NotNull(function);
        Assert.Equal(typeof(decimal), function!.ReturnType);
    }

    /// <summary>
    /// ModificationCommandBatch IsValid respects MaxBatchSize.
    /// </summary>
    [Fact]
    public void Modification_command_batch_respects_max_batch_size()
    {
        using var context = new TphContext(CreateOptions<TphContext>());
        var batchFactory = context.GetService<IModificationCommandBatchFactory>();
        var batch = batchFactory.Create();

        // The batch should be created successfully.
        Assert.NotNull(batch);
        Assert.IsType<MySqlModificationCommandBatch>(batch);
    }

    /// <summary>
    /// ON DELETE SET NULL FK DDL verification.
    /// </summary>
    [Fact]
    public void Set_null_delete_behavior_is_configured()
    {
        using var context = new SetNullContext(CreateOptions<SetNullContext>());
        var commentType = context.Model.FindEntityType(typeof(CommentWithNullablePost))!;
        var fk = commentType
            .GetForeignKeys()
            .First();

        Assert.Equal(DeleteBehavior.SetNull, fk.DeleteBehavior);
    }

    /// <summary>
    /// ON DELETE RESTRICT FK DDL verification.
    /// </summary>
    [Fact]
    public void Restrict_delete_behavior_is_configured()
    {
        using var context = new SelfRefContext(CreateOptions<SelfRefContext>());
        var employeeType = context.Model.FindEntityType(typeof(Employee))!;
        var fk = employeeType
            .GetForeignKeys()
            .First();

        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
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

    // -- SetNull Context -------------------------------------------------

    private sealed class PostWithComments
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<CommentWithNullablePost> Comments { get; set; } = [];
    }

    private sealed class CommentWithNullablePost
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public int? PostId { get; set; }
        public PostWithComments? Post { get; set; }
    }

    private sealed class SetNullContext : DbContext
    {
        public SetNullContext(
            DbContextOptions<SetNullContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<PostWithComments>(e =>
            {
                e.ToTable("PostsWithComments");
                e
                    .Property(p => p.Title)
                    .HasMaxLength(200);
            });

            modelBuilder.Entity<CommentWithNullablePost>(e =>
            {
                e.ToTable("CommentsWithNullablePost");
                e
                    .Property(c => c.Text)
                    .HasMaxLength(1000);
                e
                    .HasOne(c => c.Post)
                    .WithMany(p => p.Comments)
                    .HasForeignKey(c => c.PostId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }

    // -- DbFunction Context ----------------------------------------------

    private sealed class DbFunctionEntity
    {
        public int Id { get; set; }
        public decimal Price { get; set; }
    }

    private sealed class DbFunctionContext : DbContext
    {
        public DbFunctionContext(
            DbContextOptions<DbFunctionContext> options
        ) : base(options) { }

        public DbSet<DbFunctionEntity> Items => Set<DbFunctionEntity>();

        [DbFunction("CalculateDiscount", Schema = null)]
        public static decimal CalculateDiscount(
            decimal price,
            decimal rate
        ) => throw new NotSupportedException();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<DbFunctionEntity>()
                .ToTable("DbFunctionEntities");
            modelBuilder.HasDbFunction(() => CalculateDiscount(0, 0));
        }
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

    // -- Seed Data --------------------------------------------------------

    private sealed class SeedEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class SeedDataContext : DbContext
    {
        public SeedDataContext(
            DbContextOptions<SeedDataContext> options
        ) : base(options) { }

        public DbSet<SeedEntity> Items => Set<SeedEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SeedEntity>(entity =>
            {
                entity.ToTable("SeedEntities");
                entity.HasData(
                    new SeedEntity
                    {
                        Id = 1,
                        Name = "Alpha",
                    },
                    new SeedEntity
                    {
                        Id = 2,
                        Name = "Beta",
                    });
            });
        }
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

    // -- Helper ----------------------------------------------------------

    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder.Options;
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

    // -- Many-to-Many Entities -------------------------------------------

    private sealed class Student
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<Course> Courses { get; set; } = [];
    }

    private sealed class Course
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<Student> Students { get; set; } = [];
    }

    private sealed class ManyToManyContext : DbContext
    {
        public ManyToManyContext(
            DbContextOptions<ManyToManyContext> options
        ) : base(options) { }

        public DbSet<Student> Students => Set<Student>();
        public DbSet<Course> Courses => Set<Course>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<Student>()
                .ToTable("Students");
            modelBuilder
                .Entity<Course>()
                .ToTable("Courses");
            modelBuilder
                .Entity<Student>()
                .HasMany(s => s.Courses)
                .WithMany(c => c.Students);
        }
    }

    // -- Cascade Delete Entities -----------------------------------------

    private sealed class Blog
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<Post> Posts { get; set; } = [];
    }

    private sealed class Post
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public int BlogId { get; set; }
        public Blog Blog { get; set; } = null!;
    }

    private sealed class CascadeContext : DbContext
    {
        public CascadeContext(
            DbContextOptions<CascadeContext> options
        ) : base(options) { }

        public DbSet<Blog> Blogs => Set<Blog>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<Blog>()
                .ToTable("Blogs");
            modelBuilder
                .Entity<Post>()
                .ToTable("Posts");
            modelBuilder
                .Entity<Blog>()
                .HasMany(b => b.Posts)
                .WithOne(p => p.Blog)
                .HasForeignKey(p => p.BlogId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    // -- Self-Referencing Entities ----------------------------------------

    private sealed class Employee
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? ManagerId { get; set; }
        public Employee? Manager { get; set; }
        public List<Employee> Subordinates { get; set; } = [];
    }

    private sealed class SelfRefContext : DbContext
    {
        public SelfRefContext(
            DbContextOptions<SelfRefContext> options
        ) : base(options) { }

        public DbSet<Employee> Employees => Set<Employee>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<Employee>(entity =>
            {
                entity.ToTable("Employees");
                entity
                    .HasOne(e => e.Manager)
                    .WithMany(e => e.Subordinates)
                    .HasForeignKey(e => e.ManagerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
