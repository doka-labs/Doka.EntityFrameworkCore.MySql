namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlValueGenerationConvention : IModelFinalizingConvention
{
    private readonly MySqlSingletonOptions _singletonOptions;

    public MySqlValueGenerationConvention(
        IEnumerable<ISingletonOptions> singletonOptions
    )
    {
        ArgumentNullException.ThrowIfNull(singletonOptions);

        _singletonOptions = singletonOptions
            .OfType<MySqlSingletonOptions>()
            .Single();
    }

    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context
    )
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToArray())
        {
            foreach (var property in entityType.GetProperties().ToArray())
            {
                ApplyGuidFormat(property);
                ApplyValueGenerationStrategy(entityType, property);
            }
        }
    }

    private void ApplyGuidFormat(
        IConventionProperty property
    )
    {
        var clrType = Nullable.GetUnderlyingType(property.ClrType)
            ?? property.ClrType;

        if (clrType != typeof(Guid))
        {
            return;
        }

        var mutableProperty = (IMutableProperty)property;
        var explicitStoreType = property.GetColumnType();
        var format = property.GetMySqlGuidFormat();

        // A converter or provider CLR type is an application-level storage
        // contract. Replacing it would let nullable and non-nullable members of
        // the same FK resolve to different physical representations.
        if (format is null
            && (property.GetValueConverter() is not null
                || property.GetProviderClrType() is not null))
        {
            return;
        }

        if (format is null
            && TryApplyExplicitStoreTypeGuidMapping(mutableProperty, explicitStoreType))
        {
            return;
        }

        format ??= _singletonOptions.DefaultGuidFormat;

        mutableProperty.SetMySqlGuidFormat(format);

        switch (format)
        {
            case MySqlGuidFormat.Binary16:
                mutableProperty.SetMaxLength(16);
                mutableProperty.SetIsFixedLength(true);
                mutableProperty.SetColumnType("binary(16)");
                mutableProperty.SetProviderClrType(
                    _singletonOptions.DefaultGuidFormat == MySqlGuidFormat.Char36
                        ? typeof(byte[])
                        : null);
                mutableProperty.SetValueConverter(
                    _singletonOptions.DefaultGuidFormat == MySqlGuidFormat.Char36
                        ? MySqlGuidToBytesConverter.Default
                        : null);
                break;
            case MySqlGuidFormat.Char36:
                mutableProperty.SetMaxLength(36);
                mutableProperty.SetIsFixedLength(true);
                mutableProperty.SetColumnType("char(36)");
                mutableProperty.SetProviderClrType(typeof(string));
                mutableProperty.SetValueConverter(new GuidToStringConverter());
                break;
            default:
                ThrowUnsupportedGuidFormat(format.Value);
                break;
        }
    }

    private bool TryApplyExplicitStoreTypeGuidMapping(
        IMutableProperty property,
        string? explicitStoreType
    )
    {
        if (string.IsNullOrWhiteSpace(explicitStoreType))
        {
            return false;
        }

        var normalizedStoreType = explicitStoreType
            .Trim()
            .ToLowerInvariant();

        switch (normalizedStoreType)
        {
            case "char(36)":
                property.SetMySqlGuidFormat(MySqlGuidFormat.Char36);
                property.SetMaxLength(36);
                property.SetIsFixedLength(true);
                property.SetColumnType("char(36)");
                property.SetProviderClrType(typeof(string));
                property.SetValueConverter(new GuidToStringConverter());

                return true;

            case "varchar(36)":
                property.SetMaxLength(36);
                property.SetIsFixedLength(false);
                property.SetColumnType("varchar(36)");
                property.SetProviderClrType(typeof(string));
                property.SetValueConverter(new GuidToStringConverter());

                return true;

            case "binary(16)":
                property.SetMySqlGuidFormat(MySqlGuidFormat.Binary16);
                property.SetMaxLength(16);
                property.SetIsFixedLength(true);
                property.SetColumnType("binary(16)");
                property.SetProviderClrType(
                    _singletonOptions.DefaultGuidFormat == MySqlGuidFormat.Char36
                        ? typeof(byte[])
                        : null);
                property.SetValueConverter(
                    _singletonOptions.DefaultGuidFormat == MySqlGuidFormat.Char36
                        ? MySqlGuidToBytesConverter.Default
                        : null);

                return true;

            default:
                return false;
        }
    }

    private static void ThrowUnsupportedGuidFormat(
        MySqlGuidFormat format
    )
    {
        throw new ArgumentOutOfRangeException(
            nameof(format),
            format,
            $"Unsupported {nameof(MySqlGuidFormat)} value: {format}");
    }

    private static void ApplyValueGenerationStrategy(
        IReadOnlyEntityType entityType,
        IConventionProperty property
    )
    {
        var mutableProperty = (IMutableProperty)property;
        var configuredStrategy = property.GetMySqlValueGenerationStrategy();

        if (configuredStrategy is not null)
        {
            ApplyExplicitStrategy(mutableProperty, configuredStrategy.Value);
            return;
        }

        if (property.ClrType == typeof(Guid))
        {
            var generatedOnAdd = property.ValueGenerated == ValueGenerated.OnAdd;

            mutableProperty.SetMySqlValueGenerationStrategy(
                generatedOnAdd
                    ? MySqlValueGenerationStrategy.ClientGuid
                    : MySqlValueGenerationStrategy.None);
            mutableProperty.ValueGenerated = generatedOnAdd
                ? ValueGenerated.OnAdd
                : ValueGenerated.Never;
            return;
        }

        if (IsAutoIncrementIntegerKey(entityType, property))
        {
            mutableProperty.SetMySqlValueGenerationStrategy(MySqlValueGenerationStrategy.AutoIncrement);
            mutableProperty.ValueGenerated = ValueGenerated.OnAdd;
            return;
        }

        // Evaluate generated-key conventions before materializing None. This
        // allows EF's convention-level ValueGenerated.Never on owned collection
        // keys to become AUTO_INCREMENT while preserving stable metadata for
        // every property that remains non-generated.
        if (property.ValueGenerated == ValueGenerated.Never)
        {
            mutableProperty.SetMySqlValueGenerationStrategy(MySqlValueGenerationStrategy.None);
        }
    }

    private static void ApplyExplicitStrategy(
        IMutableProperty property,
        MySqlValueGenerationStrategy strategy
    ) => property.ValueGenerated = strategy switch
    {
        MySqlValueGenerationStrategy.None => ValueGenerated.Never,
        MySqlValueGenerationStrategy.AutoIncrement
            or MySqlValueGenerationStrategy.ClientGuid
            or MySqlValueGenerationStrategy.HiLo => ValueGenerated.OnAdd,
        _ => throw new ArgumentOutOfRangeException(nameof(strategy))
    };

    private static bool IsAutoIncrementIntegerKey(
        IReadOnlyEntityType entityType,
        IConventionProperty property
    )
    {
        var primaryKey = entityType.FindPrimaryKey();
        var ownership = entityType.FindOwnership();
        var isConventionallyGeneratedOwnedCollectionKey =
            ownership is not null
            && !ownership.Properties.Contains(property)
            && primaryKey is not null
            && primaryKey.Properties.Count(
                keyProperty => !ownership.Properties.Contains(keyProperty)) == 1
            && property.GetValueGeneratedConfigurationSource()
                is null
                or ConfigurationSource.Convention;

        if (primaryKey is null
            || !primaryKey.Properties.Contains(property)
            || (property.ValueGenerated != ValueGenerated.OnAdd
                && !isConventionallyGeneratedOwnedCollectionKey)
            || property.FindAnnotation(RelationalAnnotationNames.DefaultValue) is not null
            || property.GetDefaultValueSql() is not null
            || property.GetComputedColumnSql() is not null)
        {
            return false;
        }

        // AUTO_INCREMENT applies to the store representation, not necessarily
        // the model CLR type. An integer key converted to string or byte[] must
        // not inherit the integer convention and produce invalid MySQL DDL.
        var providerClrType = property.GetValueConverter()
                ?.ProviderClrType
            ?? property.GetProviderClrType()
            ?? property.ClrType;

        providerClrType = Nullable.GetUnderlyingType(providerClrType) ?? providerClrType;

        if (providerClrType.IsEnum)
        {
            providerClrType = Enum.GetUnderlyingType(providerClrType);
        }

        return providerClrType == typeof(byte)
            || providerClrType == typeof(short)
            || providerClrType == typeof(int)
            || providerClrType == typeof(long);
    }
}
