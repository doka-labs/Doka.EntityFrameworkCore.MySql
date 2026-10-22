namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlLikeIntegrationTests
{
    private static readonly LikeCase[] s_scalarCases = CreateScalarCases();

    private static IEnumerable<IntegrationDatabaseTarget> SelectedTargets() =>
        IntegrationTestEnvironment.GetSupportedTargets().Where(IntegrationTestEnvironment.IsTargetSelected);

    /// <summary>
    /// Expands scalar and null/escaping boundaries into separately selectable rows.
    /// </summary>
    private static LikeCase[] CreateScalarCases()
    {
        var result = new List<LikeCase>();
        string[] numericProperties =
        [
            nameof(LikeItem.ByteValue), nameof(LikeItem.SByteValue),
            nameof(LikeItem.Int16Value), nameof(LikeItem.UInt16Value),
            nameof(LikeItem.Int32Value), nameof(LikeItem.UInt32Value),
            nameof(LikeItem.Int64Value), nameof(LikeItem.UInt64Value),
            nameof(LikeItem.SingleValue), nameof(LikeItem.DoubleValue), nameof(LikeItem.DecimalValue),
        ];

        foreach (var property in numericProperties)
        {
            result.Add(new LikeCase(property, "Match", "%23%", [1]));
            result.Add(new LikeCase($"Optional{property}", "Match", "%23%", [1]));
            result.Add(new LikeCase($"Optional{property}", "NonNullWildcard", "%", [1]));
            result.Add(new LikeCase(property, "NoMatch", "absent", []));
        }

        result.Add(new LikeCase(nameof(LikeItem.CreatedAt), "Match", "2026-08-25 12:34:56.123%", [1]));
        result.Add(new LikeCase(nameof(LikeItem.OptionalCreatedAt), "Match", "2026-08-25%", [1]));
        result.Add(new LikeCase(nameof(LikeItem.BinaryToken), "Match", "00112233-%-aabbccddeeff", [1]));
        result.Add(new LikeCase(nameof(LikeItem.TextToken), "Match", "00112233-%-aabbccddeeff", [1]));
        result.Add(new LikeCase(nameof(LikeItem.OptionalBinaryToken), "Match", "00112233-%", [1]));
        result.Add(new LikeCase(nameof(LikeItem.OptionalTextToken), "Match", "00112233-%", [1]));
        result.Add(new LikeCase(nameof(LikeItem.OptionalBinaryToken), "NonNullWildcard", "%", [1]));
        result.Add(new LikeCase(nameof(LikeItem.OptionalTextToken), "NonNullWildcard", "%", [1]));
        result.Add(new LikeCase(nameof(LikeItem.Text), "Match", "sale%", [1]));
        result.Add(new LikeCase(nameof(LikeItem.OptionalText), "NonNullWildcard", "%", [1]));

        foreach (var property in new[]
                 {
                     nameof(LikeItem.OptionalInt32Value), nameof(LikeItem.OptionalCreatedAt),
                     nameof(LikeItem.OptionalBinaryToken), nameof(LikeItem.OptionalTextToken),
                     nameof(LikeItem.OptionalText),
                 })
        {
            result.Add(new LikeCase(property, "NullPattern", null, []));
        }

        result.Add(new LikeCase(nameof(LikeItem.OptionalInt32Value), "EscapedMatch", "%23", [1], true, "!"));
        result.Add(new LikeCase(nameof(LikeItem.OptionalCreatedAt), "EscapedMatch", "2026-08%", [1], true, "!"));
        result.Add(new LikeCase(nameof(LikeItem.OptionalBinaryToken), "EscapedMatch", "00112233-%", [1], true, "!"));
        result.Add(new LikeCase(nameof(LikeItem.OptionalTextToken), "EscapedMatch", "00112233-%", [1], true, "!"));
        result.Add(new LikeCase(nameof(LikeItem.OptionalText), "EscapedMatch", "sale!_25!%!!", [1], true, "!"));

        foreach (var property in new[]
                 {
                     nameof(LikeItem.OptionalInt32Value),
                     nameof(LikeItem.OptionalBinaryToken),
                     nameof(LikeItem.OptionalText),
                 })
        {
            result.Add(new LikeCase(property, "NullEscape", "%", [], true, null));
        }

        foreach (var property in new[]
                 {
                     nameof(LikeItem.Int32Value), nameof(LikeItem.BinaryToken), nameof(LikeItem.Text),
                 })
        {
            // WHY: The pattern must stay parameter data even when it resembles a SQL predicate.
            result.Add(new LikeCase(property, "InjectionLikePattern", "%' OR 1=1 --", []));
        }

        return result.ToArray();
    }

    /// <summary>
    /// Retains the original generic scalar overload for every CLR property type.
    /// </summary>
    private static IQueryable<int> CreateLikeQuery(
        LikeContext context,
        LikeCase testCase
    ) => testCase.PropertyName switch
    {
        nameof(LikeItem.ByteValue) => CreateLikeQuery<byte>(context, testCase),
        nameof(LikeItem.SByteValue) => CreateLikeQuery<sbyte>(context, testCase),
        nameof(LikeItem.Int16Value) => CreateLikeQuery<short>(context, testCase),
        nameof(LikeItem.UInt16Value) => CreateLikeQuery<ushort>(context, testCase),
        nameof(LikeItem.Int32Value) => CreateLikeQuery<int>(context, testCase),
        nameof(LikeItem.UInt32Value) => CreateLikeQuery<uint>(context, testCase),
        nameof(LikeItem.Int64Value) => CreateLikeQuery<long>(context, testCase),
        nameof(LikeItem.UInt64Value) => CreateLikeQuery<ulong>(context, testCase),
        nameof(LikeItem.SingleValue) => CreateLikeQuery<float>(context, testCase),
        nameof(LikeItem.DoubleValue) => CreateLikeQuery<double>(context, testCase),
        nameof(LikeItem.DecimalValue) => CreateLikeQuery<decimal>(context, testCase),
        nameof(LikeItem.OptionalByteValue) => CreateLikeQuery<byte?>(context, testCase),
        nameof(LikeItem.OptionalSByteValue) => CreateLikeQuery<sbyte?>(context, testCase),
        nameof(LikeItem.OptionalInt16Value) => CreateLikeQuery<short?>(context, testCase),
        nameof(LikeItem.OptionalUInt16Value) => CreateLikeQuery<ushort?>(context, testCase),
        nameof(LikeItem.OptionalInt32Value) => CreateLikeQuery<int?>(context, testCase),
        nameof(LikeItem.OptionalUInt32Value) => CreateLikeQuery<uint?>(context, testCase),
        nameof(LikeItem.OptionalInt64Value) => CreateLikeQuery<long?>(context, testCase),
        nameof(LikeItem.OptionalUInt64Value) => CreateLikeQuery<ulong?>(context, testCase),
        nameof(LikeItem.OptionalSingleValue) => CreateLikeQuery<float?>(context, testCase),
        nameof(LikeItem.OptionalDoubleValue) => CreateLikeQuery<double?>(context, testCase),
        nameof(LikeItem.OptionalDecimalValue) => CreateLikeQuery<decimal?>(context, testCase),
        nameof(LikeItem.CreatedAt) => CreateLikeQuery<DateTime>(context, testCase),
        nameof(LikeItem.OptionalCreatedAt) => CreateLikeQuery<DateTime?>(context, testCase),
        nameof(LikeItem.BinaryToken) or nameof(LikeItem.TextToken) => CreateLikeQuery<Guid>(context, testCase),
        nameof(LikeItem.OptionalBinaryToken) or nameof(LikeItem.OptionalTextToken) =>
            CreateLikeQuery<Guid?>(context, testCase),
        nameof(LikeItem.Text) or nameof(LikeItem.OptionalText) => CreateLikeQuery<string?>(context, testCase),
        _ => throw new ArgumentOutOfRangeException(nameof(testCase)),
    };

    private static IQueryable<int> CreateLikeQuery<T>(
        LikeContext context,
        LikeCase testCase
    )
    {
        var propertyName = testCase.PropertyName;
        var pattern = testCase.Pattern;
        var escapeCharacter = testCase.EscapeCharacter;
        var query = testCase.HasEscape
            ? context.Items.Where(item =>
                EF.Functions.Like(EF.Property<T>(item, propertyName), pattern, escapeCharacter))
            : context.Items.Where(item =>
                EF.Functions.Like(EF.Property<T>(item, propertyName), pattern));

        return query.OrderBy(item => item.Id).Select(item => item.Id);
    }

    private sealed record LikeCase(
        string PropertyName,
        string Name,
        string? Pattern,
        int[] ExpectedIds,
        bool HasEscape = false,
        string? EscapeCharacter = null);
}
