namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// A Hi/Lo value generator backed by a MySQL/MariaDB sequence. Claims a block of
/// <c>blockSize</c> values per round-trip; <see cref="HiLoValueGeneratorState"/>
/// hands them out client-side until the block is exhausted, then asks for the next
/// LOW via <see cref="GetNewLowValue"/>. Both the MySQL emulation and native MariaDB
/// paths return the start of the newly leased block. Commands execute through EF's
/// configured relational connection so data sources, interceptors, execution
/// strategy integration, diagnostics, and connection ownership remain intact.
/// </summary>
/// <typeparam name="TValue">The generated value type (int, long, etc.).</typeparam>
internal sealed class MySqlSequenceHiLoValueGenerator<TValue> : HiLoValueGenerator<TValue>
{
    private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder;
    private readonly IRelationalConnection _connection;
    private readonly IRelationalCommandDiagnosticsLogger _commandLogger;
    private readonly string _sequenceName;
    private readonly bool _supportsNativeSequences;
    private readonly int _blockSize;

    public MySqlSequenceHiLoValueGenerator(
        HiLoValueGeneratorState generatorState,
        IRawSqlCommandBuilder rawSqlCommandBuilder,
        IRelationalConnection connection,
        IRelationalCommandDiagnosticsLogger commandLogger,
        string sequenceName,
        bool supportsNativeSequences,
        int blockSize
    ) : base(generatorState)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);

        _rawSqlCommandBuilder = rawSqlCommandBuilder ?? throw new ArgumentNullException(nameof(rawSqlCommandBuilder));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _commandLogger = commandLogger ?? throw new ArgumentNullException(nameof(commandLogger));
        _sequenceName = sequenceName ?? throw new ArgumentNullException(nameof(sequenceName));
        _supportsNativeSequences = supportsNativeSequences;
        _blockSize = blockSize;
    }

    /// <inheritdoc />
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc />
    protected override long GetNewLowValue()
    {
        var result = _rawSqlCommandBuilder
            .Build(
                MySqlSequenceValueGenerator.GenerateNextValueSql(_sequenceName, _blockSize, _supportsNativeSequences))
            .ExecuteScalar(
                new RelationalCommandParameterObject(
                    _connection,
                    parameterValues: null,
                    readerColumns: null,
                    context: null,
                    _commandLogger,
                    CommandSource.ValueGenerator));

        return MySqlSequenceValueGenerator.ConvertResult(result);
    }

    /// <inheritdoc />
    protected override async Task<long> GetNewLowValueAsync(
        CancellationToken cancellationToken = default
    )
    {
        var result = await _rawSqlCommandBuilder
            .Build(
                MySqlSequenceValueGenerator.GenerateNextValueSql(_sequenceName, _blockSize, _supportsNativeSequences))
            .ExecuteScalarAsync(
                new RelationalCommandParameterObject(
                    _connection,
                    parameterValues: null,
                    readerColumns: null,
                    context: null,
                    _commandLogger,
                    CommandSource.ValueGenerator),
                cancellationToken)
            .ConfigureAwait(false);

        return MySqlSequenceValueGenerator.ConvertResult(result);
    }
}
