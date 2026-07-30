using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

public sealed partial class ManyToManyNoTrackingQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

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
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

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
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

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
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

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
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

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
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(
        bool async
    ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task
        Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
            bool async
        ) => base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(
        async);
}
