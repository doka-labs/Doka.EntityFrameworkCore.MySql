namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Selects the appropriate value generator for MySQL properties, including Hi/Lo sequence generators.
/// Hi/Lo state caching lives in <see cref="MySqlHiLoStateCache"/> so block windows survive
/// across DbContexts.
/// </summary>
internal sealed class MySqlValueGeneratorSelector : RelationalValueGeneratorSelector
{
    // EF 10 still consumes this obsolete hint before relational selection and exposes
    // no replacement accessor. Compare the exact built-in factory, not its output type,
    // so an application/plugin factory producing the same generator remains authoritative.
#pragma warning disable CS0612
    private static readonly Delegate? s_implicitGuidTextFactory =
        new GuidToStringConverter().MappingHints?.ValueGeneratorFactory;

    private static readonly Delegate? s_implicitGuidBytesFactory =
        new GuidToBytesConverter().MappingHints?.ValueGeneratorFactory;
#pragma warning restore CS0612

    private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder;
    private readonly IRelationalConnection _connection;
    private readonly IRelationalCommandDiagnosticsLogger _commandLogger;
    private readonly MySqlSingletonOptions _singletonOptions;

    public MySqlValueGeneratorSelector(
        ValueGeneratorSelectorDependencies dependencies,
        IRawSqlCommandBuilder rawSqlCommandBuilder,
        IRelationalConnection connection,
        IRelationalCommandDiagnosticsLogger commandLogger,
        IEnumerable<ISingletonOptions> singletonOptions
    ) : base(dependencies)
    {
        _rawSqlCommandBuilder = rawSqlCommandBuilder ?? throw new ArgumentNullException(nameof(rawSqlCommandBuilder));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _commandLogger = commandLogger ?? throw new ArgumentNullException(nameof(commandLogger));
        _singletonOptions = singletonOptions
                .OfType<MySqlSingletonOptions>()
                .Single()
            ?? throw new ArgumentNullException(nameof(singletonOptions));
    }

