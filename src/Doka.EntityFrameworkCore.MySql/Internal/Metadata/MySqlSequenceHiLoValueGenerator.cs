namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// A Hi/Lo value generator backed by a MySQL/MariaDB sequence. Claims a block of
/// <c>blockSize</c> values per round-trip; <see cref="HiLoValueGeneratorState"/>
/// hands them out client-side until the block is exhausted, then asks for the next
/// LOW via <see cref="GetNewLowValue"/>. The emulation path advances the sequence
/// table by <c>blockSize</c> in one statement and the LOW is computed from the
/// returned HIGH; the native MariaDB path relies on the sequence DDL being created
/// with <c>INCREMENT BY blockSize</c> so each <c>NEXT VALUE FOR</c> already returns
/// the block start.
/// </summary>
/// <typeparam name="TValue">The generated value type (int, long, etc.).</typeparam>
internal sealed class MySqlSequenceHiLoValueGenerator<TValue> : HiLoValueGenerator<TValue>
{
    private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder;
    private readonly IRelationalConnection _connection;
    private readonly string _sequenceName;
    private readonly bool _supportsNativeSequences;
    private readonly int _blockSize;

    public MySqlSequenceHiLoValueGenerator(
        HiLoValueGeneratorState generatorState,
        IRawSqlCommandBuilder rawSqlCommandBuilder,
        IRelationalConnection connection,
        string sequenceName,
        bool supportsNativeSequences,
        int blockSize
    ) : base(generatorState)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);

        _rawSqlCommandBuilder = rawSqlCommandBuilder ?? throw new ArgumentNullException(nameof(rawSqlCommandBuilder));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sequenceName = sequenceName ?? throw new ArgumentNullException(nameof(sequenceName));
        _supportsNativeSequences = supportsNativeSequences;
        _blockSize = blockSize;
    }

    /// <inheritdoc />
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc />
    protected override long GetNewLowValue()
    {
        using var connection = new MySqlConnection(_connection.ConnectionString);
        connection.Open();

        var serverValue = MySqlSequenceValueGenerator.GetNextValue(
            connection,
            _sequenceName,
            _blockSize,
            _supportsNativeSequences);

        return ToBlockLow(serverValue);
    }

    /// <inheritdoc />
    protected override async Task<long> GetNewLowValueAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = new MySqlConnection(_connection.ConnectionString);
        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        var serverValue = await MySqlSequenceValueGenerator
            .GetNextValueAsync(
                connection,
                _sequenceName,
                _blockSize,
                _supportsNativeSequences,
                cancellationToken)
            .ConfigureAwait(false);

        return ToBlockLow(serverValue);
    }

    /// <summary>
    /// Converts the server-returned sequence value into the LOW of the freshly
    /// claimed block. The emulation path returns the post-increment HIGH (the
    /// table column was advanced by <c>blockSize</c>); the native path returns the
    /// block start directly because the underlying sequence is created with
    /// <c>INCREMENT BY blockSize</c>.
    /// </summary>
    private long ToBlockLow(
        long serverValue
    ) => _supportsNativeSequences ? serverValue : serverValue - _blockSize + 1;
}
