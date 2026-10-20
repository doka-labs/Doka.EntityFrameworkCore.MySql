using System.Reflection;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlGeneratedValueReadbackTests
{
    /// <summary>
    /// Describes one result set without deriving the expected command order from its rows.
    /// </summary>
    private sealed class ResultRows
    {
        public ResultRows(
            object?[][] rows,
            Type? ordinalType = null,
            int? fieldCount = null
        )
        {
            Rows = rows;
            OrdinalType = ordinalType ?? (fieldCount == 1 || rows.FirstOrDefault()?.Length == 1 ? null : typeof(int));
            FieldCount = fieldCount ?? (OrdinalType is null ? 1 : 2);
        }

        public object?[][] Rows { get; }

        public Type? OrdinalType { get; }

        public int FieldCount { get; }
    }

    /// <summary>
    /// Drives the real sealed batch through its protected reader boundary, without a production test API.
    /// </summary>
    private sealed class Scenario : IDisposable
    {
        private static readonly BindingFlags s_instanceMembers = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly DbContext _context;
        private readonly MySqlModificationCommandBatch _batch;
        private readonly RelationalDataReader _relationalReader;
        private readonly MySqlCommand _command;

        private Scenario(
            DbContext context,
            MySqlModificationCommandBatch batch,
            ResultReader reader,
            TestCommand[] commands
        )
        {
            _context = context;
            _batch = batch;
            Reader = reader;
            Commands = commands;
            _command = new MySqlCommand();
            _relationalReader = new RelationalDataReader();
            _relationalReader.Initialize(
                context.GetService<IRelationalConnection>(),
                _command,
                reader,
                Guid.NewGuid(),
                logger: null);
        }

        public ResultReader Reader { get; }

        public TestCommand[] Commands { get; }

        public static Scenario Create(
            int[] groupSizes,
            ResultRows[] resultSets,
            int prefixCommands = 0,
            bool positional = true,
            ConcurrencySuppressor? interceptor = null,
            bool trackEntries = false,
            bool shareEntry = false
        )
        {
            var optionsBuilder = new DbContextOptionsBuilder()
                .UseMySql(
                    "Server=localhost;Database=readback_unit;User ID=test;Password=test;",
                    MySqlServerVersion.MySql(new Version(8, 4, 11)));

            if (interceptor is not null)
            {
                optionsBuilder.AddInterceptors(interceptor);
            }

            var context = new ReadbackContext(optionsBuilder.Options);
            IUpdateEntry[] entries = [];
            if (trackEntries)
            {
                var entryCount = shareEntry ? 1 : prefixCommands + groupSizes.Sum();
                context.AddRange(Enumerable.Range(1, entryCount).Select(id => new EntryOwner { Id = id }));
                entries = context.GetService<IUpdateAdapterFactory>().Create().Entries.ToArray();
            }

            var batch = (MySqlModificationCommandBatch)context.GetService<IModificationCommandBatchFactory>().Create();
            var commands = new List<TestCommand>();
            var mappings = (IList<ResultSetMapping>)typeof(ReaderModificationCommandBatch)
                .GetProperty("ResultSetMappings", s_instanceMembers)!
                .GetValue(batch)!;

            // WHY: Injecting the reader contract directly keeps these adversarial tests independent of SQL eligibility.
            var batchCommands = (ICollection<IReadOnlyModificationCommand>)batch.ModificationCommands;
            for (var index = 0; index < prefixCommands; index++)
            {
                AddCommand(ResultSetMapping.NoResults);
            }

            foreach (var size in groupSizes)
            {
                for (var index = 0; index < size; index++)
                {
                    var mapping = index == size - 1
                        ? ResultSetMapping.LastInResultSet
                        : ResultSetMapping.NotLastInResultSet;

                    if (positional)
                    {
                        mapping |= ResultSetMapping.IsPositionalResultMappingEnabled;
                    }

                    AddCommand(mapping);
                }
            }

            return new Scenario(context, batch, new ResultReader(resultSets), commands.ToArray());

            void AddCommand(
                ResultSetMapping mapping
            )
            {
                var command = new TestCommand(trackEntries ? [entries[shareEntry ? 0 : commands.Count]] : []);
                commands.Add(command);
                batchCommands.Add(command);
                mappings.Add(mapping);
            }
        }

        public Task ConsumeAsync(
            bool asynchronous,
            CancellationToken cancellationToken
        )
        {
            if (asynchronous)
            {
                var consumeAsync = typeof(MySqlModificationCommandBatch)
                    .GetMethod("ConsumeAsync", s_instanceMembers)!
                    .CreateDelegate<Func<RelationalDataReader, CancellationToken, Task>>(_batch);

                return consumeAsync(_relationalReader, cancellationToken);
            }

            var consume = typeof(MySqlModificationCommandBatch)
                .GetMethod("Consume", s_instanceMembers)!
                .CreateDelegate<Action<RelationalDataReader>>(_batch);

            consume(_relationalReader);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _relationalReader.Dispose();
            _command.Dispose();
            _context.Dispose();
        }
    }

    /// <summary>
    /// Supplies real EF update entries for exception attribution without a database connection.
    /// </summary>
    private sealed class ReadbackContext : DbContext
    {
        public ReadbackContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<EntryOwner>().Property(owner => owner.Id).ValueGeneratedNever();
    }

    private sealed class EntryOwner
    {
        public int Id { get; set; }
    }

    /// <summary>
    /// Ensures a consumer cannot rely on suppressible optimistic-concurrency exceptions to reject missing output.
    /// </summary>
    private sealed class ConcurrencySuppressor : SaveChangesInterceptor
    {
        public int Calls { get; private set; }

        public override InterceptionResult ThrowingConcurrencyException(
            ConcurrencyExceptionEventData eventData,
            InterceptionResult result
        )
        {
            Calls++;

            return InterceptionResult.Suppress();
        }

        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;

            return ValueTask.FromResult(InterceptionResult.Suppress());
        }
    }

    /// <summary>
    /// Captures which original modification command receives each generated value.
    /// </summary>
    private sealed class TestCommand : IReadOnlyModificationCommand
    {
        public TestCommand(
            IReadOnlyList<IUpdateEntry> entries,
            bool hasReadResults = true
        )
        {
            Entries = entries;
            ColumnModifications =
            [
                new ColumnModification(new ColumnModificationParameters(
                    "Generated", null, null, null, "int", IntTypeMapping.Default,
                    read: hasReadResults, write: !hasReadResults, key: false, condition: false,
                    sensitiveLoggingEnabled: false, isNullable: true)),
            ];
        }

        public object? GeneratedValue { get; private set; }

        public int PropagationCount { get; private set; }

        public int ReadbackTableAccesses { get; private set; }

        public ITable? Table
        {
            get
            {
                ReadbackTableAccesses++;

                return null;
            }
        }

        public IStoreStoredProcedure? StoreStoredProcedure => null;

        public string TableName => "Rows";

        public string? Schema => null;

        public IReadOnlyList<IColumnModification> ColumnModifications { get; }

        public IReadOnlyList<IUpdateEntry> Entries { get; }

        public EntityState EntityState => EntityState.Added;

        public IColumnBase? RowsAffectedColumn => null;

        public void PropagateResults(
            RelationalDataReader relationalReader
        )
        {
            GeneratedValue = relationalReader.DbDataReader.IsDBNull(0)
                ? null
                : relationalReader.DbDataReader.GetValue(0);

            PropagationCount++;
        }

        public void PropagateOutputParameters(
            DbParameterCollection parameterCollection,
            int baseParameterIndex
        ) => throw new NotSupportedException();
    }

    /// <summary>
    /// Exposes deterministic row faults and cancellation without a database or timing dependency.
    /// </summary>
    private sealed class ResultReader : DbDataReader
    {
        private readonly ResultRows[] _resultSets;
        private int _resultIndex;
        private int _rowIndex = -1;
        private bool _closed;

        public ResultReader(
            ResultRows[] resultSets
        )
        {
            _resultSets = resultSets;
        }

        private ResultRows Current => _resultSets[_resultIndex];

        public int ReadCount { get; private set; }

        public Action<int>? BeforeRead { get; set; }

        public List<CancellationToken> ObservedTokens { get; } = [];

        public override int FieldCount => Current.FieldCount;

        public override bool HasRows => Current.Rows.Length > 0;

        public override bool IsClosed => _closed;

        public override int Depth => 0;

        public override int RecordsAffected => -1;

        public override object this[int ordinal] => GetValue(ordinal);

        public override object this[string name] => GetValue(GetOrdinal(name));

        public override bool Read()
        {
            ReadCount++;
            BeforeRead?.Invoke(ReadCount);

            return ++_rowIndex < Current.Rows.Length;
        }

        public override Task<bool> ReadAsync(
            CancellationToken cancellationToken
        )
        {
            ObservedTokens.Add(cancellationToken);
            ReadCount++;
            BeforeRead?.Invoke(ReadCount);
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(++_rowIndex < Current.Rows.Length);
        }

        public override bool NextResult()
        {
            _rowIndex = -1;

            return ++_resultIndex < _resultSets.Length;
        }

        public override Task<bool> NextResultAsync(
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(NextResult());
        }

        public override object GetValue(
            int ordinal
        ) => Current.Rows[_rowIndex][ordinal] ?? DBNull.Value;

        public override int GetValues(
            object[] values
        )
        {
            var count = Math.Min(values.Length, FieldCount);
            for (var index = 0; index < count; index++)
            {
                values[index] = GetValue(index);
            }

            return count;
        }

        public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;

        public override Type GetFieldType(int ordinal) => ordinal == FieldCount - 1 && Current.OrdinalType is not null
            ? Current.OrdinalType
            : typeof(int);

        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);

        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);

        public override string GetName(int ordinal) => ordinal == 0 ? "Generated" : "Slot";

        public override int GetOrdinal(string name) => name == "Generated" ? 0 : 1;

        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);

        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);

        public override char GetChar(int ordinal) => (char)GetValue(ordinal);

        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);

        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);

        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);

        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);

        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);

        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);

        public override string GetString(int ordinal) => (string)GetValue(ordinal);

        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
            => throw new NotSupportedException();

        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
            => throw new NotSupportedException();

        public override IEnumerator GetEnumerator() => Current.Rows.GetEnumerator();

        public override void Close() => _closed = true;
    }
}
