using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Update;

/// <summary>
/// Runs the official store-generated value contract against MySQL and MariaDB.
/// It verifies identity, default, computed, sentinel, backing-field, converter,
/// and read-only value behavior across insert, update, and delete operations.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class StoreGeneratedMySqlTest
    : StoreGeneratedTestBase<StoreGeneratedMySqlTest.StoreGeneratedMySqlFixture>
{
    public StoreGeneratedMySqlTest(
        StoreGeneratedMySqlFixture fixture
    ) : base(fixture)
    {
    }

    protected override void UseTransaction(
        DatabaseFacade facade,
        IDbContextTransaction transaction
    ) => facade.UseTransaction(transaction.GetDbTransaction());

    /// <summary>
    /// Configures MySQL-compatible column shapes for every store-generated
    /// value pattern defined by the official specification model.
    /// </summary>
    public sealed class StoreGeneratedMySqlFixture : StoreGeneratedFixtureBase
    {
        protected override string StoreName => "StoreGeneratedTest";

        // Reusing an existing schema can hide provider type-mapping drift.
        protected override bool RecreateStore => true;

        protected override ITestStoreFactory TestStoreFactory =>
            MySqlTestStoreFactory.Instance;

        public override DbContextOptionsBuilder AddOptions(
            DbContextOptionsBuilder builder
        ) => builder
            .EnableSensitiveDataLogging()
            .ConfigureWarnings(warnings => warnings
                .Default(WarningBehavior.Throw)
                .Ignore(CoreEventId.SensitiveDataLoggingEnabledWarning)
                .Ignore(RelationalEventId.BoolWithDefaultWarning)
                .Ignore(RelationalEventId.MultipleCollectionIncludeWarning));

        protected override void OnModelCreating(
            ModelBuilder modelBuilder,
            DbContext context
        )
        {
            modelBuilder.Entity<Gumball>(entity =>
            {
                entity.Property(value => value.Id).UseMySqlAutoIncrementColumn();
                ConfigureDefault(entity.Property(value => value.Identity), "Banana Joe");
                ConfigureDefault(
                    entity.Property(value => value.IdentityReadOnlyBeforeSave),
                    "Doughnut Sheriff");
                ConfigureDefault(
                    entity.Property(value => value.IdentityReadOnlyAfterSave),
                    "Anton");
                ConfigureDefault(
                    entity.Property(value => value.AlwaysIdentity),
                    "Banana Joe");
                ConfigureDefault(
                    entity.Property(value => value.AlwaysIdentityReadOnlyBeforeSave),
                    "Doughnut Sheriff");
                ConfigureDefault(
                    entity.Property(value => value.AlwaysIdentityReadOnlyAfterSave),
                    "Anton");
                ConfigureDefault(entity.Property(value => value.Computed), "Alan");
                ConfigureDefault(
                    entity.Property(value => value.ComputedReadOnlyBeforeSave),
                    "Carmen");
                ConfigureDefault(
                    entity.Property(value => value.ComputedReadOnlyAfterSave),
                    "Tina Rex");
                ConfigureDefault(
                    entity.Property(value => value.AlwaysComputed),
                    "Alan");
                ConfigureDefault(
                    entity.Property(value => value.AlwaysComputedReadOnlyBeforeSave),
                    "Carmen");
                ConfigureDefault(
                    entity.Property(value => value.AlwaysComputedReadOnlyAfterSave),
                    "Tina Rex");
            });

            modelBuilder.Entity<Anais>(entity =>
            {
                ConfigureDefault(entity.Property(value => value.OnAdd), "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddUseBeforeUseAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddIgnoreBeforeUseAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddThrowBeforeUseAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddUseBeforeIgnoreAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddIgnoreBeforeIgnoreAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddThrowBeforeIgnoreAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddUseBeforeThrowAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddIgnoreBeforeThrowAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddThrowBeforeThrowAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddOrUpdate),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddOrUpdateUseBeforeUseAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddOrUpdateIgnoreBeforeUseAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddOrUpdateThrowBeforeUseAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddOrUpdateUseBeforeIgnoreAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddOrUpdateIgnoreBeforeIgnoreAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddOrUpdateThrowBeforeIgnoreAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddOrUpdateUseBeforeThrowAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddOrUpdateIgnoreBeforeThrowAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnAddOrUpdateThrowBeforeThrowAfter),
                    "Rabbit");
                ConfigureDefault(entity.Property(value => value.OnUpdate), "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnUpdateUseBeforeUseAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnUpdateIgnoreBeforeUseAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnUpdateThrowBeforeUseAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnUpdateUseBeforeIgnoreAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnUpdateIgnoreBeforeIgnoreAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnUpdateThrowBeforeIgnoreAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnUpdateUseBeforeThrowAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnUpdateIgnoreBeforeThrowAfter),
                    "Rabbit");
                ConfigureDefault(
                    entity.Property(value => value.OnUpdateThrowBeforeThrowAfter),
                    "Rabbit");
            });

            modelBuilder.Entity<WithBackingFields>(entity =>
            {
                entity.Property(value => value.NullableAsNonNullable)
                    .HasComputedColumnSql("1");
                entity.Property(value => value.NonNullableAsNullable)
                    .HasComputedColumnSql("1");
            });

            modelBuilder.Entity<WithNoBackingFields>(entity =>
            {
                entity.Property(value => value.TrueDefault).HasDefaultValue(true);
                entity.Property(value => value.NonZeroDefault).HasDefaultValue(-1);
                entity.Property(value => value.FalseDefault).HasDefaultValue(false);
                entity.Property(value => value.ZeroDefault).HasDefaultValue(0);
            });

            modelBuilder.Entity<WithNullableBackingFields>(entity =>
            {
                entity.Property(value => value.NullableBackedBoolTrueDefault)
                    .HasDefaultValue(true);
                entity.Property(value => value.NullableBackedIntNonZeroDefault)
                    .HasDefaultValue(-1);
                entity.Property(value => value.NullableBackedBoolFalseDefault)
                    .HasDefaultValue(false);
                entity.Property(value => value.NullableBackedIntZeroDefault)
                    .HasDefaultValue(0);
            });

            modelBuilder.Entity<WithObjectBackingFields>(entity =>
            {
                entity.Property(value => value.NullableBackedBoolTrueDefault)
                    .HasDefaultValue(true);
                entity.Property(value => value.NullableBackedIntNonZeroDefault)
                    .HasDefaultValue(-1);
                entity.Property(value => value.NullableBackedBoolFalseDefault)
                    .HasDefaultValue(false);
                entity.Property(value => value.NullableBackedIntZeroDefault)
                    .HasDefaultValue(0);
            });

            modelBuilder.Entity<NonStoreGenDependent>()
                .Property(value => value.HasTemp)
                .HasDefaultValue(777);
            modelBuilder.Entity<CompositePrincipal>()
                .Property(value => value.Id)
                .UseMySqlAutoIncrementColumn();

            base.OnModelCreating(modelBuilder, context);

            static void ConfigureDefault(
                PropertyBuilder<string?> property,
                string defaultValue
            ) => property
                .HasMaxLength(500)
                .HasDefaultValue(defaultValue);
        }
    }
}
