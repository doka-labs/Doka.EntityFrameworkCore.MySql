namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    private sealed class GeneratedEntitySplitContext : DbContext
    {
        /// <summary>
        /// Initializes the generated entity split context fixture.
        /// </summary>
        public GeneratedEntitySplitContext(
            DbContextOptions<GeneratedEntitySplitContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SplitInventory>(entity =>
            {
                entity.ToTable("SplitInventory");
                entity.HasKey(inventory => inventory.Id);
                entity.Property(inventory => inventory.Id).UseMySqlAutoIncrementColumn();
                entity.SplitToTable(
                    "SplitInventoryDetails",
                    split => split.Property(inventory => inventory.Description));
            });
        }
    }

    private sealed class NonGeneratedEntitySplitContext : DbContext
    {
        /// <summary>
        /// Initializes the non generated entity split context fixture.
        /// </summary>
        public NonGeneratedEntitySplitContext(
            DbContextOptions<NonGeneratedEntitySplitContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SplitInventory>(entity =>
            {
                entity.ToTable("ManualSplitInventory");
                entity.HasKey(inventory => inventory.Id);
                entity.Property(inventory => inventory.Id).ValueGeneratedNever();
                entity.SplitToTable(
                    "ManualSplitInventoryDetails",
                    split => split.Property(inventory => inventory.Description));
            });

            modelBuilder.Entity<OrdinaryGeneratedEntity>(entity =>
            {
                entity.ToTable("OrdinaryGeneratedEntities");
                entity.HasKey(item => item.Id);
            });
        }
    }

    private sealed class NonGeneratedSameTableEntitySplitContext : DbContext
    {
        /// <summary>
        /// Initializes the non generated same table entity split context fixture.
        /// </summary>
        public NonGeneratedSameTableEntitySplitContext(
            DbContextOptions<NonGeneratedSameTableEntitySplitContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SplitInventory>(entity =>
            {
                entity.ToTable("SplitInventory");
                entity.HasKey(inventory => inventory.Id);
                entity.Property(inventory => inventory.Id).ValueGeneratedNever();
                entity.SplitToTable(
                    "SplitInventoryDetails",
                    split => split.Property(inventory => inventory.Description));
            });
        }
    }

    private sealed class SplitInventory
    {
        /// <summary>
        /// Gets or sets the entity key.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the description.
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }

    private sealed class OrdinaryGeneratedEntity
    {
        /// <summary>
        /// Gets or sets the entity key.
        /// </summary>
        public int Id { get; set; }
    }
}
