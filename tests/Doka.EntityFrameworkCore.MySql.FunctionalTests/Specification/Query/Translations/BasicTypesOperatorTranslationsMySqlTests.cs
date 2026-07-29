using Microsoft.EntityFrameworkCore.Query.Translations.Operators;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Translations;

/// <summary>
/// Executes the official arithmetic-operator translation contract against the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesArithmeticOperatorTranslationsMySqlTest
    : ArithmeticOperatorTranslationsTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesArithmeticOperatorTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }
}

/// <summary>
/// Executes the official bitwise-operator translation contract against the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesBitwiseOperatorTranslationsMySqlTest
    : BitwiseOperatorTranslationsTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesBitwiseOperatorTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }
}

/// <summary>
/// Executes the official comparison-operator translation contract against the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesComparisonOperatorTranslationsMySqlTest
    : ComparisonOperatorTranslationsTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesComparisonOperatorTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }
}

/// <summary>
/// Executes the official logical-operator translation contract against the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesLogicalOperatorTranslationsMySqlTest
    : LogicalOperatorTranslationsTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesLogicalOperatorTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }
}

/// <summary>
/// Executes the remaining official operator translation contract against the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesMiscellaneousOperatorTranslationsMySqlTest
    : MiscellaneousOperatorTranslationsTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesMiscellaneousOperatorTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }
}
