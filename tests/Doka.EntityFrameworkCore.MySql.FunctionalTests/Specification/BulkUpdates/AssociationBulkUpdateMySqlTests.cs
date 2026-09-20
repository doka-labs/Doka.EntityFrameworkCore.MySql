using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Associations;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Query.Associations.ComplexJson;
using Microsoft.EntityFrameworkCore.Query.Associations.ComplexTableSplitting;
using Microsoft.EntityFrameworkCore.Query.Associations.Navigations;
using Microsoft.EntityFrameworkCore.Query.Associations.OwnedJson;
using Xunit.Sdk;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.BulkUpdates;

/// <summary>
/// Executes the official complex-property JSON bulk-update contract.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ComplexJsonBulkUpdateMySqlTest
    : ComplexJsonBulkUpdateRelationalTestBase<
        ComplexJsonBulkUpdateMySqlFixture>
{
    public ComplexJsonBulkUpdateMySqlTest(
        ComplexJsonBulkUpdateMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(
        fixture,
        testOutputHelper)
    {
    }
}

/// <summary>
/// MySQL fixture for complex properties stored as JSON.
/// </summary>
public sealed class ComplexJsonBulkUpdateMySqlFixture
    : ComplexJsonRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory =>
        MySqlTestStoreFactory.Instance;
}

/// <summary>
/// Executes the official complex-property table-splitting bulk-update
/// contract.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ComplexTableSplittingBulkUpdateMySqlTest
    : ComplexTableSplittingBulkUpdateRelationalTestBase<
        ComplexTableSplittingBulkUpdateMySqlFixture>
{
    public ComplexTableSplittingBulkUpdateMySqlTest(
        ComplexTableSplittingBulkUpdateMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(
        fixture,
        testOutputHelper)
    {
    }
}

/// <summary>
/// MySQL fixture for complex-property table splitting.
/// </summary>
public sealed class ComplexTableSplittingBulkUpdateMySqlFixture
    : ComplexTableSplittingRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory =>
        MySqlTestStoreFactory.Instance;
}

/// <summary>
/// Executes the official owned-JSON bulk-update rejection and diagnostic
/// contract.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class OwnedJsonBulkUpdateMySqlTest
    : OwnedJsonBulkUpdateRelationalTestBase<
        OwnedJsonBulkUpdateMySqlFixture>
{
    public OwnedJsonBulkUpdateMySqlTest(
        OwnedJsonBulkUpdateMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(
        fixture,
        testOutputHelper)
    {
    }
}

/// <summary>
/// MySQL fixture for owned navigations stored as JSON.
/// </summary>
public sealed class OwnedJsonBulkUpdateMySqlFixture
    : OwnedJsonRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory =>
        MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class NavigationsBulkUpdateMySqlTest
    : NavigationsBulkUpdateRelationalTestBase<NavigationsMySqlFixture>
{
    public NavigationsBulkUpdateMySqlTest(
        NavigationsMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper)
    {
    }

    public override Task Delete_entity_with_associations()
        => Assert.ThrowsAsync<MySqlException>(base.Delete_entity_with_associations);

    public override Task Delete_required_associate()
        => Assert.ThrowsAsync<MySqlException>(base.Delete_required_associate);

    public override Task Delete_optional_associate()
        => Assert.ThrowsAsync<MySqlException>(base.Delete_optional_associate);

    public override Task Update_associate_to_parameter()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_associate_to_parameter);

    public override Task Update_associate_to_inline()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_associate_to_inline);

    public override Task Update_associate_to_inline_with_lambda()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_associate_to_inline_with_lambda);

    public override Task Update_associate_to_another_associate()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_associate_to_another_associate);

    public override Task Update_associate_to_null()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_associate_to_null);

    public override Task Update_associate_to_null_with_lambda()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_associate_to_null_with_lambda);

    public override Task Update_associate_to_null_parameter()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_associate_to_null_parameter);

    public override Task Update_nested_associate_to_parameter()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_nested_associate_to_parameter);

    public override Task Update_nested_associate_to_inline_with_lambda()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_nested_associate_to_inline_with_lambda);

    public override Task Update_nested_associate_to_another_nested_associate()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_nested_associate_to_another_nested_associate);

    public override Task Update_nested_collection_to_parameter()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_nested_collection_to_parameter);

    public override Task Update_nested_collection_to_inline_with_lambda()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_nested_collection_to_inline_with_lambda);

    public override Task Update_nested_collection_to_another_nested_collection()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_nested_collection_to_another_nested_collection);

    public override Task Update_collection_to_parameter()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_collection_to_parameter);

    public override Task Update_collection_referencing_the_original_collection()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_collection_referencing_the_original_collection);

    public override Task Update_primitive_collection_to_another_collection()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_primitive_collection_to_another_collection);

    public override Task Update_inside_structural_collection()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_inside_structural_collection);

    public override Task Update_multiple_properties_inside_associates_and_on_entity_type()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_multiple_properties_inside_associates_and_on_entity_type);

    public override Task Update_multiple_projected_associates_via_anonymous_type()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_multiple_projected_associates_via_anonymous_type);

    public override Task Update_multiple_properties_inside_same_associate()
        => Assert.ThrowsAsync<InvalidOperationException>(base.Update_multiple_properties_inside_same_associate);

    public override Task Update_property_on_projected_associate_with_OrderBy_Skip()
        => Assert.ThrowsAsync<EqualException>(base.Update_property_on_projected_associate_with_OrderBy_Skip);
}
