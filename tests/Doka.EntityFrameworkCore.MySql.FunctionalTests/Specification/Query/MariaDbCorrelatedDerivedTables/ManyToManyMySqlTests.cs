using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

public sealed partial class ManyToManyNoTrackingQueryMySqlTest
{
    [Theory]
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

    [Theory]
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
            bool async
        ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
        async);
}

public sealed partial class ManyToManyQueryMySqlTest
{
    [Theory]
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

    [Theory]
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
            bool async
        ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
        async);
}

public sealed partial class TpcManyToManyNoTrackingQueryMySqlTest
{
    [Theory]
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

    [Theory]
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
            bool async
        ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
        async);
}

public sealed partial class TpcManyToManyQueryMySqlTest
{
    [Theory]
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

    [Theory]
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
            bool async
        ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
        async);
}

public sealed partial class TptManyToManyNoTrackingQueryMySqlTest
{
    [Theory]
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

    [Theory]
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
            bool async
        ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
        async);
}

public sealed partial class TptManyToManyQueryMySqlTest
{
    [Theory]
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

    [Theory]
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
            bool async
        ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
        async);
}