    /// <inheritdoc />
    public override bool TrySelect(
        IProperty property,
        ITypeBase typeBase,
        out ValueGenerator? valueGenerator
    )
    {
        ArgumentNullException.ThrowIfNull(property);

        var strategy = property.GetMySqlValueGenerationStrategy();
        if (property.GetValueGeneratorFactory() is not null)
        {
            return base.TrySelect(property, typeBase, out valueGenerator);
        }

        if (strategy == MySqlValueGenerationStrategy.ClientGuid
            && (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(Guid))
        {
            valueGenerator = new MySqlSequentialGuidValueGenerator();
            return true;
        }

        // EF's implicit GUID converters carry permanent-generator hints,
        // which runs before relational SQL-default selection. Only bypass that
        // inferred converter; application-supplied converters/factories remain authoritative.
        if (strategy is null
            && property.ValueGenerated == ValueGenerated.OnAdd
            && (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(Guid)
            && MySqlStoreGenerationMetadata.GetDefaultValueSql(property) is not null
            && property.GetValueConverter() is null
            && HasImplicitGuidConverterFactory(property.GetTypeMapping()))
        {
            valueGenerator = Cache.GetOrAdd(property, typeBase, static (_, _) => new TemporaryGuidValueGenerator());

            return true;
        }

        if (strategy != MySqlValueGenerationStrategy.HiLo)
        {
            return base.TrySelect(property, typeBase, out valueGenerator);
        }

        // Do not route HiLo through ValueGeneratorSelector's generator-instance
        // cache. A generator captures the scoped relational connection and must
        // therefore remain DbContext-scoped. Only HiLoValueGeneratorState is shared.
        var propertyType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (propertyType.IsEnum)
        {
            propertyType = Enum.GetUnderlyingType(propertyType);
        }

        valueGenerator = CreateHiLoGenerator(property, propertyType);
        if (valueGenerator is not null)
        {
            return true;
        }

        var converter = property.GetTypeMapping()
            .Converter;

        if (converter is not null
            && converter.ProviderClrType != propertyType)
        {
            valueGenerator = CreateHiLoGenerator(property, converter.ProviderClrType);
            if (valueGenerator is not null)
            {
                valueGenerator = valueGenerator.WithConverter(converter);
                return true;
            }
        }

        throw new InvalidOperationException(
            $"Hi/Lo value generation is not supported for properties of type '{property.ClrType.Name}'.");
    }

    /// <inheritdoc />
    protected override ValueGenerator? FindForType(
        IProperty property,
        ITypeBase typeBase,
        Type clrType
    )
    {
        // EF's fallback reads local annotations. Shared-table defaults instead
        // live on the root property, but still require temporary model GUIDs.
        if (clrType == typeof(Guid)
            && property.ValueGenerated != ValueGenerated.Never
            && MySqlStoreGenerationMetadata.GetDefaultValueSql(property) is not null)
        {
            return new TemporaryGuidValueGenerator();
        }

        return base.FindForType(property, typeBase, clrType);
    }

    /// <summary>Recognizes only EF 10's inherited Guid conversion generation hints.</summary>
    private static bool HasImplicitGuidConverterFactory(
        CoreTypeMapping mapping
    )
    {
        // Keep the compatibility read local: EF 10's factory-first ordering cannot
        // be intercepted through FindForType, and custom factories must not be discarded.
#pragma warning disable CS0612
        return (mapping.Converter?.GetType() == typeof(GuidToStringConverter)
                && Equals(mapping.ValueGeneratorFactory, s_implicitGuidTextFactory))
            || (mapping.Converter?.GetType() == typeof(GuidToBytesConverter)
                && Equals(mapping.ValueGeneratorFactory, s_implicitGuidBytesFactory));
#pragma warning restore CS0612
    }

    private ValueGenerator? CreateHiLoGenerator(
        IProperty property,
        Type clrType
    )
    {
        var sequenceName = property.FindAnnotation(MySqlAnnotationNames.HiLoSequenceName)
                ?.Value as string
            ?? $"EntityFrameworkHiLoSequence_{property.DeclaringType.ShortName()}";

        var sequence = property.DeclaringType.Model.FindSequence(sequenceName);
        var blockSize = sequence?.IncrementBy ?? 10;
        var supportsNative = _singletonOptions.Profile?.GetSupport(ProviderCapability.Sequences)
            == ProviderSupportStatus.Native;

        // RelationalConnection.ConnectionString is null for the MySqlDataSource path.
        // The provider-created DbConnection still exposes the canonical string while
        // retaining the data source as its owner.
        var connectionString = _connection.DbConnection.ConnectionString;
        var databaseIdentity = MySqlDatabaseIdentity.FromConnectionString(connectionString);
        var generatorState = MySqlHiLoStateCache.GetOrCreate(databaseIdentity, sequenceName, blockSize);

        var unwrappedType = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (unwrappedType == typeof(int))
        {
            return new MySqlSequenceHiLoValueGenerator<int>(
                generatorState,
                _rawSqlCommandBuilder,
                _connection,
                _commandLogger,
                sequenceName,
                supportsNative,
                blockSize);
        }

        if (unwrappedType == typeof(long))
        {
            return new MySqlSequenceHiLoValueGenerator<long>(
                generatorState,
                _rawSqlCommandBuilder,
                _connection,
                _commandLogger,
                sequenceName,
                supportsNative,
                blockSize);
        }

        if (unwrappedType == typeof(short))
        {
            return new MySqlSequenceHiLoValueGenerator<short>(
                generatorState,
                _rawSqlCommandBuilder,
                _connection,
                _commandLogger,
                sequenceName,
                supportsNative,
                blockSize);
        }

        if (unwrappedType == typeof(byte))
        {
            return new MySqlSequenceHiLoValueGenerator<byte>(
                generatorState,
                _rawSqlCommandBuilder,
                _connection,
                _commandLogger,
                sequenceName,
                supportsNative,
                blockSize);
        }

        return null;
    }
}
