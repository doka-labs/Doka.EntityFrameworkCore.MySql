using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Types;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Storage;

public sealed class ByteTypeMySqlFixture : RelationalTypeFixtureBase<byte>
{
    public override byte Value { get; } = byte.MinValue;

    public override byte OtherValue { get; } = byte.MaxValue;

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ByteTypeMySqlTest : RelationalTypeTestBase<byte, ByteTypeMySqlFixture>
{
    public ByteTypeMySqlTest(
        ByteTypeMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class ShortTypeMySqlFixture : RelationalTypeFixtureBase<short>
{
    public override short Value { get; } = short.MinValue;

    public override short OtherValue { get; } = short.MaxValue;

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ShortTypeMySqlTest : RelationalTypeTestBase<short, ShortTypeMySqlFixture>
{
    public ShortTypeMySqlTest(
        ShortTypeMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class IntTypeMySqlFixture : RelationalTypeFixtureBase<int>
{
    public override int Value { get; } = int.MinValue;

    public override int OtherValue { get; } = int.MaxValue;

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class IntTypeMySqlTest : RelationalTypeTestBase<int, IntTypeMySqlFixture>
{
    public IntTypeMySqlTest(
        IntTypeMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class LongTypeMySqlFixture : RelationalTypeFixtureBase<long>
{
    public override long Value { get; } = long.MinValue;

    public override long OtherValue { get; } = long.MaxValue;

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class LongTypeMySqlTest : RelationalTypeTestBase<long, LongTypeMySqlFixture>
{
    public LongTypeMySqlTest(
        LongTypeMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class DecimalTypeMySqlFixture : RelationalTypeFixtureBase<decimal>
{
    public override decimal Value { get; } = 30.5m;

    public override decimal OtherValue { get; } = 30m;

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class DecimalTypeMySqlTest : RelationalTypeTestBase<decimal, DecimalTypeMySqlFixture>
{
    public DecimalTypeMySqlTest(
        DecimalTypeMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class DoubleTypeMySqlFixture : RelationalTypeFixtureBase<double>
{
    public override double Value { get; } = 30.5d;

    public override double OtherValue { get; } = 30d;

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class DoubleTypeMySqlTest : RelationalTypeTestBase<double, DoubleTypeMySqlFixture>
{
    public DoubleTypeMySqlTest(
        DoubleTypeMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class FloatTypeMySqlFixture : RelationalTypeFixtureBase<float>
{
    public override float Value { get; } = 30.5f;

    public override float OtherValue { get; } = 30f;

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class FloatTypeMySqlTest : RelationalTypeTestBase<float, FloatTypeMySqlFixture>
{
    public FloatTypeMySqlTest(
        FloatTypeMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class BoolTypeMySqlFixture : RelationalTypeFixtureBase<bool>
{
    public override bool Value { get; } = true;

    public override bool OtherValue => false;

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BoolTypeMySqlTest : RelationalTypeTestBase<bool, BoolTypeMySqlFixture>
{
    public BoolTypeMySqlTest(
        BoolTypeMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class StringTypeMySqlFixture : RelationalTypeFixtureBase<string>
{
    public override string Value { get; } = "foo";

    public override string OtherValue { get; } = "bar";

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class StringTypeMySqlTest : RelationalTypeTestBase<string, StringTypeMySqlFixture>
{
    public StringTypeMySqlTest(
        StringTypeMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class GuidTypeMySqlFixture : RelationalTypeFixtureBase<Guid>
{
    public override Guid Value { get; } = new("8f7331d6-cde9-44fb-8611-81fff686f280");

    public override Guid OtherValue { get; } = new("ae192c36-9004-49b2-b785-8be10d169627");

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class GuidTypeMySqlTest : RelationalTypeTestBase<Guid, GuidTypeMySqlFixture>
{
    public GuidTypeMySqlTest(
        GuidTypeMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class ByteArrayTypeMySqlFixture : RelationalTypeFixtureBase<byte[]>
{
    public override byte[] Value { get; } =
    [
        1,
        2,
        3
    ];

    public override byte[] OtherValue { get; } =
    [
        4,
        5,
        6,
        7
    ];

    public override Func<byte[], byte[], bool> Comparer { get; } = (
        left,
        right
    ) => left.SequenceEqual(right);

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ByteArrayTypeMySqlTest : RelationalTypeTestBase<byte[], ByteArrayTypeMySqlFixture>
{
    public ByteArrayTypeMySqlTest(
        ByteArrayTypeMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class DateTimeTypeMySqlFixture : RelationalTypeFixtureBase<DateTime>
{
    public override DateTime Value { get; } = new(
        2020,
        1,
        5,
        12,
        30,
        45,
        DateTimeKind.Unspecified);

    public override DateTime OtherValue { get; } = new(
        2022,
        5,
        3,
        0,
        0,
        0,
        DateTimeKind.Unspecified);

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class DateTimeTypeMySqlTest : RelationalTypeTestBase<DateTime, DateTimeTypeMySqlFixture>
{
    public DateTimeTypeMySqlTest(
        DateTimeTypeMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class DateTimeOffsetTypeMySqlFixture : RelationalTypeFixtureBase<DateTimeOffset>
{
    public override DateTimeOffset Value { get; } = new(
        2020,
        1,
        5,
        12,
        30,
        45,
        TimeSpan.FromHours(2));

    public override DateTimeOffset OtherValue { get; } = new(
        2020,
        1,
        5,
        12,
        30,
        45,
        TimeSpan.FromHours(3));

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class DateTimeOffsetTypeMySqlTest : RelationalTypeTestBase<DateTimeOffset, DateTimeOffsetTypeMySqlFixture>
{
    public DateTimeOffsetTypeMySqlTest(
        DateTimeOffsetTypeMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class DateOnlyTypeMySqlFixture : RelationalTypeFixtureBase<DateOnly>
{
    public override DateOnly Value { get; } = new(2020, 1, 5);

    public override DateOnly OtherValue { get; } = new(2022, 5, 3);

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class DateOnlyTypeMySqlTest : RelationalTypeTestBase<DateOnly, DateOnlyTypeMySqlFixture>
{
    public DateOnlyTypeMySqlTest(
        DateOnlyTypeMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class TimeOnlyTypeMySqlFixture : RelationalTypeFixtureBase<TimeOnly>
{
    public override TimeOnly Value { get; } = new(12, 30, 45);

    public override TimeOnly OtherValue { get; } = new(14, 0, 0);

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TimeOnlyTypeMySqlTest : RelationalTypeTestBase<TimeOnly, TimeOnlyTypeMySqlFixture>
{
    public TimeOnlyTypeMySqlTest(
        TimeOnlyTypeMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class TimeSpanTypeMySqlFixture : RelationalTypeFixtureBase<TimeSpan>
{
    public override TimeSpan Value { get; } = new(12, 30, 45);

    public override TimeSpan OtherValue { get; } = new(14, 0, 0);

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TimeSpanTypeMySqlTest : RelationalTypeTestBase<TimeSpan, TimeSpanTypeMySqlFixture>
{
    public TimeSpanTypeMySqlTest(
        TimeSpanTypeMySqlFixture fixture
    ) : base(fixture) { }
}
