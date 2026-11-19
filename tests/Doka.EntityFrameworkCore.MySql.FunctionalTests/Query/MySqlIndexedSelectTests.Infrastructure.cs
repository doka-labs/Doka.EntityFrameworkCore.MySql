namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlIndexedSelectTests
{
    /// <summary>Builds offline options through the existing transient-provider policy.</summary>
    private static IndexedContext CreateContext(
        bool mariaDb,
        IndexValues? values = null,
        MySqlServerVersion? version = null
    )
    {
        var builder = MySqlFunctionalTestOptions
            .CreateTransientBuilder<IndexedContext>()
            .UseMySql("Server=127.0.0.1;Port=1;Database=indexed_offline;User ID=unused;Pooling=false;",
                version
                ?? (mariaDb
                    ? MySqlServerVersion.MariaDb(new Version(12, 3, 3))
                    : MySqlServerVersion.MySql(new Version(8, 4, 0))));

        if (values is not null)
        {
            builder.AddInterceptors(values, new SuppressedOpen());
        }

        return new IndexedContext(builder.Options);
    }

    private static int Occurrences(
        string text,
        string fragment
    )
    {
        var count = 0;
        var position = 0;

        while ((position = text.IndexOf(fragment, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += fragment.Length;
        }

        return count;
    }

    /// <summary>Configures one physical row shape shared by the filtered and unfiltered model controls.</summary>
    private static void ConfigureIndexedRows(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<IndexedRow>(row =>
        {
            row.ToTable("IndexedRows");
            row.HasKey(value => value.Id);

            // WHY: The converted grouping-key control must retain EF's string provider representation.
            row
                .Property(value => value.Bucket)
                .HasConversion<string>();

            row
                .Property(value => value.Name)
                .HasMaxLength(80);
        });
    }

    private sealed class IndexedContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public IndexedContext(
            DbContextOptions<IndexedContext> options
        ) : base(options) { }

        internal DbSet<IndexedRow> Rows => Set<IndexedRow>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexedRows(modelBuilder);
    }

    private sealed class IndexedRow
    {
        public int Id { get; set; }
        public int? AccountId { get; set; }
        public int Stamp { get; set; }
        public int Amount { get; set; }
        public IndexedBucket Bucket { get; set; }
        public string? Name { get; set; }
    }

    private enum IndexedBucket
    {
        First,
        Second,
    }

    /// <summary>Replaces only the driver's result so the real compiled EF reader-converter path can be tested offline.</summary>
    private sealed class IndexValues : DbCommandInterceptor
    {
        private readonly long _value;

        /// <summary>Initializes the test helper with its supplied dependencies and values.</summary>
        public IndexValues(
            long value
        )
        {
            _value = value;
        }

        internal int Commands { get; private set; }

        internal int Int32Reads { get; private set; }

        internal int ReaderCloses { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            Commands++;

            // WHY: RelationalDataReader owns the replacement reader. Record callbacks only;
            // retaining its disposable instance here would invent a second fixture owner.
            return InterceptionResult<DbDataReader>.SuppressWithResult(
                new NativeIntegerReader(_value, () => Int32Reads++, () => ReaderCloses++));
        }
    }

    /// <summary>Models the qualified connector's checked numeric access on a native signed-bigint result.</summary>
    private sealed class NativeIntegerReader : DbDataReader
    {
        private readonly Action _closedCallback;
        private readonly Action _readInt32;
        private readonly long _value;

        private bool _closed;
        private bool _read;

        /// <summary>Initializes the reader with the configured values and observation callbacks.</summary>
        public NativeIntegerReader(
            long value,
            Action readInt32,
            Action closed
        )
        {
            _value = value;
            _readInt32 = readInt32;
            _closedCallback = closed;
        }

        public override int Depth => 0;
        public override int FieldCount => 1;
        public override bool HasRows => true;
        public override bool IsClosed => _closed;
        public override int RecordsAffected => -1;

        public override object this[
            int ordinal
        ] => GetValue(ordinal);

        public override object this[
            string name
        ] => GetValue(GetOrdinal(name));

        public override int GetInt32(
            int ordinal
        )
        {
            if (ordinal != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ordinal),
                    ordinal,
                    "The native integer fixture has exactly one result column.");
            }

            _readInt32();

            // WHY: DataTableReader rejects Int64 GetInt32 access, unlike both qualified
            // MySqlConnector lines. Native SELECT controls prove the driver's exact/overflow contract.
            return checked((int)_value);
        }

        public override long GetInt64(
            int ordinal
        ) => _value;

        public override string GetDataTypeName(
            int ordinal
        ) => "BIGINT";

        public override Type GetFieldType(
            int ordinal
        ) => typeof(long);

        public override string GetName(
            int ordinal
        ) => "Index";

        public override int GetOrdinal(
            string name
        ) => name == "Index"
            ? 0
            : throw new ArgumentOutOfRangeException(nameof(name),
                name,
                "The native integer fixture exposes only Index.");

        public override object GetValue(
            int ordinal
        ) => _value;

        public override bool IsDBNull(
            int ordinal
        ) => false;

        public override bool NextResult() => false;
        public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

        public override void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _closedCallback();
        }

        public override bool Read()
        {
            var available = !_read;
            _read = true;

            return available;
        }

        public override int GetValues(
            object[] values
        )
        {
            if (values.Length == 0)
            {
                return 0;
            }

            values[0] = _value;

            return 1;
        }

        public override bool GetBoolean(
            int ordinal
        ) => throw new NotSupportedException();

        public override byte GetByte(
            int ordinal
        ) => throw new NotSupportedException();

        public override char GetChar(
            int ordinal
        ) => throw new NotSupportedException();

        public override DateTime GetDateTime(
            int ordinal
        ) => throw new NotSupportedException();

        public override decimal GetDecimal(
            int ordinal
        ) => throw new NotSupportedException();

        public override double GetDouble(
            int ordinal
        ) => throw new NotSupportedException();

        public override float GetFloat(
            int ordinal
        ) => throw new NotSupportedException();

        public override Guid GetGuid(
            int ordinal
        ) => throw new NotSupportedException();

        public override short GetInt16(
            int ordinal
        ) => throw new NotSupportedException();

        public override string GetString(
            int ordinal
        ) => throw new NotSupportedException();

        public override long GetBytes(
            int ordinal,
            long dataOffset,
            byte[]? buffer,
            int bufferOffset,
            int length
        ) => throw new NotSupportedException();

        public override long GetChars(
            int ordinal,
            long dataOffset,
            char[]? buffer,
            int bufferOffset,
            int length
        ) => throw new NotSupportedException();
    }

    /// <summary>A suppressed open keeps these materializer controls independent of a live database.</summary>
    private sealed class SuppressedOpen : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result
        ) => InterceptionResult.Suppress();
    }
}
