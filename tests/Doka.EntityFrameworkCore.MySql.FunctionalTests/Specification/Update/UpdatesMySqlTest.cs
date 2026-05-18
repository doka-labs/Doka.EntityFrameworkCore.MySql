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
public class UpdatesMySqlTest : UpdatesRelationalTestBase<UpdatesMySqlTest.UpdatesMySqlFixture>
{
    public UpdatesMySqlTest(
        UpdatesMySqlFixture fixture
    ) : base(fixture)
    {
    }

    // The upstream test asserts that the spec's deliberately-long entity-type name flows through
    // the provider's identifier pipeline UNTRUNCATED into table / key / constraint / index names.
    // Doka's MySqlModelValidator.ValidateConstraintNameLengths rejects any FK or index name above
    // MySQL's 64-character limit at model-build time rather than silently truncating; the design
    // choice favors explicit error over silent name collision. The upstream assertion shape does
    // not apply: Doka throws at CreateContext() before the assertion runs. Listed under
    // "Permanent skips" in SkipList.md per ADR D-011 bucket 3.
    [Fact(Skip =
        "Doka rejects identifiers above the MySQL 64-character limit at model-build time; "
        + "the upstream assertion assumes silent truncation, which is not the provider's design. "
        + "See ADR D-011 and SkipList.md.")]
    public override void Identifiers_are_generated_correctly()
    {
        // Skipped per attribute.
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

        // The spec model's deliberately-long LoginEntityType... entity name (105 chars) drives
        // every key / FK / index name above MySQL's 64-character limit. The Login + LoginDetails
        // table-name overrides remap the two entities to short table names so EF Core's auto-
        // generated constraint names stay within budget.
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
                    eb.ToTable("Login");
                    eb.Property(l => l.ProfileId1).HasMaxLength(64);
                });

            modelBuilder
                .Entity<LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectlyDetails>(eb =>
                {
                    eb.ToTable("LoginDetails");
                    eb.Property(l => l.ProfileId1).HasMaxLength(64);
                });

            modelBuilder.Entity<Profile>().Property(p => p.Id1).HasMaxLength(64);
            modelBuilder.Entity<Product>().Property(p => p.Name).HasMaxLength(255);
            modelBuilder.Entity<Rodney>().Property(r => r.Id).HasMaxLength(64);

            // The Login -> Profile FK is a composite of 15+ ProfileId* properties; the
            // auto-generated constraint and index names ("FK_Login_Profile_ProfileId_..."
            // and "IX_Login_ProfileId_...") run > 180 chars and trip
            // MySqlModelValidator.ValidateConstraintNameLengths. Rename the FK and any
            // index on either Login or LoginDetails whose name overflows the 64-char limit
            // via metadata access (a fresh HasOne / WithOne would create a shadow
            // relationship in parallel to the base configuration).
            var loginEntityType = modelBuilder
                .Entity<LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectly>()
                .Metadata;

            foreach (var foreignKey in loginEntityType.GetForeignKeys())
            {
                if (foreignKey.PrincipalEntityType.ClrType == typeof(Profile))
                {
                    foreignKey.SetConstraintName("FK_Login_Profile");
                }
            }

            ShortenLongIndexNames(loginEntityType, "Login");

            var detailsBuilder = modelBuilder
                .Entity<LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectlyDetails>();

            // The Details entity carries two deliberately-long property names (139 and 174
            // chars) to exercise the spec's identifier-truncation tests. MySQL columns cap at
            // 64 chars; map the two columns to short explicit names so EnsureCreated emits
            // valid CREATE TABLE DDL.
            detailsBuilder
                .Property(d => d.ExtraPropertyWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectly)
                .HasColumnName("ExtraProperty");
            detailsBuilder
                .Property(d => d.ExtraPropertyWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectlyWhenTruncatedNamesCollide)
                .HasColumnName("ExtraPropertyCollide");

            ShortenLongIndexNames(detailsBuilder.Metadata, "LoginDetails");
        }

        private static void ShortenLongIndexNames(
            IMutableEntityType entityType,
            string entityShortName
        )
        {
            var ordinal = 0;
            foreach (var index in entityType.GetIndexes())
            {
                index.SetDatabaseName($"IX_{entityShortName}_{ordinal++}");
            }
        }
    }
}
