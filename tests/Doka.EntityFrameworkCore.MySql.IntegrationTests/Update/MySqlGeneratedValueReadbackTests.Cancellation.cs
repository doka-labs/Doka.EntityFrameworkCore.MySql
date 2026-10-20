using System.Collections;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGeneratedValueReadbackTests
{
    /// <summary>Cancels an in-flight readback after one row was propagated and rolls back the whole group.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task InFlightReadbackCancellationDiscardsPartialGeneratedValues()
    {
        using var cancellation = new CancellationTokenSource();
        CancellableReadbackReader? observedReader = null;
        var secondReadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = new ReadbackCapture
        {
            AfterExecution = (command, reader, _) =>
            {
                if (command.CommandText.Contains("UNION ALL", StringComparison.Ordinal))
                {
                    observedReader = new CancellableReadbackReader(reader, secondReadStarted);

                    return ValueTask.FromResult<DbDataReader>(observedReader);
                }

                return ValueTask.FromResult(reader);
            },
        };

        await using var context = CreateContext<int, StandardMode>(IntegrationDatabaseTarget.MySql84, capture);
        await RecreateTableAsync(context);
        var rows = CreateIntegerRows(8);
        AddRows(context, rows);
        capture.Enabled = true;

        var save = context.SaveChangesAsync(cancellation.Token);
        bool firstRowConsumed;
        bool saveWasPending;

        try
        {
            await secondReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));

            // WHY: The provider propagates the first result before requesting
            // the second. EF may defer writing the generated value back to the
            // entity until SaveChanges finishes, so observe the reader boundary.
            firstRowConsumed = observedReader?.CompletedReads >= 1;
            saveWasPending = !save.IsCompleted;
        }
        finally
        {
            cancellation.Cancel();
        }

        var exception = await Record.ExceptionAsync(() => save);

        capture.Enabled = false;
        Assert.True(firstRowConsumed);
        Assert.True(saveWasPending);
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.NotNull(observedReader);
        Assert.True(observedReader.PendingReadObserved);
        Assert.True(observedReader.CompletedReads >= 1);
        Assert.Equal(0, await context.Rows.AsNoTracking().CountAsync(CancellationToken.None));
        Assert.All(rows, row => Assert.Equal(0, row.Stamp));
        Assert.All(rows, row => Assert.Equal(EntityState.Added, context.Entry(row).State));
    }

    /// <summary>Delegates real rows to the connector and holds the second read until cancellation.</summary>
    private sealed class CancellableReadbackReader(
        DbDataReader inner,
        TaskCompletionSource<bool> secondReadStarted) : DbDataReader
    {
        private int _readAttempts;
        private bool _disposed;
        internal int CompletedReads { get; private set; }
        internal bool PendingReadObserved { get; private set; }
        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override int VisibleFieldCount => inner.VisibleFieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override void Close()
        {
            if (!_disposed)
            {
                inner.Close();
            }
        }
        public override DataTable? GetSchemaTable() => inner.GetSchemaTable();
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long offset, byte[]? buffer, int bufferOffset, int length) =>
            inner.GetBytes(ordinal, offset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long offset, char[]? buffer, int bufferOffset, int length) =>
            inner.GetChars(ordinal, offset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override bool NextResult() => inner.NextResult();
        public override bool Read() => inner.Read();
        public override IEnumerator GetEnumerator() => ((IEnumerable)inner).GetEnumerator();
        public override T GetFieldValue<T>(int ordinal) => inner.GetFieldValue<T>(ordinal);
        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) =>
            inner.GetFieldValueAsync<T>(ordinal, cancellationToken);
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) =>
            inner.IsDBNullAsync(ordinal, cancellationToken);
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) =>
            inner.NextResultAsync(cancellationToken);

        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            if (++_readAttempts == 2)
            {
                PendingReadObserved = true;
                secondReadStarted.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var read = await inner.ReadAsync(cancellationToken);

            if (read)
            {
                CompletedReads++;
            }

            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                if (!_disposed)
                {
                    _disposed = true;
                    await inner.DisposeAsync();
                }
            }
            finally
            {
                // WHY: Base disposal still runs when the connector cleanup throws.
                await base.DisposeAsync();
            }
        }
    }
}
