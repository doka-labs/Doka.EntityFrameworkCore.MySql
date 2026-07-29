using Microsoft.EntityFrameworkCore.Query.Translations;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Translations;

/// <summary>
/// Executes the official core and relational miscellaneous translation contracts against
/// the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesMiscellaneousTranslationsMySqlTest
    : MiscellaneousTranslationsRelationalTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesMiscellaneousTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }
}
