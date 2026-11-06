using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

public sealed partial class OwnedQueryMySqlTest
{
    [Theory]
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Distinct_over_owned_collection(
        bool async
    ) => base.Distinct_over_owned_collection(async);

    [Theory]
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Union_over_owned_collection(
        bool async
    ) => base.Union_over_owned_collection(async);
}

public sealed partial class PrecompiledQueryMySqlTest
{
    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task ValuesExpression() => base.ValuesExpression();
}

public sealed partial class PrimitiveCollectionsQueryMySqlTest
{
    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Distinct() => base.Column_collection_Distinct();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Intersect_inline_collection() =>
        base.Column_collection_Intersect_inline_collection();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Skip() => base.Column_collection_Skip();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Union_parameter_collection() =>
        base.Column_collection_Union_parameter_collection();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Where_Skip() => base.Column_collection_Where_Skip();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Where_Skip_Take() => base.Column_collection_Where_Skip_Take();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Where_Take() => base.Column_collection_Where_Take();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_Where_Union() => base.Column_collection_Where_Union();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Column_collection_in_subquery_Union_parameter_collection() =>
        base.Column_collection_in_subquery_Union_parameter_collection();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Inline_collection_Except_column_collection() =>
        base.Inline_collection_Except_column_collection();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Inline_collection_of_nullable_value_type_Max() =>
        base.Inline_collection_of_nullable_value_type_Max();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Inline_collection_of_nullable_value_type_Min() =>
        base.Inline_collection_of_nullable_value_type_Min();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Inline_collection_of_nullable_value_type_with_null_Max() =>
        base.Inline_collection_of_nullable_value_type_with_null_Max();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Inline_collection_of_nullable_value_type_with_null_Min() =>
        base.Inline_collection_of_nullable_value_type_with_null_Min();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Parameter_collection_Concat_column_collection() =>
        base.Parameter_collection_Concat_column_collection();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Parameter_collection_in_subquery_Union_column_collection() =>
        base.Parameter_collection_in_subquery_Union_column_collection();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Parameter_collection_in_subquery_Union_column_collection_as_compiled_query() =>
        base.Parameter_collection_in_subquery_Union_column_collection_as_compiled_query();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Parameter_collection_in_subquery_Union_column_collection_nested() =>
        base.Parameter_collection_in_subquery_Union_column_collection_nested();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Project_collection_of_ints_with_distinct() => base.Project_collection_of_ints_with_distinct();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Project_collection_of_nullable_ints_with_paging() =>
        base.Project_collection_of_nullable_ints_with_paging();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Project_collection_of_nullable_ints_with_paging2() =>
        base.Project_collection_of_nullable_ints_with_paging2();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Project_collection_of_nullable_ints_with_paging3() =>
        base.Project_collection_of_nullable_ints_with_paging3();

    [Fact]
    [SpecEngineLimitationFact("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    public override Task Project_inline_collection_with_Union() => base.Project_inline_collection_with_Union();
}
