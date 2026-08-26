namespace Doka.Caching.MySql;

internal sealed class MySqlCacheDatabaseOperations
{
    internal const int CleanupBatchSize = 1000;
    internal const int MaximumKeyByteLength = 1024;

    private const int StreamBufferSize = 8192;
    private static readonly UTF8Encoding s_keyEncoding = new(false, true);

    private readonly MySqlDataSource _dataSource;
    private readonly MySqlCacheSql _sql;

    public MySqlCacheDatabaseOperations(
        MySqlDataSource dataSource,
        string qualifiedTableName
    )
    {
        _dataSource = dataSource;
        _sql = new MySqlCacheSql(qualifiedTableName);
    }

    public byte[]? Get(
        string key
    )
    {
        ValidateKey(key);

        using var connection = _dataSource.OpenConnection();
        using var command = CreateKeyCommand(connection, _sql.Get, key);
        using var reader = command.ExecuteReader(CommandBehavior.SequentialAccess | CommandBehavior.SingleRow);

        if (!reader.Read())
        {
            return null;
        }

        var revision = reader.GetInt64(0);
        var hasSlidingExpiration = !reader.IsDBNull(1);
        var value = reader.GetFieldValue<byte[]>(2);
        reader.Close();

        if (hasSlidingExpiration)
        {
            RefreshCore(connection, key, revision);
        }

        return value;
    }

    public async Task<byte[]?> GetAsync(
        string key,
        CancellationToken cancellationToken
    )
    {
        ValidateKey(key);

        await using var connection = await _dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = CreateKeyCommand(connection, _sql.Get, key);
        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);

        if (!await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        var revision = reader.GetInt64(0);
        var hasSlidingExpiration = !reader.IsDBNull(1);
        var value = await reader
            .GetFieldValueAsync<byte[]>(2, cancellationToken)
            .ConfigureAwait(false);

        await reader
            .CloseAsync()
            .ConfigureAwait(false);

        if (hasSlidingExpiration)
        {
            await RefreshCoreAsync(connection, key, revision, cancellationToken)
                .ConfigureAwait(false);
        }

        return value;
    }

    public bool TryGet(
        string key,
        IBufferWriter<byte> destination
    )
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(destination);

        using var connection = _dataSource.OpenConnection();
        using var command = CreateKeyCommand(connection, _sql.Get, key);
        using var reader = command.ExecuteReader(CommandBehavior.SequentialAccess | CommandBehavior.SingleRow);

        if (!reader.Read())
        {
            return false;
        }

        var revision = reader.GetInt64(0);
        var hasSlidingExpiration = !reader.IsDBNull(1);
        using (var stream = reader.GetStream(2))
        {
            CopyTo(stream, reader.GetBytes(2, 0, null, 0, 0), destination);
        }

        reader.Close();

        if (hasSlidingExpiration)
        {
            RefreshCore(connection, key, revision);
        }

