using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.TestModels.UpdatesModel;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Update;

/// <summary>
/// Updates specification subclass. Exercises the modification-command-batch pipeline end-to-end
/// against the MySQL provider's <see cref="MySqlModificationCommandBatch"/> implementation: insert
/// + update + delete shapes, store-generated keys, concurrency tokens, and multi-statement
/// batching at the MySQL <c>max_allowed_packet</c> + 65535-placeholder cap.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class UpdatesMySqlTest : UpdatesRelationalTestBase<UpdatesMySqlTest.UpdatesMySqlFixture>
{
    public UpdatesMySqlTest(
        UpdatesMySqlFixture fixture
    ) : base(fixture)
    {
    }

    public override void Identifiers_are_generated_correctly()
    {
        using var context = CreateContext();
        var firstEntityType = context.Model.FindEntityType(
            typeof(
                LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectly
            ))!;
        var secondEntityType = context.Model.FindEntityType(
            typeof(
                LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectlyDetails
            ))!;

        AssertIdentifier(firstEntityType.GetTableName());
        AssertIdentifier(firstEntityType.GetKeys().Single().GetName());
        AssertIdentifier(firstEntityType.GetForeignKeys().Single().GetConstraintName());
        AssertIdentifier(firstEntityType.GetIndexes().Single().GetDatabaseName());

        AssertIdentifier(secondEntityType.GetTableName());
        AssertIdentifier(secondEntityType.GetKeys().Single().GetName());
        AssertIdentifier(secondEntityType.GetIndexes().Single().GetDatabaseName());
        Assert.NotEqual(firstEntityType.GetTableName(), secondEntityType.GetTableName());

        var table = StoreObjectIdentifier.Table(secondEntityType.GetTableName()!);
        var longPropertyColumnNames = secondEntityType
            .GetProperties()
            .Where(p => p.Name.StartsWith("ExtraPropertyWithAnExtremelyLong", StringComparison.Ordinal))
            .Select(p => p.GetColumnName(table))
            .ToArray();

        Assert.Equal(2, longPropertyColumnNames.Length);
        Assert.All(longPropertyColumnNames, AssertIdentifier);
        Assert.NotEqual(longPropertyColumnNames[0], longPropertyColumnNames[1]);
    }

    private static void AssertIdentifier(
        string? identifier
    )
    {
        Assert.NotNull(identifier);
        Assert.InRange(identifier.Length, 1, MySqlConventionSetBuilder.MaxIdentifierLength);
    }

    public class UpdatesMySqlFixture : UpdatesRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        // The spec's seed payloads put long content into unkeyed string columns (Discriminator,
        // Name, Concurrency token, etc.); the keyed-string properties (Login.ProfileId1,
        // LoginDetails.ProfileId1, Profile.Id1, Product.Name, Rodney.Id) need MaxLength to
        // satisfy MySqlModelValidator's keyed-or-indexed-text contract. A global
        // HaveMaxLength on every string would cap the unkeyed payloads and trip the seed
        // with "Data too long for column"; the per-property overrides in OnModelCreating
        // below cover the keyed surface while leaving unkeyed strings on the provider's
        // longtext default.

        protected override void OnModelCreating(
            ModelBuilder modelBuilder,
            DbContext context
        )
        {
            base.OnModelCreating(modelBuilder, context);

            // Map Product / ProductWithBytes into a single ProductBase table (TPH) so the
            // Save_with_shared_foreign_key spec test's ProductCategory.ProductId shared FK
            // resolves to a single physical FK constraint instead of two competing constraints
            // (one to Products and one to ProductWithBytes) that cannot both be satisfied for
            // any single row. The base fixture leaves the inheritance unconfigured; EF Core's
            // convention then materializes each concrete type as its own table because both
            // Product and ProductWithBytes carry a DbSet. Forcing TPH on the abstract base
            // mirrors the SqlServer spec-test schema (which emits INSERT INTO [ProductBase]).
            modelBuilder.Entity<ProductBase>().UseTphMappingStrategy();

            modelBuilder
                .Entity<LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectly>(eb =>
                {
                    eb.Property(l => l.ProfileId1).HasMaxLength(64);
                });

            modelBuilder
                .Entity<LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectlyDetails>(eb =>
                {
                    eb.Property(l => l.ProfileId1).HasMaxLength(64);
                });

            modelBuilder.Entity<Profile>().Property(p => p.Id1).HasMaxLength(64);
            modelBuilder.Entity<Product>().Property(p => p.Name).HasMaxLength(255);
            modelBuilder.Entity<Rodney>().Property(r => r.Id).HasMaxLength(64);
        }
    }
}
