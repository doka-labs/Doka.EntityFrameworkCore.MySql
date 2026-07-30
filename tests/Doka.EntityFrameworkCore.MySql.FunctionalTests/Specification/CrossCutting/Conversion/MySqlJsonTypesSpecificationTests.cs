using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Conversion;

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class JsonTypesMySqlTest : JsonTypesRelationalTestBase
{
    public JsonTypesMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    public override Task Can_read_write_DateTimeOffset_JSON_values(
        string value,
        string json
    ) => base.Can_read_write_DateTimeOffset_JSON_values(
        value,
        value switch
        {
            "0001-01-01T00:00:00.0000000-01:00" => """{"Prop":"0001-01-01 00:00:00-01:00"}""",
            "9999-12-31T23:59:59.9999999+02:00" => """{"Prop":"9999-12-31 23:59:59.9999999\u002B02:00"}""",
            "0001-01-01T00:00:00.0000000-03:00" => """{"Prop":"0001-01-01 00:00:00-03:00"}""",
            "2023-05-29T11:11:15.5672854+04:00" => """{"Prop":"2023-05-29 11:11:15.5672854\u002B04:00"}""",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        });

    public override Task Can_read_write_nullable_DateTimeOffset_JSON_values(
        string? value,
        string json
    ) => base.Can_read_write_nullable_DateTimeOffset_JSON_values(
        value,
        value switch
        {
            "0001-01-01T00:00:00.0000000-01:00" => """{"Prop":"0001-01-01 00:00:00-01:00"}""",
            "9999-12-31T23:59:59.9999999+02:00" => """{"Prop":"9999-12-31 23:59:59.9999999\u002B02:00"}""",
            "0001-01-01T00:00:00.0000000-03:00" => """{"Prop":"0001-01-01 00:00:00-03:00"}""",
            "2023-05-29T11:11:15.5672854+04:00" => """{"Prop":"2023-05-29 11:11:15.5672854\u002B04:00"}""",
            null => """{"Prop":null}""",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        });

    public override Task Can_read_write_collection_of_DateTimeOffset_JSON_values(
        string expected
    ) => base.Can_read_write_collection_of_DateTimeOffset_JSON_values(
        "{\"Prop\":[\"0001-01-01 00:00:00\\u002B00:00\","
        + "\"2023-05-29 10:52:47-02:00\","
        + "\"2023-05-29 10:52:47\\u002B00:00\","
        + "\"2023-05-29 10:52:47\\u002B02:00\","
        + "\"9999-12-31 23:59:59.9999999\\u002B00:00\"]}");

    public override Task Can_read_write_collection_of_nullable_DateTimeOffset_JSON_values(
        string expected
    ) => base.Can_read_write_collection_of_nullable_DateTimeOffset_JSON_values(
        "{\"Prop\":[\"0001-01-01 00:00:00\\u002B00:00\","
        + "\"2023-05-29 10:52:47-02:00\","
        + "\"2023-05-29 10:52:47\\u002B00:00\",null,"
        + "\"2023-05-29 10:52:47\\u002B02:00\","
        + "\"9999-12-31 23:59:59.9999999\\u002B00:00\"]}");

    public override Task Can_read_write_ulong_enum_JSON_values(
        EnumU64 value,
        string json
    ) => Can_read_and_write_JSON_value<EnumU64Type, EnumU64>(nameof(EnumU64Type.EnumU64), value, json);

    public override Task Can_read_write_nullable_ulong_enum_JSON_values(
        object? value,
        string json
    ) => Can_read_and_write_JSON_value<NullableEnumU64Type, EnumU64?>(
        nameof(NullableEnumU64Type.EnumU64),
        value is null ? null : (EnumU64)value,
        json);

    public override Task Can_read_write_collection_of_ulong_enum_JSON_values() =>
        Can_read_and_write_JSON_value<EnumU64CollectionType, List<EnumU64>>(
            nameof(EnumU64CollectionType.EnumU64),
            [
                EnumU64.Min,
                EnumU64.Max,
                EnumU64.Default,
                EnumU64.One,
                (EnumU64)8,
            ],
            """{"Prop":[0,18446744073709551615,0,1,8]}""",
            mappedCollection: true);

    public override Task Can_read_write_collection_of_nullable_ulong_enum_JSON_values() =>
        Can_read_and_write_JSON_value<NullableEnumU64CollectionType, List<EnumU64?>>(
            nameof(NullableEnumU64CollectionType.EnumU64),
            [
                EnumU64.Min,
                null,
                EnumU64.Max,
                EnumU64.Default,
                EnumU64.One,
                (EnumU64?)8,
            ],
            """{"Prop":[0,null,18446744073709551615,0,1,8]}""",
            mappedCollection: true);

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    protected override DbContextOptionsBuilder AddOptions(
        DbContextOptionsBuilder builder
    )
    {
        var optionsBuilder = base.AddOptions(builder);

        new MySqlDbContextOptionsBuilder(optionsBuilder).UseNetTopologySuite();

        return optionsBuilder;
    }
}
