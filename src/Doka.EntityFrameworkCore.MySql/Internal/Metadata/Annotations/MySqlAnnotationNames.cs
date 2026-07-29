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
}
