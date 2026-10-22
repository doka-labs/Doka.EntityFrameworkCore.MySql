using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlIndexByteBudgetTests
{
    /// <summary>
    /// Independently budgeted principal and dependent columns must retain their
    /// compatible implicit lengths, including composite and transitive links.
    /// </summary>
    [Theory]
    [InlineData("self", 254)]
    [InlineData("reverse", 254)]
    [InlineData("composite", 254)]
    [InlineData("transitive", 254)]
    [InlineData("owned-principal", 120)]
    [InlineData("both-ends", 191)]
    public void Implicit_foreign_keys_share_the_strictest_complete_index_budget(
        string scenario,
        int expectedLength
    )
    {
        // Arrange
        using var context = CreateRelationshipContext(scenario);

        // Act
        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(RelationshipBudgetNode))!;
        var principal = entity.FindProperty(nameof(RelationshipBudgetNode.Id))!;
        var dependent = entity.FindProperty(nameof(RelationshipBudgetNode.ParentId))!;
        var ddl = context.Database.GenerateCreateScript();

        // Assert
        Assert.Equal(expectedLength, principal.GetMaxLength());
        Assert.Equal(expectedLength, dependent.GetMaxLength());
        Assert.Equal(principal.GetColumnType(), dependent.GetColumnType());
        Assert.Equal(
            ConfigurationSource.Convention,
            ((IConventionProperty)principal).GetMaxLengthConfigurationSource());
        Assert.Equal(
            ConfigurationSource.Convention,
            ((IConventionProperty)dependent).GetMaxLengthConfigurationSource());
        Assert.Contains("FOREIGN KEY", ddl, StringComparison.Ordinal);

        if (scenario == "transitive")
        {
            var follower = model.FindEntityType(typeof(RelationshipBudgetFollower))!;

            Assert.Equal(
                expectedLength,
                follower.FindProperty(nameof(RelationshipBudgetFollower.NodeId))!.GetMaxLength());
        }
    }

    /// <summary>
    /// Explicit dependent facets remain application-owned and do not constrain
    /// an otherwise implicit principal through a reverse inference rule.
    /// </summary>
    [Theory]
    [InlineData("explicit-dependent", 100, "varchar(100)")]
    [InlineData("store-dependent", null, "varchar(100)")]
    public void Explicit_dependent_facets_do_not_resize_the_implicit_principal(
        string scenario,
        int? dependentLength,
        string dependentStoreType
    )
    {
        // Arrange
        using var context = CreateRelationshipContext(scenario);

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(RelationshipBudgetNode))!;
        var principal = entity.FindProperty(nameof(RelationshipBudgetNode.Id))!;
        var dependent = entity.FindProperty(nameof(RelationshipBudgetNode.ParentId))!;

        // Assert
        Assert.Equal(254, principal.GetMaxLength());
        Assert.Equal(dependentLength, dependent.GetMaxLength());
        Assert.Equal(dependentStoreType, dependent.GetColumnType());
    }

    /// <summary>
    /// A deliberate principal contract continues to supply normal EF inference
    /// to an implicit dependent without changing either application's facets.
    /// </summary>
    [Theory]
    [InlineData("explicit-principal")]
    [InlineData("store-principal")]
    public void Explicit_principal_facets_anchor_only_the_implicit_dependent(
        string scenario
    )
    {
        // Arrange
        using var context = CreateRelationshipContext(scenario);

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(RelationshipBudgetNode))!;
        var principal = entity.FindProperty(nameof(RelationshipBudgetNode.Id))!;
        var dependent = entity.FindProperty(nameof(RelationshipBudgetNode.ParentId))!;

        // Assert
        Assert.Equal(scenario == "explicit-principal" ? 100 : null, principal.GetMaxLength());
        Assert.Equal("varchar(100)", principal.GetColumnType());
        Assert.Equal("varchar(100)", dependent.GetColumnType());
    }

    /// <summary>
    /// Intentionally different explicit lengths remain application metadata;
    /// index budgeting does not introduce a universal FK equality restriction.
    /// </summary>
    [Fact]
    public void Explicit_relationship_asymmetry_remains_unchanged()
    {
        // Arrange
        using var context = CreateRelationshipContext("explicit-both");

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(RelationshipBudgetNode))!;

        // Assert
        Assert.Equal(120, entity.FindProperty(nameof(RelationshipBudgetNode.Id))!.GetMaxLength());
        Assert.Equal(100, entity.FindProperty(nameof(RelationshipBudgetNode.ParentId))!.GetMaxLength());
    }

    /// <summary>
    /// Relationship groups retain deliberate prefix minima and reject a model
    /// that cannot fit them together in the same complete physical index.
    /// </summary>
    [Fact]
    public void Relationship_minima_that_exceed_the_complete_budget_are_rejected()
    {
        // Arrange
        using var context = CreateRelationshipContext("impossible");

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        // Assert
        Assert.Contains("minimum implicit lengths", exception.Message, StringComparison.Ordinal);
        Assert.Contains("3072", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates a disconnected model probe with a separate cache entry per case.
    /// </summary>
    private static RelationshipBudgetContext CreateRelationshipContext(
        string scenario
    )
    {
        var options = MySqlFunctionalTestOptions.CreateTransientBuilder<RelationshipBudgetContext>()
            .UseMySql(
                "Server=localhost;Database=not_used;User Id=not_used;Password=not_used",
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ReplaceService<IModelCacheKeyFactory, RelationshipBudgetCacheKeyFactory>()
            .Options;

        return new RelationshipBudgetContext(options, scenario);
    }

    /// <summary>
    /// Builds the relationship and index shapes exclusively through public EF APIs.
    /// </summary>
    /// <param name="options">The disconnected provider options.</param>
    /// <param name="scenario">The relationship contract under examination.</param>
    private sealed class RelationshipBudgetContext(
        DbContextOptions<RelationshipBudgetContext> options,
        string scenario
    ) : DbContext(options)
    {
        /// <summary>
        /// Gets the model-cache discriminator for this contract case.
        /// </summary>
        internal string Scenario => scenario;

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("utf8mb4");
            var entity = modelBuilder.Entity<RelationshipBudgetNode>();
            entity.HasKey(node => node.Id);
            entity.Property(node => node.Id).ValueGeneratedNever();

            if (scenario == "owned-principal")
            {
                entity.ToTable("RelationshipBudgetNodes");
                entity.Property(node => node.Id).HasColumnName("Id");
                entity.OwnsOne(node => node.Detail, detail =>
                {
                    detail.ToTable("RelationshipBudgetNodes");
                    detail.WithOwner().HasForeignKey("OwnerId");
                    detail.HasKey("OwnerId");
                    detail.Property<string>("OwnerId").HasColumnName("Id").HasMaxLength(120);
                });
                entity.Navigation(node => node.Detail).IsRequired();
            }
            else
            {
                entity.Ignore(node => node.Detail);
            }

            if (scenario == "reverse")
            {
                ConfigureRelationshipBudgetIndex(entity, scenario);
            }

            if (scenario == "composite")
            {
                entity.HasOne<RelationshipBudgetNode>().WithMany()
                    .HasForeignKey(node => new { node.Scope, node.ParentId })
                    .HasPrincipalKey(node => new { node.Scope, node.Id });
            }
            else
            {
                entity.HasOne<RelationshipBudgetNode>().WithMany().HasForeignKey(node => node.ParentId);
            }

            if (scenario != "reverse")
            {
                ConfigureRelationshipBudgetIndex(entity, scenario);
            }

            if (scenario is "explicit-dependent" or "explicit-both")
            {
                entity.Property(node => node.ParentId).HasMaxLength(100);
            }
            else if (scenario == "store-dependent")
            {
                entity.Property(node => node.ParentId).HasColumnType("varchar(100)");
            }
            else if (scenario == "store-principal")
            {
                entity.Property(node => node.Id).HasColumnType("varchar(100)");
            }

            if (scenario is "explicit-principal" or "explicit-both")
            {
                entity.Property(node => node.Id).HasMaxLength(scenario == "explicit-both" ? 120 : 100);
            }

            if (scenario == "transitive")
            {
                modelBuilder.Entity<RelationshipBudgetLink>(link =>
                {
                    link.HasKey(value => value.Id);
                    link.HasAlternateKey(value => value.NodeId);
                    link.HasOne<RelationshipBudgetNode>().WithMany().HasForeignKey(value => value.NodeId);
                });
                modelBuilder.Entity<RelationshipBudgetFollower>(follower =>
                {
                    follower.HasKey(value => value.Id);
                    follower.HasOne<RelationshipBudgetLink>().WithMany()
                        .HasForeignKey(value => value.NodeId)
                        .HasPrincipalKey(value => value.NodeId);
                });
            }

            if (scenario == "impossible")
            {
                entity.HasIndex(node => node.Scope).HasPrefixLength(255);
                entity.HasIndex(node => node.TreeId).HasPrefixLength(255);
                entity.HasIndex(node => node.ParentId).HasPrefixLength(255);
            }
        }
    }

    /// <summary>
    /// Applies complete indexes that expose either a tighter principal cap or
    /// the repeated coefficient of two linked columns in one physical index.
    /// </summary>
    private static void ConfigureRelationshipBudgetIndex(
        EntityTypeBuilder<RelationshipBudgetNode> entity,
        string scenario
    )
    {
        if (scenario == "both-ends")
        {
            entity.HasIndex(node => new { node.Id, node.ParentId, node.Scope, node.TreeId, node.Left, node.Flag });
        }
        else
        {
            entity.HasIndex(node => new { node.Scope, node.Flag, node.TreeId, node.Right, node.Left, node.Id });
        }
    }

    /// <summary>
    /// Keeps explicit, implicit, and negative models isolated in EF's model cache.
    /// </summary>
    private sealed class RelationshipBudgetCacheKeyFactory : IModelCacheKeyFactory
    {
        /// <inheritdoc />
        public object Create(
            DbContext context,
            bool designTime
        ) => (context.GetType(), ((RelationshipBudgetContext)context).Scenario, designTime);
    }

    /// <summary>
    /// Supplies a principal, a nullable self-reference, and complete index parts.
    /// </summary>
    private sealed class RelationshipBudgetNode
    {
        /// <summary>
        /// Gets or sets the complete principal identifier.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the nullable self-reference.
        /// </summary>
        public string? ParentId { get; set; }

        /// <summary>
        /// Gets or sets the first composite identity segment.
        /// </summary>
        public string Scope { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets another complete string index segment.
        /// </summary>
        public string TreeId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the first fixed-width index part.
        /// </summary>
        public long Left { get; set; }

        /// <summary>
        /// Gets or sets the second fixed-width index part.
        /// </summary>
        public long Right { get; set; }

        /// <summary>
        /// Gets or sets the one-byte index discriminator.
        /// </summary>
        public bool Flag { get; set; }

        /// <summary>
        /// Gets or sets the owned mapping sharing the principal column.
        /// </summary>
        public RelationshipBudgetDetail Detail { get; set; } = new();
    }

    /// <summary>
    /// Supplies the owned key's explicit physical alias and an ordinary payload.
    /// </summary>
    private sealed class RelationshipBudgetDetail
    {
        /// <summary>
        /// Gets or sets the owned payload outside the shared identifier.
        /// </summary>
        public string Code { get; set; } = string.Empty;
    }

    /// <summary>
    /// Connects the principal to an alternate-key relationship in another entity.
    /// </summary>
    private sealed class RelationshipBudgetLink
    {
        /// <summary>
        /// Gets or sets the local entity identifier.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the inherited principal identifier.
        /// </summary>
        public string NodeId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Exercises implicit inference through two consecutive FK relationships.
    /// </summary>
    private sealed class RelationshipBudgetFollower
    {
        /// <summary>
        /// Gets or sets the local entity identifier.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the inherited principal identifier.
        /// </summary>
        public string NodeId { get; set; } = string.Empty;
    }
}
