using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Storage;

/// <summary>
/// CLR-type round-trip baseline against the provider's type-mapping source. Subclassed from
/// the EF Core <c>BuiltInDataTypesTestBase</c> contract so every primitive plus the standard
/// reference types (string, byte array, decimal, DateTime, ...) flows through the same
/// observation that the official Microsoft providers are held to.
/// </summary>
[Trait("Category", "Spec")]
public class BuiltInDataTypesMySqlTest : BuiltInDataTypesTestBase<
    BuiltInDataTypesMySqlTest.BuiltInDataTypesMySqlFixture>
{
    public BuiltInDataTypesMySqlTest(
        BuiltInDataTypesMySqlFixture fixture
    ) : base(fixture)
    {
    }

    public class BuiltInDataTypesMySqlFixture : BuiltInDataTypesFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        public override bool StrictEquality => true;

        public override bool SupportsAnsi => false;

        public override bool SupportsUnicodeToAnsiConversion => false;

        public override bool SupportsLargeStringComparisons => true;

        public override bool SupportsDecimalComparisons => true;

        public override bool SupportsBinaryKeys => true;

        public override bool PreservesDateTimeKind => false;

        public override DateTime DefaultDateTime => new();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder,
            DbContext context
        )
        {
            base.OnModelCreating(modelBuilder, context);

            // The binary-key entities have byte[] primary and foreign keys; MySqlModelValidator
            // requires every keyed or indexed binary property to declare an explicit max length
            // because MySQL refuses to index BLOB / LONGBLOB columns without a prefix. The 36-byte
            // ceiling fits the spec test's binary-key payloads (most are 36-byte GUID-like blobs)
            // while staying inside MySQL's 64-character index-key-prefix budget when combined
            // with the index-key overhead.
            modelBuilder
                .Entity<BinaryKeyDataType>()
                .Property(e => e.Id)
                .HasMaxLength(36);

            modelBuilder.Entity<BinaryForeignKeyDataType>(b =>
            {
                b
                    .Property(e => e.BinaryKeyDataTypeId)
                    .HasMaxLength(36);
                // The default FK constraint name "FK_BinaryForeignKeyDataType_BinaryKeyDataType_
                // BinaryKeyDataTypeId" exceeds MySQL's 64-character identifier limit which
                // MySqlModelValidator rejects at model-build time. The shorter explicit name
                // keeps the spec-test schema buildable without weakening the provider's
                // identifier-length contract.
                b
                    .HasOne(e => e.Principal)
                    .WithMany(e => e.Dependents)
                    .HasConstraintName("FK_BinaryForeignKeyDataType_BinaryKey");
            });

            modelBuilder
                .Entity<StringKeyDataType>()
                .Property(e => e.Id)
                .HasMaxLength(64);

            modelBuilder.Entity<StringForeignKeyDataType>(b =>
            {
                b
                    .Property(e => e.StringKeyDataTypeId)
                    .HasMaxLength(64);
                b
                    .HasOne(e => e.Principal)
                    .WithMany(e => e.Dependents)
                    .HasConstraintName("FK_StringForeignKeyDataType_StringKey");
            });
        }
    }
}
