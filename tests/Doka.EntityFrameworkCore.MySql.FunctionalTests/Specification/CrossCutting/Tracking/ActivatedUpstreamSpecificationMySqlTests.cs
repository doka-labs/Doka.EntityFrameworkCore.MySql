using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Tracking;

/// <summary>
/// Activates upstream-skipped complex-collection operations that the provider matrix
/// proves are supported without provider-specific behavior.
/// </summary>
public sealed partial class ComplexTypesTrackingMySqlTest
{
    [DirectTheory]
    [InheritedTheoryData]
    public override void Can_remove_from_complex_record_collection_with_nested_complex_collection(
        bool trackFromQuery
    ) => base.Can_remove_from_complex_record_collection_with_nested_complex_collection(trackFromQuery);

    [DirectTheory]
    [InheritedTheoryData]
    public override void Can_remove_from_complex_record_field_collection_with_nested_complex_collection(
        bool trackFromQuery
    ) => base.Can_remove_from_complex_record_field_collection_with_nested_complex_collection(trackFromQuery);
}
