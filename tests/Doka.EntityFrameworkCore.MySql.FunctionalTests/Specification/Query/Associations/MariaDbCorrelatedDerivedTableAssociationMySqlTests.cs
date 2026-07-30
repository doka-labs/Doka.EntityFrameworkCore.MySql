using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Associations;

// These metadata-only overrides keep every unsupported official query explicit. The base
// implementation and its theory rows remain authoritative; only the MariaDB engine
// disposition changes at the provider boundary.

public sealed partial class ComplexJsonCollectionMySqlTest
{
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Distinct() => base.Distinct();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Distinct_over_projected_nested_collection() =>
        base.Distinct_over_projected_nested_collection();
}

public sealed partial class ComplexJsonProjectionMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_FirstOrDefault_complex_collection(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_FirstOrDefault_complex_collection(queryTrackingBehavior);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_optional_related_FirstOrDefault(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_optional_related_FirstOrDefault(queryTrackingBehavior);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_required_related_FirstOrDefault(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_required_related_FirstOrDefault(queryTrackingBehavior);
}

public sealed partial class ComplexJsonSetOperationsMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Over_assocate_collection_Select_nested_with_aggregates_projected(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Over_assocate_collection_Select_nested_with_aggregates_projected(queryTrackingBehavior);

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Over_associate_collections() => base.Over_associate_collections();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Over_nested_associate_collection() => base.Over_nested_associate_collection();
}

public sealed partial class ComplexTableSplittingProjectionMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_FirstOrDefault_complex_collection(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_FirstOrDefault_complex_collection(queryTrackingBehavior);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_optional_related_FirstOrDefault(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_optional_related_FirstOrDefault(queryTrackingBehavior);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_required_related_FirstOrDefault(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_required_related_FirstOrDefault(queryTrackingBehavior);
}

public sealed partial class NavigationsCollectionMySqlTest
{
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Distinct() => base.Distinct();

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Distinct_projected(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Distinct_projected(queryTrackingBehavior);
}

public sealed partial class NavigationsProjectionMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_FirstOrDefault_complex_collection(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_FirstOrDefault_complex_collection(queryTrackingBehavior);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_optional_related_FirstOrDefault(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_optional_related_FirstOrDefault(queryTrackingBehavior);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_required_related_FirstOrDefault(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_required_related_FirstOrDefault(queryTrackingBehavior);
}

public sealed partial class NavigationsSetOperationsMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Over_assocate_collection_Select_nested_with_aggregates_projected(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Over_assocate_collection_Select_nested_with_aggregates_projected(queryTrackingBehavior);

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Over_associate_collections() => base.Over_associate_collections();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Over_different_collection_properties() => base.Over_different_collection_properties();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Over_nested_associate_collection() => base.Over_nested_associate_collection();
}

public sealed partial class OwnedJsonCollectionMySqlTest
{
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Distinct() => base.Distinct();

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Distinct_projected(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Distinct_projected(queryTrackingBehavior);

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task GroupBy() => base.GroupBy();
}

public sealed partial class OwnedJsonProjectionMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_FirstOrDefault_complex_collection(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_FirstOrDefault_complex_collection(queryTrackingBehavior);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_optional_related_FirstOrDefault(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_optional_related_FirstOrDefault(queryTrackingBehavior);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_required_related_FirstOrDefault(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_required_related_FirstOrDefault(queryTrackingBehavior);
}

public sealed partial class OwnedNavigationsCollectionMySqlTest
{
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Distinct() => base.Distinct();

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Distinct_projected(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Distinct_projected(queryTrackingBehavior);
}

public sealed partial class OwnedNavigationsProjectionMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_FirstOrDefault_complex_collection(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_FirstOrDefault_complex_collection(queryTrackingBehavior);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_optional_related_FirstOrDefault(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_optional_related_FirstOrDefault(queryTrackingBehavior);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_required_related_FirstOrDefault(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_required_related_FirstOrDefault(queryTrackingBehavior);
}

public sealed partial class OwnedNavigationsSetOperationsMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Over_assocate_collection_Select_nested_with_aggregates_projected(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Over_assocate_collection_Select_nested_with_aggregates_projected(queryTrackingBehavior);

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Over_associate_collections() => base.Over_associate_collections();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Over_nested_associate_collection() => base.Over_nested_associate_collection();
}

public sealed partial class OwnedTableSplittingProjectionMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_FirstOrDefault_complex_collection(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_FirstOrDefault_complex_collection(queryTrackingBehavior);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_optional_related_FirstOrDefault(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_optional_related_FirstOrDefault(queryTrackingBehavior);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Select_subquery_required_related_FirstOrDefault(
        QueryTrackingBehavior queryTrackingBehavior
    ) => base.Select_subquery_required_related_FirstOrDefault(queryTrackingBehavior);
}
