using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

public sealed partial class OwnedQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Distinct_over_owned_collection(
        bool async
    ) => base.Distinct_over_owned_collection(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Union_over_owned_collection(
        bool async
    ) => base.Union_over_owned_collection(async);
}

public sealed partial class PrecompiledQueryMySqlTest
{
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task ValuesExpression() => base.ValuesExpression();
}

public sealed partial class PrimitiveCollectionsQueryMySqlTest
{
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Distinct() => base.Column_collection_Distinct();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Intersect_inline_collection() =>
        base.Column_collection_Intersect_inline_collection();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Skip() => base.Column_collection_Skip();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Union_parameter_collection() =>
        base.Column_collection_Union_parameter_collection();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Where_Skip() => base.Column_collection_Where_Skip();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Where_Skip_Take() => base.Column_collection_Where_Skip_Take();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Where_Take() => base.Column_collection_Where_Take();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Where_Union() => base.Column_collection_Where_Union();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_in_subquery_Union_parameter_collection() =>
        base.Column_collection_in_subquery_Union_parameter_collection();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Inline_collection_Except_column_collection() =>
        base.Inline_collection_Except_column_collection();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Inline_collection_of_nullable_value_type_Max() =>
        base.Inline_collection_of_nullable_value_type_Max();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Inline_collection_of_nullable_value_type_Min() =>
        base.Inline_collection_of_nullable_value_type_Min();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Inline_collection_of_nullable_value_type_with_null_Max() =>
        base.Inline_collection_of_nullable_value_type_with_null_Max();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Inline_collection_of_nullable_value_type_with_null_Min() =>
        base.Inline_collection_of_nullable_value_type_with_null_Min();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Parameter_collection_Concat_column_collection() =>
        base.Parameter_collection_Concat_column_collection();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Parameter_collection_in_subquery_Union_column_collection() =>
        base.Parameter_collection_in_subquery_Union_column_collection();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Parameter_collection_in_subquery_Union_column_collection_as_compiled_query() =>
        base.Parameter_collection_in_subquery_Union_column_collection_as_compiled_query();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Parameter_collection_in_subquery_Union_column_collection_nested() =>
        base.Parameter_collection_in_subquery_Union_column_collection_nested();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Project_collection_of_ints_with_distinct() => base.Project_collection_of_ints_with_distinct();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Project_collection_of_nullable_ints_with_paging() =>
        base.Project_collection_of_nullable_ints_with_paging();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Project_collection_of_nullable_ints_with_paging2() =>
        base.Project_collection_of_nullable_ints_with_paging2();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Project_collection_of_nullable_ints_with_paging3() =>
        base.Project_collection_of_nullable_ints_with_paging3();

    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Project_inline_collection_with_Union() => base.Project_inline_collection_with_Union();
}
