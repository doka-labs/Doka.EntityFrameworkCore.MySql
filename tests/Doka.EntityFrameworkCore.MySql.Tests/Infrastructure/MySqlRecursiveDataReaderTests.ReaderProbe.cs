namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlRecursiveDataReaderTests
{
    private static ReaderProbe Rows(
        Type payloadType,
        params object?[] values
    )
    {
        var table = GuardTable(payloadType);

        foreach (var value in values)
        {
            table.Rows.Add(value ?? DBNull.Value, 1L, 0L);
        }

        return new ReaderProbe(table);
    }

    private static ReaderProbe GuardRow(
        Type payloadType,
        object payload,
        object presence,
        object invalidLimit
    )
    {
        var table = GuardTable(payloadType);
        table.Rows.Add(payload, presence, invalidLimit);

        return new ReaderProbe(table);
    }

    private static DataTable GuardTable(
        Type payloadType
    )
    {
        var table = new DataTable();
        table.Columns.Add("payload", payloadType);
        table.Columns.Add(Presence, typeof(long));
        table.Columns.Add(InvalidLimit, typeof(object));

        return table;
    }

    private sealed class ReaderProbe : DbDataReader
    {
        private readonly int _payloadCount;
        private readonly DataTableReader _reader;
        private readonly DataTable _table;

        internal ReaderProbe(
            DataTable table
        )
        {
            _table = table;
            _reader = table.CreateDataReader();
            _payloadCount = table.Columns.Count - 2;
        }

        internal int ReadCalls { get; private set; }
        internal int PayloadReads { get; private set; }
        internal int DisposeCalls { get; private set; }
        internal Exception? ReadFailure { get; set; }
        internal Exception? OrdinalFailure { get; set; }
        internal int FailReadAt { get; set; } = 1;
        internal Exception? DisposeFailure { get; set; }
        internal bool FailDisposeOnce { get; set; }

        public override int FieldCount => _reader.FieldCount;
        public override bool HasRows => _reader.HasRows;
        public override bool IsClosed => _reader.IsClosed;
        public override int Depth => _reader.Depth;
        public override int RecordsAffected => -1;

        public override object this[
            int ordinal
        ] => GetValue(ordinal);

        public override object this[
            string name
        ] => GetValue(GetOrdinal(name));

        public override bool Read()
        {
            ReadCalls++;

            if (ReadCalls == FailReadAt
                && ReadFailure is { } failure)
            {
                throw failure;
            }

            return _reader.Read();
        }

        public override Task<bool> ReadAsync(
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: The fake has no asynchronous I/O; both paths record the same in-memory read.
            // ReSharper disable once MethodHasAsyncOverload
            return Task.FromResult(Read());
        }

        public override bool NextResult() => _reader.NextResult();

        public override Task<bool> NextResultAsync(
            CancellationToken cancellationToken
        ) => _reader.NextResultAsync(cancellationToken);

        public override string GetName(
            int ordinal
        ) => _reader.GetName(ordinal);

        public override int GetOrdinal(
            string name
        )
        {
            if (OrdinalFailure is { } failure)
            {
                throw failure;
            }

            return _reader.GetOrdinal(name);
        }

        [return:
            DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties
                | DynamicallyAccessedMemberTypes.PublicFields)]
        public override Type GetFieldType(
            int ordinal
        ) => _reader.GetFieldType(ordinal);

        public override string GetDataTypeName(
            int ordinal
        ) => _reader.GetDataTypeName(ordinal);

        public override object GetValue(
            int ordinal
        ) => _reader.GetValue(Track(ordinal));

        public override int GetValues(
            object[] values
        ) => _reader.GetValues(values);

        public override bool IsDBNull(
            int ordinal
        ) => _reader.IsDBNull(Track(ordinal));

        public override bool GetBoolean(
            int ordinal
        ) => _reader.GetBoolean(Track(ordinal));

        public override byte GetByte(
            int ordinal
        ) => _reader.GetByte(Track(ordinal));

        public override char GetChar(
            int ordinal
        ) => _reader.GetChar(Track(ordinal));

        public override DateTime GetDateTime(
            int ordinal
        ) => _reader.GetDateTime(Track(ordinal));

        public override decimal GetDecimal(
            int ordinal
        ) => _reader.GetDecimal(Track(ordinal));

        public override double GetDouble(
            int ordinal
        ) => _reader.GetDouble(Track(ordinal));

        public override float GetFloat(
            int ordinal
        ) => _reader.GetFloat(Track(ordinal));

        public override Guid GetGuid(
            int ordinal
        ) => _reader.GetGuid(Track(ordinal));

        public override short GetInt16(
            int ordinal
        ) => _reader.GetInt16(Track(ordinal));

        public override int GetInt32(
            int ordinal
        ) => _reader.GetInt32(Track(ordinal));

        public override long GetInt64(
            int ordinal
        ) => _reader.GetInt64(Track(ordinal));

        public override string GetString(
            int ordinal
        ) => _reader.GetString(Track(ordinal));

        public override T GetFieldValue<T>(
            int ordinal
        ) => _reader.GetFieldValue<T>(Track(ordinal));

        public override Task<T> GetFieldValueAsync<T>(
            int ordinal,
            CancellationToken cancellationToken
        ) => _reader.GetFieldValueAsync<T>(Track(ordinal), cancellationToken);

        public override long GetBytes(
            int ordinal,
            long dataOffset,
            byte[]? buffer,
            int bufferOffset,
            int length
        ) => _reader.GetBytes(Track(ordinal), dataOffset, buffer, bufferOffset, length);

        public override long GetChars(
            int ordinal,
            long dataOffset,
            char[]? buffer,
            int bufferOffset,
            int length
        ) => _reader.GetChars(Track(ordinal), dataOffset, buffer, bufferOffset, length);

        public override DataTable GetSchemaTable() => _reader.GetSchemaTable();
        public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);
        public override void Close() => _reader.Close();

        protected override void Dispose(
            bool disposing
        )
        {
            if (disposing)
            {
                DisposeCalls++;
                _reader.Dispose();
                _table.Dispose();

                if (DisposeFailure is { } failure)
                {
                    if (FailDisposeOnce)
                    {
                        DisposeFailure = null;
                    }

                    throw failure;
                }
            }

            base.Dispose(disposing);
        }

        private int Track(
            int ordinal
        )
        {
            if ((uint)ordinal < (uint)_payloadCount)
            {
                PayloadReads++;
            }

            return ordinal;
        }
    }
}
