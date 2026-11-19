namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Creates one independently typed parameter query for the membership regression cases.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterTests
{
    /// <summary>Selects the existing model mapping without adding another provider ownership policy.</summary>
    private static IQueryable<PrimitiveCollectionEntity> CreateIndependentMembershipQuery(
        PrimitiveCollectionContext context,
        string representation
    )
    {
        Guid[] guids = [s_first, s_second];
        string[] text = ["alpha", "'quoted'", "\\path"];
        CollectionStatus[] statuses = [CollectionStatus.Active];
        StrongGuid[] convertedGuids = [new(s_first)];
        byte[][] bytes = [[1, 2, 3]];
        TimeSpan[] durations = [TimeSpan.FromHours(25)];

        return representation switch
        {
            "binary-guid" => context.Items.Where(item => EF
                .Parameter(guids)
                .Contains(item.BinaryId)),
            "char-guid" => context.Items.Where(item => EF
                .Parameter(guids)
                .Contains(item.CharId)),
            "string" => context.Items.Where(item => EF
                .Parameter(text)
                .Contains(item.Name)),
            "converted-string" => context.Items.Where(item => EF
                .Parameter(text)
                .Contains(item.ConvertedName)),
            "converted-enum" => context.Items.Where(item => EF
                .Parameter(statuses)
                .Contains(item.Status)),
            "converted-guid" => context.Items.Where(item => EF
                .Parameter(convertedGuids)
                .Contains(item.ConvertedGuid)),
            "bytes" => context.Items.Where(item => EF
                .Parameter(bytes)
                .Contains(item.Payload)),
            "duration" => context.Items.Where(item => EF
                .Parameter(durations)
                .Contains(item.Duration)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(representation),
                representation,
                "Unknown membership representation."),
        };
    }
}
