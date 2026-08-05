namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlAnnotationNames
{
    public const string Prefix = "Doka:MySql:";

    public const string GuidFormat = Prefix + nameof(GuidFormat);

    public const string ValueGenerationStrategy = Prefix + nameof(ValueGenerationStrategy);

    public const string CharSet = Prefix + nameof(CharSet);

    public const string StorageEngine = Prefix + nameof(StorageEngine);

    public const string SpatialReferenceSystemId = Prefix + nameof(SpatialReferenceSystemId);

    public const string SpatialIndex = Prefix + nameof(SpatialIndex);

    public const string FullTextIndex = Prefix + nameof(FullTextIndex);

    public const string HiLoSequenceName = Prefix + nameof(HiLoSequenceName);

    public const string Invisible = Prefix + nameof(Invisible);

    public const string IndexPrefixLength = Prefix + nameof(IndexPrefixLength);

    public const string Collation = Prefix + nameof(Collation);

    public const string Comment = Prefix + nameof(Comment);

    public const string ScaffoldingCheckConstraints = Prefix + nameof(ScaffoldingCheckConstraints);

    public const string ScaffoldingIndexParts = Prefix + nameof(ScaffoldingIndexParts);

    public const string IsTemporal = Prefix + nameof(IsTemporal);

    public const string TemporalHistoryTableName = Prefix + nameof(TemporalHistoryTableName);

    public const string TemporalHistoryTableSchema = Prefix + nameof(TemporalHistoryTableSchema);

    public const string TemporalPeriodStartPropertyName = Prefix + nameof(TemporalPeriodStartPropertyName);

    public const string TemporalPeriodEndPropertyName = Prefix + nameof(TemporalPeriodEndPropertyName);

    public const string TemporalOperation = Prefix + nameof(TemporalOperation);

    public const string TemporalPointInTime = Prefix + nameof(TemporalPointInTime);

    public const string TemporalRangeStart = Prefix + nameof(TemporalRangeStart);

    public const string TemporalRangeEnd = Prefix + nameof(TemporalRangeEnd);

    public const string TemporalHistoryTable = Prefix + nameof(TemporalHistoryTable);

    public const string TemporalHistorySchema = Prefix + nameof(TemporalHistorySchema);

    public const string TemporalPeriodStartColumn = Prefix + nameof(TemporalPeriodStartColumn);

    public const string TemporalPeriodEndColumn = Prefix + nameof(TemporalPeriodEndColumn);

    public const string TemporalSourceIsTemporal = Prefix + nameof(TemporalSourceIsTemporal);

    public const string TemporalSourceHistoryTable = Prefix + nameof(TemporalSourceHistoryTable);

    public const string TemporalSourceHistorySchema = Prefix + nameof(TemporalSourceHistorySchema);

    public const string TemporalSourcePeriodStartColumn = Prefix + nameof(TemporalSourcePeriodStartColumn);

    public const string TemporalSourcePeriodEndColumn = Prefix + nameof(TemporalSourcePeriodEndColumn);
}
