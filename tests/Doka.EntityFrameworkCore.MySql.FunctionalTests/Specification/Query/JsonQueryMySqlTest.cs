using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

/// <summary>
/// JSON-query specification subclass. Exercises the EF Core JSON column query surface
/// (JSON_EXTRACT, JSON_TABLE on MySQL 8.x, JSON path navigation, JSON array indexing) against
/// the provider's <see cref="MySqlJsonTypeMapping"/> + JSON translator pipeline. MariaDB / MySQL
/// JSON-alias divergence is documented in ADR D-012; engine-specific Ignore() calls on nested
/// collection-of-collection types follow as the first live-DB run reveals them.
/// </summary>
[Trait("Category", "Spec")]
public class JsonQueryMySqlTest : JsonQueryRelationalTestBase<JsonQueryMySqlTest.JsonQueryMySqlFixture>
{
    public JsonQueryMySqlTest(
        JsonQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }

    public class JsonQueryMySqlFixture : JsonQueryRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}