        return true;
    }

    public async Task<bool> TryGetAsync(
        string key,
        IBufferWriter<byte> destination,
        CancellationToken cancellationToken
    )
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(destination);

        await using var connection = await _dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = CreateKeyCommand(connection, _sql.Get, key);
        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);

        if (!await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        var revision = reader.GetInt64(0);
        var hasSlidingExpiration = !reader.IsDBNull(1);
        await using (var stream = reader.GetStream(2))
        {
            await CopyToAsync(stream, reader.GetBytes(2, 0, null, 0, 0), destination, cancellationToken)
                .ConfigureAwait(false);
        }

        await reader
            .CloseAsync()
            .ConfigureAwait(false);

        if (hasSlidingExpiration)
        {
            await RefreshCoreAsync(connection, key, revision, cancellationToken)
                .ConfigureAwait(false);
        }

        return true;
    }

    public void Set(
        string key,
        ReadOnlyMemory<byte> value,
        MySqlCacheExpiration expiration
    )
    {
        ValidateKey(key);

        using var connection = _dataSource.OpenConnection();
        using var command = CreateSetCommand(connection, key, value, expiration);
        command.ExecuteNonQuery();
    }

    public async Task SetAsync(
        string key,
        ReadOnlyMemory<byte> value,
        MySqlCacheExpiration expiration,
        CancellationToken cancellationToken
    )
    {
        ValidateKey(key);

        await using var connection = await _dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = CreateSetCommand(connection, key, value, expiration);
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Refresh(
        string key
    )
    {
        ValidateKey(key);

        using var connection = _dataSource.OpenConnection();
        RefreshCore(connection, key, revision: null);
    }

    public async Task RefreshAsync(
        string key,
        CancellationToken cancellationToken
    )
    {
        ValidateKey(key);

        await using var connection = await _dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await RefreshCoreAsync(connection, key, revision: null, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Remove(
        string key
    )
    {
        ValidateKey(key);

        using var connection = _dataSource.OpenConnection();
        using var command = CreateKeyCommand(connection, _sql.Remove, key);
        command.ExecuteNonQuery();
    }

    public async Task RemoveAsync(
        string key,
        CancellationToken cancellationToken
    )
    {
        ValidateKey(key);

        await using var connection = await _dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = CreateKeyCommand(connection, _sql.Remove, key);
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public bool DeleteExpiredItems()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new MySqlCommand(_sql.SelectExpired, connection);
        command.Parameters.Add("@batchSize", MySqlDbType.Int32).Value = CleanupBatchSize;
        List<byte[]> candidates = [];

        using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess))
        {
            while (reader.Read())
            {
                candidates.Add(reader.GetFieldValue<byte[]>(0));
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        ConfigureDeleteExpiredCommand(command, candidates);
        command.ExecuteNonQuery();

        return candidates.Count == CleanupBatchSize;
    }

    public async Task<bool> DeleteExpiredItemsAsync(
        CancellationToken cancellationToken
    )
    {
        await using var connection = await _dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = new MySqlCommand(_sql.SelectExpired, connection);
        command.Parameters.Add("@batchSize", MySqlDbType.Int32).Value = CleanupBatchSize;
        List<byte[]> candidates = [];

        await using (var reader = await command
                         .ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken)
                         .ConfigureAwait(false))
        {
            while (await reader
                       .ReadAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                candidates.Add(
                    await reader
                        .GetFieldValueAsync<byte[]>(0, cancellationToken)
                        .ConfigureAwait(false));
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        ConfigureDeleteExpiredCommand(command, candidates);
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);

        return candidates.Count == CleanupBatchSize;
    }

    private void ConfigureDeleteExpiredCommand(
        MySqlCommand command,
        List<byte[]> candidates
    )
    {
        command.Parameters.Clear();
        var sql = new StringBuilder(_sql.DeleteExpiredPrefix);

        // OR ranges avoid MariaDB's IN-to-subquery conversion at the full 1,000-key batch size.
        for (var index = 0; index < candidates.Count; index++)
        {
            if (index > 0)
            {
                sql.Append(" OR ");
            }

            var parameterName = "@key" + index.ToString(CultureInfo.InvariantCulture);
            sql
                .Append("`Id` = ")
                .Append(parameterName);

            command.Parameters.Add(parameterName, MySqlDbType.VarBinary).Value = candidates[index];
        }

        command.CommandText = sql
            .Append(");")
            .ToString();
    }

    internal static void ValidateKey(
        string key
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        int byteCount;
        try
        {
            byteCount = s_keyEncoding.GetByteCount(key);
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("Cache keys must contain valid UTF-16 text.", nameof(key));
        }

        if (byteCount > MaximumKeyByteLength)
        {
            throw new ArgumentException($"Cache keys cannot exceed {MaximumKeyByteLength} UTF-8 bytes.", nameof(key));
        }
    }

    private static void CopyTo(
        Stream source,
        long remaining,
        IBufferWriter<byte> destination
    )
    {
        while (remaining > 0)
        {
            var length = (int)Math.Min(remaining, StreamBufferSize);
            var buffer = destination
                .GetSpan(length)[..length];
            var bytesRead = source.Read(buffer);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("The cache value ended before its declared length.");
            }

            destination.Advance(bytesRead);
            remaining -= bytesRead;
        }
    }

    private static async Task CopyToAsync(
        Stream source,
        long remaining,
        IBufferWriter<byte> destination,
        CancellationToken cancellationToken
    )
    {
        while (remaining > 0)
        {
            var length = (int)Math.Min(remaining, StreamBufferSize);
            var buffer = destination.GetMemory(length)[..length];
            var bytesRead = await source
                .ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("The cache value ended before its declared length.");
            }

            destination.Advance(bytesRead);
            remaining -= bytesRead;
        }
    }

    private MySqlCommand CreateSetCommand(
        MySqlConnection connection,
        string key,
        ReadOnlyMemory<byte> value,
        MySqlCacheExpiration expiration
    )
    {
        var command = new MySqlCommand(_sql.Set, connection);
        AddKeyParameter(command, key);
        command.Parameters.Add("@value", MySqlDbType.LongBlob)
            .Value = value;
        command.Parameters.Add("@absoluteExpirationUtc", MySqlDbType.DateTime)
            .Value = (object?)expiration.AbsoluteExpirationUtc ?? DBNull.Value;
        command.Parameters.Add("@absoluteExpirationRelativeMicroseconds", MySqlDbType.Int64)
            .Value = (object?)expiration.AbsoluteExpirationRelativeMicroseconds ?? DBNull.Value;
        command.Parameters.Add("@slidingExpirationMicroseconds", MySqlDbType.Int64)
            .Value = (object?)expiration.SlidingExpirationMicroseconds ?? DBNull.Value;
        command.Parameters.Add("@revision", MySqlDbType.Int64)
            .Value = CreateRevision();

        return command;
    }

    private static MySqlCommand CreateKeyCommand(
        MySqlConnection connection,
        string commandText,
        string key
    )
    {
        var command = new MySqlCommand(commandText, connection);
        AddKeyParameter(command, key);
        return command;
    }

    private static void AddKeyParameter(
        MySqlCommand command,
        string key
    ) => command.Parameters.Add("@key", MySqlDbType.VarChar)
        .Value = key;

    private void RefreshCore(
        MySqlConnection connection,
        string key,
        long? revision
    )
    {
        // Start UPDATE after acquiring the row lock so its UTC timestamp excludes the lock wait.
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        using var command = CreateRefreshCommand(connection, transaction, key, revision);
        if (command.ExecuteScalar() is not null)
        {
            command.CommandText = revision.HasValue ? _sql.RefreshAfterRead : _sql.Refresh;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private async Task RefreshCoreAsync(
        MySqlConnection connection,
        string key,
        long? revision,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        await using var command = CreateRefreshCommand(connection, transaction, key, revision);
        if (await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false) is not null)
        {
            command.CommandText = revision.HasValue ? _sql.RefreshAfterRead : _sql.Refresh;
            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction
            .CommitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private MySqlCommand CreateRefreshCommand(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string key,
        long? revision
    )
    {
        var command = CreateKeyCommand(connection, _sql.LockForRefresh, key);
        command.Transaction = transaction;
        if (revision.HasValue)
        {
            command.Parameters.Add("@revision", MySqlDbType.Int64)
                .Value = revision.Value;
        }

        return command;
    }

    private static long CreateRevision()
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        RandomNumberGenerator.Fill(bytes);
        return BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }
}
