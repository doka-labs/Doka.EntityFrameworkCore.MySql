namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Maps EF's conventional <c>byte[]</c> row-version property to a MySQL
/// <c>timestamp(6)</c> value with structural snapshot comparison.
/// </summary>
public sealed class MySqlRowVersionTypeMapping : DateTimeTypeMapping
{
    /// <summary>
    /// Gets the canonical mapping used as the cloning source for generated compiled models.
    /// </summary>
    public static new MySqlRowVersionTypeMapping Default { get; } = new();

    /// <summary>
    /// Creates the conventional MySQL <c>byte[]</c> row-version mapping.
    /// </summary>
    public MySqlRowVersionTypeMapping() : base(
        new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                typeof(byte[]),
                new MySqlBytesToDateTimeConverter(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<byte[]>(
                    favorStructuralComparisons: true)),
            "timestamp(6)",
            StoreTypePostfix.Precision,
            System.Data.DbType.DateTime,
            unicode: false,
            size: null,
            fixedLength: false,
            precision: 6,
            scale: null))
    { }

    private MySqlRowVersionTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlRowVersionTypeMapping(parameters);
}
