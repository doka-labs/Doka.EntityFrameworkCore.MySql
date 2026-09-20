using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Query.Translations;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class JsonTranslationsMySqlTest
    : JsonTranslationsRelationalTestBase<JsonTranslationsMySqlTest.JsonTranslationsMySqlFixture>
{
    public JsonTranslationsMySqlTest(
        JsonTranslationsMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public sealed class JsonTranslationsMySqlFixture : JsonTranslationsQueryFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        protected override string RemoveJsonProperty(
            string column,
            string property
        ) => $"JSON_REMOVE({column}, '$.{property}')";
    }
}
