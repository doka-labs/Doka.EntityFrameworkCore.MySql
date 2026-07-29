using Microsoft.EntityFrameworkCore.Query.Translations;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Translations;

/// <summary>
/// Executes the official core and relational string translation contracts against the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesStringTranslationsMySqlTest
    : StringTranslationsRelationalTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesStringTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }
}
