using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Protects convention ownership and complete physical key-byte budgets.
/// </summary>
public sealed partial class MySqlIndexByteBudgetTests
{
    /// <summary>
    /// Overlapping constraints must share their smallest safe implicit lengths.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Overlapping_indexes_reserve_actual_fixed_bytes_and_keep_convention_ownership(
        bool reverse
    )
    {
        using var context = CreateContext(reverse ? "reverse" : "forward");

        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType("Budget")!;
        var properties = entity.GetProperties().ToDictionary(property => property.Name);

        Assert.Equal(254, properties["Id"].GetMaxLength());
        Assert.Equal(254, properties["Scope"].GetMaxLength());
        Assert.Equal(254, properties["Tree"].GetMaxLength());
        Assert.Equal(255, properties["Parent"].GetMaxLength());
        Assert.All(
            properties.Values.Where(property => property.ClrType == typeof(string)),
            property => Assert.Equal(
                ConfigurationSource.Convention,
                ((IConventionProperty)property).GetMaxLengthConfigurationSource()));
        Assert.Equal(3065, (3 * properties["Id"].GetMaxLength()!.Value * 4) + 17);
        Assert.All(entity.GetIndexes(), index => Assert.Null(index.GetMySqlIndexPrefixLengths()));
    }

    private static BudgetContext CreateContext(
        string scenario,
        MySqlServerVersion? version = null
    )
    {
        var options = MySqlFunctionalTestOptions.CreateTransientBuilder<BudgetContext>()
            .UseMySql(
                "Server=localhost;Database=not_used;User Id=not_used;Password=not_used",
                version ?? MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ReplaceService<IModelCacheKeyFactory, BudgetModelCacheKeyFactory>()
            .Options;

        return new BudgetContext(options, scenario);
    }

    private sealed class BudgetContext(
        DbContextOptions<BudgetContext> options,
        string scenario
    ) : DbContext(options)
    {
        internal string Scenario => scenario;

        protected override void ConfigureConventions(
            ModelConfigurationBuilder configurationBuilder
        )
        {
            if (scenario == "twice")
            {
                configurationBuilder.Conventions.Add(services =>
                    new MySqlIndexLengthConvention(services.GetRequiredService<IRelationalTypeMappingSource>()));
            }
        }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("utf8mb4");
            if (scenario.StartsWith("owned-table-", StringComparison.Ordinal))
            {
                var narrowOwner = scenario is "owned-table-narrow"
                    or "owned-table-first-wins"
                    or "owned-table-collation-first-wins";

                modelBuilder.HasCharSet(narrowOwner ? "utf8mb4" : "latin1");
                modelBuilder.Entity<BudgetDefaultOwner>(owner =>
                {
                    owner.ToTable("SharedDefaultBudget").HasCharSet(narrowOwner ? "latin1" : "utf8mb4");

                    if (scenario == "owned-table-collation-first-wins")
                    {
                        owner.HasAnnotation(RelationalAnnotationNames.Collation, "latin1_bin");
                    }

                    owner.OwnsOne(value => value.Detail, detail =>
                    {
                        detail.ToTable("SharedDefaultBudget");
                        detail.Property(value => value.A).HasMaxLength(
                            scenario.EndsWith("first-wins", StringComparison.Ordinal) ? 1200 : 700);

                        if (scenario.EndsWith("first-wins", StringComparison.Ordinal))
                        {
                            detail.HasAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");
                        }

                        if (scenario == "owned-table-collation-first-wins")
                        {
                            detail.HasAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_bin");
                        }


                        if (scenario == "owned-table-overlong")
                        {
                            detail.Property(value => value.B).HasMaxLength(100);
                        }
                        else if (scenario == "owned-table-property-collation")
                        {
                            detail.Property(value => value.A).UseCollation("latin1_bin");
                        }

                        detail.HasIndex(value => new { value.A, value.B });
                    });
                    owner.Navigation(value => value.Detail).IsRequired();
                });

                return;
            }

            if (scenario is "defaults-tph" or "defaults-tpt")
            {
                modelBuilder.Entity<BudgetDefaultBase>(entity =>
                {
                    entity.ToTable("DefaultBaseBudget").HasCharSet("latin1");

                    if (scenario == "defaults-tph")
                    {
                        entity.UseTphMappingStrategy();
                    }
                    else
                    {
                        entity.UseTptMappingStrategy();
                    }
                });
                var derived = modelBuilder.Entity<BudgetDefaultDerived>().HasCharSet("utf8mb4");

                if (scenario == "defaults-tpt")
                {
                    derived.ToTable("DefaultDerivedBudget");
                }

                return;
            }

            if (scenario == "tpc")
            {
                modelBuilder.HasCharSet("latin1");
                modelBuilder.Entity<BudgetTpcBase>(entity =>
                {
                    entity.UseTpcMappingStrategy();
                    entity.HasIndex(value => new
                    {
                        value.Code,
                        value.Flag,
                        value.Scope,
                        value.Right,
                        value.Left,
                        value.Tree,
                    });
                });
                modelBuilder.Entity<BudgetTpcRow>().HasCharSet("utf8mb4");

                return;
            }

            if (scenario is "shared-binary" or "shared-binary-mixed")
            {
                modelBuilder.Entity<BudgetBinaryOwner>(owner =>
                {
                    owner.ToTable("SharedBinaryBudget");
                    owner.Property(value => value.Code).HasColumnName("Code");
                    owner.HasIndex(value => value.Code);
                    owner.OwnsOne(value => value.Detail, detail =>
                    {
                        detail.ToTable("SharedBinaryBudget");
                        detail.Property(value => value.Code).HasColumnName("Code").HasMaxLength(16);
                    });
                    owner.Navigation(value => value.Detail).IsRequired();

                    if (scenario == "shared-binary-mixed")
                    {
                        owner.OwnsOne(value => value.StoreDetail, detail =>
                        {
                            detail.ToTable("SharedBinaryBudget");
                            detail.Property(value => value.Code).HasColumnName("Code").HasColumnType("varbinary(17)");
                        });
                        owner.Navigation(value => value.StoreDetail).IsRequired();
                    }
                    else
                    {
                        owner.Ignore(value => value.StoreDetail);
                    }
                });

                return;
            }

            if (scenario.StartsWith("shared-", StringComparison.Ordinal))
            {
                modelBuilder.Entity<BudgetOwner>(owner =>
                {
                    owner.ToTable(scenario == "shared-split" ? "BudgetMain" : "SharedBudget");
                    owner.Property(value => value.Code).HasColumnName("Code");
                    if (scenario is "shared-explicit-length" or "shared-explicit-store")
                    {
                        owner.HasIndex(value => new { value.Code, value.Spare });
                    }
                    else
                    {
                        owner.HasIndex(value => value.Code);
                    }

                    if (scenario == "shared-split")
                    {
                        owner.SplitToTable("SharedBudget", split =>
                        {
                            split.Property(value => value.Code).HasColumnName("Code");
                            split.Property(value => value.Spare);
                        });
                    }

                    owner.OwnsOne(value => value.Detail, detail =>
                    {
                        detail.ToTable("SharedBudget");
                        detail.Property(value => value.Code).HasColumnName("Code");
                        detail.Property(value => value.Wide).HasMaxLength(700);

                        if (scenario == "shared-explicit-length")
                        {
                            detail.Property(value => value.Code).HasMaxLength(700);
                        }
                        else if (scenario == "shared-explicit-store")
                        {
                            detail.Property(value => value.Code).HasColumnType("char(200)");
                        }
                        else
                        {
                            detail.HasIndex(value => new { value.Code, value.Wide });
                        }

                    });
                    owner.Navigation(value => value.Detail).IsRequired();
                });

                return;
            }

            var entity = modelBuilder.SharedTypeEntity<Dictionary<string, object>>("Budget");
            if (scenario is "forward" or "reverse" or "repeat" or "twice")
            {
                ConfigureReportEntity(entity, scenario == "reverse");
            }
            else
            {
                ConfigureFacetEntity(modelBuilder, entity, scenario);
            }
        }
    }

    private sealed class BudgetModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(
            DbContext context,
            bool designTime
        ) => (context.GetType(), ((BudgetContext)context).Scenario, designTime);
    }

    private static void ConfigureReportEntity(
        EntityTypeBuilder<Dictionary<string, object>> entity,
        bool reverse
    )
    {
        entity.Property<string>("Id").IsRequired();
        entity.Property<string>("Scope").IsRequired();
        entity.Property<string>("Tree").IsRequired();
        entity.Property<string>("Parent");
        entity.Property<long>("L");
        entity.Property<long>("R");
        entity.Property<long>("Position");
        entity.Property<int>("Depth");
        entity.Property<bool>("Flag").HasComputedColumnSql("`R` > `L` + 1", stored: false);
        entity.HasKey("Id");
        string[][] definitions =
        [
            ["Scope", "Tree", "L"],
            ["Scope", "Tree", "R"],
            ["Scope", "Tree", "Parent", "Position"],
            ["Scope", "Flag", "Tree", "R", "L", "Id"],
        ];

        foreach (var definition in reverse ? definitions.Reverse() : definitions)
        {
            entity.HasIndex(definition);
        }
    }
}
