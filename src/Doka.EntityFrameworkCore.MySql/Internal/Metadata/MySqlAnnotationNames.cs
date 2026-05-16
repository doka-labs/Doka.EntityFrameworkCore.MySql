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

    public const string HiLoSequenceName = Prefix + nameof(HiLoSequenceName);

    public const string Invisible = Prefix + nameof(Invisible);

    public const string IndexPrefixLength = Prefix + nameof(IndexPrefixLength);
}
