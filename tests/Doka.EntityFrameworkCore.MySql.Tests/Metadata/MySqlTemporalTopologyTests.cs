namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies temporal contracts whose correctness depends on the relational model topology.
/// </summary>
public sealed class MySqlTemporalTopologyTests
{
    private static readonly MySqlServerVersion s_mariaDb114 =
        MySqlServerVersion.MariaDb(new Version(11, 4, 0));

    /// <summary>
    /// Convention-created many-to-many join rows remain part of a fully temporal graph.
    /// </summary>
    [Fact]
    public void Implicit_many_to_many_join_between_temporal_entities_is_temporal()
    {
        using var context = CreateContext<ManyToManyConfiguration>();

        var left = context.Model.FindEntityType(typeof(TemporalLeft))!;
        var joinEntityType = Assert.Single(left.GetSkipNavigations())
            .JoinEntityType;

        Assert.True(joinEntityType.IsMySqlTemporal());
        Assert.NotNull(joinEntityType.FindProperty("PeriodStart"));
        Assert.NotNull(joinEntityType.FindProperty("PeriodEnd"));
    }

    /// <summary>
    /// Every EF entity sharing a physical temporal table must declare the same contract.
    /// </summary>
    [Fact]
    public void Current_only_owned_entity_cannot_share_temporal_table()
    {
        using var context = CreateContext<CurrentOwnedConfiguration>();

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains(
            "Every entity type sharing table 'TemporalOwners' must use the same temporal mapping",
            exception.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Explicitly aligned owner and owned metadata produce one valid table contract.
    /// </summary>
    [Fact]
    public void Matching_owned_temporal_mapping_can_share_table()
    {
        using var context = CreateContext<TemporalOwnedConfiguration>();

        var owner = context.Model.FindEntityType(typeof(TemporalOwner))!;
        var owned = Assert.Single(owner.GetNavigations())
            .TargetEntityType;

        Assert.True(owner.IsMySqlTemporal());
        Assert.True(owned.IsMySqlTemporal());
        Assert.Equal(
            owner.GetMySqlTemporalPeriodStartPropertyName(),
            owned.GetMySqlTemporalPeriodStartPropertyName());
        Assert.Equal(
            owner.GetMySqlTemporalPeriodEndPropertyName(),
            owned.GetMySqlTemporalPeriodEndPropertyName());
    }

    /// <summary>
    /// A TPH hierarchy remains valid because all entity types use one physical table.
    /// </summary>
    [Fact]
    public void Tph_hierarchy_can_share_temporal_table()
    {
        using var context = CreateContext<TphConfiguration>();

        var root = context.Model.FindEntityType(typeof(TemporalAnimal))!;
        var derived = context.Model.FindEntityType(typeof(TemporalDog))!;

        Assert.Equal("TemporalAnimals", root.GetTableName());
        Assert.Equal(root.GetTableName(), derived.GetTableName());
        Assert.True(root.IsMySqlTemporal());
        Assert.True(derived.IsMySqlTemporal());
    }

    /// <summary>
    /// A TPT hierarchy gives every physical table an independent period contract.
    /// </summary>
    [Fact]
    public void Tpt_hierarchy_propagates_temporal_metadata_to_each_table()
    {
        using var context = CreateContext<TptConfiguration>();

        var root = context.Model.FindEntityType(typeof(TemporalAnimal))!;
        var derived = context.Model.FindEntityType(typeof(TemporalDog))!;
        var rootPeriodStart = root.GetMySqlTemporalPeriodStartPropertyName()!;
        var derivedPeriodStart = derived.GetMySqlTemporalPeriodStartPropertyName()!;
        var derivedStoreObject = StoreObjectIdentifier.Table("TemporalDogs", schema: null);
        var baseLink = Assert.Single(derived.GetForeignKeys(), foreignKey => foreignKey.IsBaseLinking());

        Assert.True(root.IsMySqlTemporal());
        Assert.True(derived.IsMySqlTemporal());
        Assert.NotEqual(rootPeriodStart, derivedPeriodStart);
        Assert.Equal(DeleteBehavior.NoAction, baseLink.DeleteBehavior);
        Assert.Equal(
            MySqlTemporalMetadata.DefaultPeriodStartPropertyName,
            derived.FindProperty(derivedPeriodStart)!.GetColumnName(derivedStoreObject));
    }

    /// <summary>
    /// A temporal TPC branch makes every concrete union branch temporal.
    /// </summary>
    [Fact]
    public void Tpc_hierarchy_propagates_temporal_metadata_to_each_branch()
    {
        using var context = CreateContext<TpcConfiguration>();

        var dog = context.Model.FindEntityType(typeof(TemporalDog))!;
        var cat = context.Model.FindEntityType(typeof(TemporalCat))!;
        var catPeriodStart = cat.GetMySqlTemporalPeriodStartPropertyName()!;
        var catStoreObject = StoreObjectIdentifier.Table("TemporalCats", schema: null);

        Assert.True(dog.IsMySqlTemporal());
        Assert.True(cat.IsMySqlTemporal());
        Assert.Equal(
            MySqlTemporalMetadata.DefaultPeriodStartPropertyName,
            cat.FindProperty(catPeriodStart)!.GetColumnName(catStoreObject));
    }

    private static TopologyContext<TConfiguration> CreateContext<TConfiguration>()
        where TConfiguration : ITopologyConfiguration, new()
    {
        var options = new DbContextOptionsBuilder<TopologyContext<TConfiguration>>()
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=password;",
                s_mariaDb114)
            .Options;

        return new TopologyContext<TConfiguration>(options);
    }

    private interface ITopologyConfiguration
    {
        void Configure(ModelBuilder modelBuilder);
    }

    private sealed class ManyToManyConfiguration : ITopologyConfiguration
    {
        public void Configure(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TemporalLeft>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.ToTable("TemporalLefts", table => table.IsTemporal());
                entity.HasMany(item => item.Rights)
                    .WithMany(item => item.Lefts);
            });

            modelBuilder.Entity<TemporalRight>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.ToTable("TemporalRights", table => table.IsTemporal());
            });
        }
    }

    private sealed class CurrentOwnedConfiguration : ITopologyConfiguration
    {
        public void Configure(
            ModelBuilder modelBuilder
        ) => ConfigureOwner(modelBuilder, configureOwnedAsTemporal: false);
    }

    private sealed class TemporalOwnedConfiguration : ITopologyConfiguration
    {
        public void Configure(
            ModelBuilder modelBuilder
        ) => ConfigureOwner(modelBuilder, configureOwnedAsTemporal: true);
    }

    private sealed class TphConfiguration : ITopologyConfiguration
    {
        public void Configure(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TemporalAnimal>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.ToTable("TemporalAnimals", table => table.IsTemporal());
                entity.HasDiscriminator<string>("AnimalType");
            });

            modelBuilder.Entity<TemporalDog>();
        }
    }

    private sealed class TptConfiguration : ITopologyConfiguration
    {
        public void Configure(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TemporalAnimal>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.UseTptMappingStrategy();
                entity.ToTable("TemporalAnimals", table => table.IsTemporal());
            });

            modelBuilder.Entity<TemporalDog>()
                .ToTable("TemporalDogs");
        }
    }

    private sealed class TpcConfiguration : ITopologyConfiguration
    {
        public void Configure(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TemporalAnimal>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.UseTpcMappingStrategy();
            });

            modelBuilder.Entity<TemporalDog>()
                .ToTable("TemporalDogs", table => table.IsTemporal());
            modelBuilder.Entity<TemporalCat>()
                .ToTable("TemporalCats");
        }
    }

    private static void ConfigureOwner(
        ModelBuilder modelBuilder,
        bool configureOwnedAsTemporal
    )
    {
        modelBuilder.Entity<TemporalOwner>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.ToTable(
                "TemporalOwners",
                table => table.IsTemporal(
                    temporal =>
                    {
                        temporal.HasPeriodStart("ValidFrom");
                        temporal.HasPeriodEnd("ValidTo");
                    }));

            entity.OwnsOne(
                item => item.Details,
                owned =>
                {
                    if (configureOwnedAsTemporal)
                    {
                        owned.ToTable(
                            "TemporalOwners",
                            table => table.IsTemporal(
                                temporal =>
                                {
                                    temporal.HasPeriodStart("ValidFrom");
                                    temporal.HasPeriodEnd("ValidTo");
                                }));
                    }
                });
        });
    }

    private sealed class TopologyContext<TConfiguration> : DbContext
        where TConfiguration : ITopologyConfiguration, new()
    {
        public TopologyContext(
            DbContextOptions<TopologyContext<TConfiguration>> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => new TConfiguration().Configure(modelBuilder);
    }

    private sealed class TemporalLeft
    {
        public int Id { get; set; }

        public List<TemporalRight> Rights { get; } = [];
    }

    private sealed class TemporalRight
    {
        public int Id { get; set; }

        public List<TemporalLeft> Lefts { get; } = [];
    }

    private sealed class TemporalOwner
    {
        public int Id { get; set; }

        public OwnedDetails Details { get; set; } = new();
    }

    private sealed class OwnedDetails
    {
        public string Description { get; set; } = null!;
    }

    private abstract class TemporalAnimal
    {
        public int Id { get; set; }
    }

    private sealed class TemporalDog : TemporalAnimal
    {
        public string Breed { get; set; } = null!;
    }

    private sealed class TemporalCat : TemporalAnimal
    {
        public string CoatColor { get; set; } = null!;
    }
}
