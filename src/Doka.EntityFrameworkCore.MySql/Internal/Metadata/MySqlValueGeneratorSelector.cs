namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Selects the appropriate value generator for MySQL properties, including Hi/Lo sequence generators.
/// Hi/Lo state caching lives in <see cref="MySqlHiLoStateCache"/> so block windows survive
/// across DbContexts.
/// </summary>
internal sealed class MySqlValueGeneratorSelector : RelationalValueGeneratorSelector
{
    private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder;
    private readonly IRelationalConnection _connection;
    private readonly MySqlSingletonOptions _singletonOptions;

    public MySqlValueGeneratorSelector(
        ValueGeneratorSelectorDependencies dependencies,
        IRawSqlCommandBuilder rawSqlCommandBuilder,
        IRelationalConnection connection,
        IEnumerable<ISingletonOptions> singletonOptions
    ) : base(dependencies)
    {
        _rawSqlCommandBuilder = rawSqlCommandBuilder ?? throw new ArgumentNullException(nameof(rawSqlCommandBuilder));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _singletonOptions = singletonOptions
                .OfType<MySqlSingletonOptions>()
                .Single()
            ?? throw new ArgumentNullException(nameof(singletonOptions));
    }

    /// <inheritdoc />
    protected override ValueGenerator? FindForType(
        IProperty property,
        ITypeBase typeBase,
        Type clrType
    )
    {
        ArgumentNullException.ThrowIfNull(property);

        if (property.GetMySqlValueGenerationStrategy() == MySqlValueGenerationStrategy.HiLo)
        {
            return CreateHiLoGenerator(property, clrType);
        }

        return base.FindForType(property, typeBase, clrType);
    }

    private ValueGenerator CreateHiLoGenerator(
        IProperty property,
        Type clrType
    )
    {
        var sequenceName = property.FindAnnotation(MySqlAnnotationNames.HiLoSequenceName)
                ?.Value as string
            ?? $"EntityFrameworkHiLoSequence_{property.DeclaringType.ShortName()}";

        var sequence = property.DeclaringType.Model.FindSequence(sequenceName);
        var blockSize = sequence?.IncrementBy ?? 10;
        var supportsNative = _singletonOptions.Profile?.Has(Capability.SupportsNativeSequences) ?? false;

        var generatorState = MySqlHiLoStateCache.GetOrCreate(sequenceName, blockSize);

        var unwrappedType = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (unwrappedType == typeof(int))
        {
            return new MySqlSequenceHiLoValueGenerator<int>(
                generatorState,
                _rawSqlCommandBuilder,
                _connection,
                sequenceName,
                supportsNative);
        }

        if (unwrappedType == typeof(long))
        {
            return new MySqlSequenceHiLoValueGenerator<long>(
                generatorState,
                _rawSqlCommandBuilder,
                _connection,
                sequenceName,
                supportsNative);
        }

        if (unwrappedType == typeof(short))
        {
            return new MySqlSequenceHiLoValueGenerator<short>(
                generatorState,
                _rawSqlCommandBuilder,
                _connection,
                sequenceName,
                supportsNative);
        }

        if (unwrappedType == typeof(byte))
        {
            return new MySqlSequenceHiLoValueGenerator<byte>(
                generatorState,
                _rawSqlCommandBuilder,
                _connection,
                sequenceName,
                supportsNative);
        }

        throw new InvalidOperationException(
            $"Hi/Lo value generation is not supported for properties of type '{clrType.Name}'.");
    }
}
