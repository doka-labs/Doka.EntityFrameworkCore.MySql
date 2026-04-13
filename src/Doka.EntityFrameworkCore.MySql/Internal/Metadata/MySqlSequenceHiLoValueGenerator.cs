namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// A Hi/Lo value generator backed by a MySQL/MariaDB sequence.
/// Allocates blocks of values client-side, reducing database round-trips.
/// </summary>
/// <typeparam name="TValue">The generated value type (int, long, etc.).</typeparam>
internal sealed class MySqlSequenceHiLoValueGenerator<TValue> : HiLoValueGenerator<TValue>
{
    private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder;
    private readonly IRelationalConnection _connection;
    private readonly string _sequenceName;
    private readonly bool _supportsNativeSequences;

    public MySqlSequenceHiLoValueGenerator(
        HiLoValueGeneratorState generatorState,
        IRawSqlCommandBuilder rawSqlCommandBuilder,
        IRelationalConnection connection,
        string sequenceName,
        bool supportsNativeSequences
    ) : base(generatorState)
    {
        _rawSqlCommandBuilder = rawSqlCommandBuilder ?? throw new ArgumentNullException(nameof(rawSqlCommandBuilder));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sequenceName = sequenceName ?? throw new ArgumentNullException(nameof(sequenceName));
        _supportsNativeSequences = supportsNativeSequences;
    }

    /// <inheritdoc />
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc />
    protected override long GetNewLowValue()
    {
        var opened = _connection.Open();

        try
        {
            return MySqlSequenceValueGenerator.GetNextValue(
                _connection.DbConnection,
                _sequenceName,
                1,
                _supportsNativeSequences);
        }
        finally
        {
            if (opened)
            {
                _connection.Close();
            }
        }
    }

    /// <inheritdoc />
    protected override async Task<long> GetNewLowValueAsync(
        CancellationToken cancellationToken = default
    )
    {
        var opened = await _connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return await MySqlSequenceValueGenerator
                .GetNextValueAsync(
                    _connection.DbConnection,
                    _sequenceName,
                    1,
                    _supportsNativeSequences,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (opened)
            {
                await _connection
                    .CloseAsync()
                    .ConfigureAwait(false);
            }
        }
    }
}
