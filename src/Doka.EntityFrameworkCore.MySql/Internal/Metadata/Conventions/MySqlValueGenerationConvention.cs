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

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
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
            && TryApplyExplicitTextGuidMapping(mutableProperty, explicitStoreType))
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
                mutableProperty.SetProviderClrType(null);
                mutableProperty.SetValueConverter((ValueConverter?)null);
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

    private static bool TryApplyExplicitTextGuidMapping(
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
                property.SetProviderClrType(null);
                property.SetValueConverter((ValueConverter?)null);

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

        // Respect an explicit `ValueGeneratedNever()` from the user model. EF Core stores
        // that decision on the core ValueGenerated facet; the auto-increment default below
        // must not override it. Without this guard every integer-key entity gets
        // AUTO_INCREMENT regardless of intent, which breaks modification-batch protocols
        // that read back the generated key when the user provides an explicit primary key
        // (the round-trip "fetch back the generated Id" path collides with the explicit
        // value the user already supplied and the server reports as a duplicate-PK).
        if (property.ValueGenerated == ValueGenerated.Never)
        {
            mutableProperty.SetMySqlValueGenerationStrategy(MySqlValueGenerationStrategy.None);
            return;
        }

        if (property.ClrType == typeof(Guid))
        {
            var explicitlyGeneratedOnAdd =
                property.ValueGenerated == ValueGenerated.OnAdd
                && property.GetValueGeneratedConfigurationSource()
                    is ConfigurationSource.Explicit
                    or ConfigurationSource.DataAnnotation;

            mutableProperty.SetMySqlValueGenerationStrategy(
                explicitlyGeneratedOnAdd
                    ? MySqlValueGenerationStrategy.ClientGuid
                    : MySqlValueGenerationStrategy.None);
            mutableProperty.ValueGenerated = explicitlyGeneratedOnAdd
                ? ValueGenerated.OnAdd
                : ValueGenerated.Never;
            return;
        }

        if (IsIntegerPrimaryKey(entityType, property))
        {
            mutableProperty.SetMySqlValueGenerationStrategy(MySqlValueGenerationStrategy.AutoIncrement);
            mutableProperty.ValueGenerated = ValueGenerated.OnAdd;
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

    private static bool IsIntegerPrimaryKey(
        IReadOnlyEntityType entityType,
        IConventionProperty property
    )
    {
        var primaryKey = entityType.FindPrimaryKey();

        if (primaryKey is null
            || primaryKey.Properties.Count != 1
            || primaryKey.Properties[0] != property)
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
