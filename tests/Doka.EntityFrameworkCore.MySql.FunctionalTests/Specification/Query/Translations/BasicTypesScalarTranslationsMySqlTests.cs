using Microsoft.EntityFrameworkCore.Query.Translations;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Translations;

/// <summary>
/// Executes the official byte-array translation contract against the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesByteArrayTranslationsMySqlTest
    : ByteArrayTranslationsTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesByteArrayTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }
}

/// <summary>
/// Executes the official enum and flags translation contract against the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesEnumTranslationsMySqlTest
    : EnumTranslationsTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesEnumTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }
}

/// <summary>
/// Executes the official GUID translation contract against the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesGuidTranslationsMySqlTest
    : GuidTranslationsTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesGuidTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }
}

/// <summary>
/// Executes the official numeric math translation contract against the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesMathTranslationsMySqlTest
    : MathTranslationsTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesMathTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }
}
