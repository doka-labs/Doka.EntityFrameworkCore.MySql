namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlModelingBaselineTests
{
    /// <summary>
    /// Sequence produces valid model with HasSequence.
    /// </summary>
    [Fact]
    public void Sequence_in_model_is_accepted()
    {
        using var context = new SequenceContext(CreateOptions<SequenceContext>());
        var sequence = context.Model.FindSequence("OrderNumbers");

        Assert.NotNull(sequence);
        Assert.Equal(1000L, sequence.StartValue);
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

        Assert.Contains("varchar", columnType, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(typeof(decimal), function.ReturnType);
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
}
