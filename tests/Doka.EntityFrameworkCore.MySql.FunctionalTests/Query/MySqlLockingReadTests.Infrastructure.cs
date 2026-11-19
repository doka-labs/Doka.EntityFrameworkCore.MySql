namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlLockingReadTests
{
    private static readonly string[] s_modes = ["Update", "Share", "NoWait", "SkipLocked"];
    private static readonly bool[] s_dialects = [false, true];

    /// <summary>Reports each public lock mode independently.</summary>
    public static TheoryData<string> Modes => new(s_modes);

    /// <summary>Reports each mode under both supported SQL dialects.</summary>
    public static TheoryData<bool, string> DialectModes
    {
        get
        {
            var cases = new TheoryData<bool, string>();

            foreach (var (mariaDb, mode) in GetDialectModeCases())
            {
                cases.Add(mariaDb, mode);
            }

            return cases;
        }
    }

    /// <summary>Reports every different ordered mode pair independently under each dialect.</summary>
    public static TheoryData<bool, string, string> ConflictingModes
    {
        get
        {
            var cases = new TheoryData<bool, string, string>();

            foreach (var mariaDb in s_dialects)
            {
                foreach (var first in s_modes)
                {
                    foreach (var second in s_modes)
                    {
                        if (second != first)
                        {
                            cases.Add(mariaDb, first, second);
                        }
                    }
                }
            }

            return cases;
        }
    }

    /// <summary>Shares typed dialect/mode pairs between theory data and parallel compilation controls.</summary>
    private static IEnumerable<(bool MariaDb, string Mode)> GetDialectModeCases()
    {
        foreach (var mariaDb in s_dialects)
        {
            foreach (var mode in s_modes)
            {
                yield return (mariaDb, mode);
            }
        }
    }

    /// <summary>Builds an isolated offline provider whose connection interceptor makes unexpected I/O observable.</summary>
    private static LockingContext CreateContext(
        bool mariaDb,
        ConnectionAttempts connections,
        string? storageEngine = null,
        bool temporal = false,
        MySqlServerVersion? version = null
    )
    {
        var options = MySqlFunctionalTestOptions
            .CreateTransientBuilder<LockingContext>()
            .UseMySql("Server=127.0.0.1;Port=1;Database=locking_offline;User ID=unused;Pooling=false;",
                version
                ?? (mariaDb
                    ? MySqlServerVersion.MariaDb(new Version(12, 3, 3))
                    : MySqlServerVersion.MySql(new Version(8, 4, 0))))
            .AddInterceptors(connections)
            .Options;

        return new LockingContext(options, storageEngine, temporal);
    }

    /// <summary>Applies only the documented public operator, without depending on internal representation.</summary>
    private static IQueryable<TEntity> Mark<TEntity>(
        IQueryable<TEntity> source,
        string mode
    )
        where TEntity : class => mode switch
    {
        "Update" => source.ForUpdate(),
        "Share" => source.ForShare(),
        "NoWait" => source.ForUpdateNoWait(),
        "SkipLocked" => source.ForUpdateSkipLocked(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>Returns the exact supported native final-block clause.</summary>
    private static string Clause(
        bool mariaDb,
        string mode
    ) => mode switch
    {
        "Update" => "FOR UPDATE",
        "Share" => mariaDb
            ? "LOCK IN SHARE MODE"
            : "FOR SHARE",
        "NoWait" => "FOR UPDATE NOWAIT",
        "SkipLocked" => "FOR UPDATE SKIP LOCKED",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>Counts nonoverlapping exact SQL fragments without modifying the query tree.</summary>
    private static int CountOccurrences(
        string sql,
        string fragment
    )
    {
        var count = 0;
        var position = 0;

        while ((position = sql.IndexOf(fragment, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += fragment.Length;
        }

        return count;
    }

    /// <summary>Excludes diagnostic comments when determining whether a query actually contains a lock clause.</summary>
    private static string ExecutableSql(
        string sql
    ) => string.Join('\n',
        sql
            .Split('\n')
            .Where(line => !line
                .TrimStart()
                .StartsWith("--", StringComparison.Ordinal)));

    /// <summary>Records attempted connection opening and prevents accidental external database use.</summary>
    private sealed class ConnectionAttempts : DbConnectionInterceptor
    {
        private int _count;

        /// <summary>Gets the number of synchronous or asynchronous opens attempted by the query.</summary>
        public int Count => Volatile.Read(ref _count);

        /// <inheritdoc />
        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result
        )
        {
            Interlocked.Increment(ref _count);

            throw new InvalidOperationException("Unexpected database opening in the offline locking contract.");
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _count);

            throw new InvalidOperationException("Unexpected database opening in the offline locking contract.");
        }
    }

    /// <summary>Models an arbitrary async provider that would discard an unrecognized locking expression.</summary>
    private sealed class ForeignAsyncSource : IAsyncQueryProvider, IQueryable<LockRow>
    {
        /// <summary>Gets whether the foreign provider was allowed to replace the marked expression.</summary>
        public bool CreateQueryCalled { get; private set; }

        /// <inheritdoc />
        public IQueryable CreateQuery(
            Expression expression
        )
        {
            CreateQueryCalled = true;

            return Array
                .Empty<LockRow>()
                .AsQueryable();
        }

        /// <inheritdoc />
        public IQueryable<TElement> CreateQuery<TElement>(
            Expression expression
        )
        {
            CreateQueryCalled = true;

            return Array
                .Empty<TElement>()
                .AsQueryable();
        }

        /// <inheritdoc />
        public object? Execute(
            Expression expression
        ) => throw new InvalidOperationException("The foreign query must not execute.");

        /// <inheritdoc />
        public TResult Execute<TResult>(
            Expression expression
        ) => throw new InvalidOperationException("The foreign query must not execute.");

        /// <inheritdoc />
        public TResult ExecuteAsync<TResult>(
            Expression expression,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("The foreign query must not execute.");

        /// <inheritdoc />
        public Type ElementType => typeof(LockRow);

        /// <inheritdoc />
        public Expression Expression => Expression.Constant(this);

        /// <inheritdoc />
        public IQueryProvider Provider => this;

        /// <inheritdoc />
        public IEnumerator<LockRow> GetEnumerator() => Enumerable
            .Empty<LockRow>()
            .GetEnumerator();

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class LockingContext : DbContext
    {
        private readonly string? _storageEngine;
        private readonly bool _temporal;

        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public LockingContext(
            DbContextOptions<LockingContext> options,
            string? storageEngine,
            bool temporal
        ) : base(options)
        {
            _storageEngine = storageEngine;
            _temporal = temporal;
        }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<LockRow>(row =>
            {
                row.ToTable("LockRows", table => table.IsTemporal(_temporal));
                row.HasKey(entity => entity.Id);
                row
                    .Property(entity => entity.Name)
                    .HasMaxLength(64);

                row
                    .HasOne(entity => entity.Parent)
                    .WithMany(parent => parent.Children)
                    .HasForeignKey(entity => entity.ParentId)
                    .OnDelete(DeleteBehavior.Restrict);

                if (_storageEngine is not null)
                {
                    row.UseStorageEngine(_storageEngine);
                }
            });

            modelBuilder.Entity<LockParent>(parent =>
            {
                parent.ToTable("LockParents");
                parent.HasKey(entity => entity.Id);
                parent
                    .Property(entity => entity.Name)
                    .HasMaxLength(64);
            });

            modelBuilder
                .Entity<LockView>()
                .ToView("LockView")
                .HasKey(row => row.Id);

            modelBuilder
                .Entity<KeylessLockRow>()
                .HasNoKey()
                .ToTable("KeylessLockRows");
        }
    }

    private sealed class LockRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Value { get; set; }

        public int ParentId { get; set; }

        public LockParent Parent { get; set; } = null!;
    }

    private sealed class LockParent
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<LockRow> Children { get; set; } = [];
    }

    private sealed class LockProjection
    {
        public int Id { get; set; }

        public int Count { get; set; }

        public string? ParentName { get; set; }
    }

    private sealed class LockView
    {
        public int Id { get; set; }
    }

    private sealed class KeylessLockRow
    {
        public int Id { get; set; }
    }
}
