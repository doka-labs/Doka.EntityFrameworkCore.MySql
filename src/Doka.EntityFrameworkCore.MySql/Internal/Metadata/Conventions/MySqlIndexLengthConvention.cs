namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Sizes implicit variable-length key and index properties against the complete
/// MySQL index-byte budget rather than treating every column in isolation.
/// </summary>
internal sealed class MySqlIndexLengthConvention : IModelFinalizingConvention
{
    private const int MaximumIndexBytes = 3072;
    private const int MaximumImplicitLength = 255;
    private const int Utf8Mb4BytesPerCharacter = 4;

    // Every supported fixed-width scalar, including binary GUIDs and decimals,
    // fits within this reserve. The remaining budget is divided between the
    // variable-width properties in the same key or index.
    private const int FixedPropertyReserveBytes = 32;

    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context
    )
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var key in entityType.GetKeys())
            {
                ApplySharedByteBudget(key.Properties);
            }

            foreach (var index in entityType.GetIndexes())
            {
                if (!index.GetMySqlFullTextIndex()
                    && !index.GetMySqlSpatialIndex())
                {
                    ApplySharedByteBudget(index.Properties);
                }
            }
        }
    }

    private static void ApplySharedByteBudget(
        IReadOnlyList<IConventionProperty> properties
    )
    {
        var variableProperties = properties
            .Where(IsImplicitVariableLengthProperty)
            .ToArray();

        if (variableProperties.Length == 0)
        {
            return;
        }

        var fixedPropertyCount = properties.Count - variableProperties.Length;
        var availableBytes = Math.Max(1, MaximumIndexBytes - (fixedPropertyCount * FixedPropertyReserveBytes));
        var totalWeight = variableProperties.Sum(static property => IsString(property) ? Utf8Mb4BytesPerCharacter : 1);
        var length = Math.Min(MaximumImplicitLength, Math.Max(1, availableBytes / totalWeight));

        foreach (var property in variableProperties)
        {
            if (property.GetMaxLength() is null
                || property.GetMaxLength() > length)
            {
                ((IMutableProperty)property).SetMaxLength(length);
            }
        }
    }

    private static bool IsImplicitVariableLengthProperty(
        IConventionProperty property
    )
    {
        var clrType = property.ClrType.UnwrapNullableType();

        if (clrType != typeof(string)
            && clrType != typeof(byte[]))
        {
            return false;
        }

        return property.GetMaxLengthConfigurationSource() is null or ConfigurationSource.Convention
            && property.GetColumnTypeConfigurationSource() is null or ConfigurationSource.Convention;
    }

    private static bool IsString(
        IReadOnlyProperty property
    ) => property.ClrType.UnwrapNullableType() == typeof(string);
}
