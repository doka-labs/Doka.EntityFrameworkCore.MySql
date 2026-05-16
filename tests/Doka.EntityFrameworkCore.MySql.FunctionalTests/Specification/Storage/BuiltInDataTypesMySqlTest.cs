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
    }
}
