using System.Collections.Concurrent;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Per-database test store for Doka.EntityFrameworkCore.MySql specification suites. Wires the
/// provider's <c>UseMySql</c> registration onto the EF Core spec-test infrastructure, handles
/// database create / clean / drop lifecycle, and exposes the server-version snapshot the spec
/// tests query through <see cref="ServerVersion"/>.
/// </summary>
public class MySqlTestStore : RelationalTestStore
{
    public const int DefaultCommandTimeout = 600;

    private static readonly string s_adminConnectionString = BuildAdminConnectionString();

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_initializationLocks =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, byte> s_initializedSharedStores = new(StringComparer.Ordinal);

    public MySqlTestStore(
        string name,
        bool shared = true
    ) : base(name, shared, new MySqlConnection(BuildConnectionString(name)))
    {
    }

    /// <summary>
    /// Cached server version pulled from the active spec-test connection. The value comes from
    /// <see cref="MySqlTestEnvironment.ServerVersion"/>; specification tests use it to dispatch
    /// per-engine assertions when behavior diverges between MySQL and MariaDB.
    /// </summary>
    public static MySqlServerVersion ServerVersion => MySqlTestEnvironment.ServerVersion;

    public static MySqlTestStore GetOrCreate(string name) => new(name);

    public static MySqlTestStore Create(string name) => new(name, shared: false);

    public override DbContextOptionsBuilder AddProviderOptions(
        DbContextOptionsBuilder builder
    ) => (UseSharedConnectionInProviderOptions && !UseConnectionString
            ? builder.UseMySql(
                Connection,
                ServerVersion,
                provider => provider.UseNetTopologySuite())
            : builder.UseMySql(
                Connection.ConnectionString,
                ServerVersion,
                provider => provider.UseNetTopologySuite()))
        .ConfigureWarnings(
            warnings => warnings.Log(
                RelationalEventId.MultipleCollectionIncludeWarning));

    /// <summary>
    /// Default <see langword="true"/>: the test-store's owned <see cref="DbConnection"/> is
    /// reused across every context the fixture hands out. Tests that bracket each scenario in
    /// a single shared transaction (Updates, NorthwindWhereQuery, BIDT, JsonQuery) rely on
    /// the shared-connection identity so the transaction enrolls correctly.
    /// Override to <see langword="false"/> for fixtures whose tests spawn concurrent contexts
    /// against the same store (Migrations <c>Can_apply_*_in_parallel*</c> patterns); without
    /// per-context connections the parallel <see cref="DbConnection.OpenAsync"/> calls race
    /// on the single shared connection and surface as
    /// <c>InvalidOperationException: Cannot Open when State is Connecting</c>.
    /// </summary>
    public virtual bool UseSharedConnectionInProviderOptions => true;

    /// <summary>
    /// MySQL and MariaDB delimit identifiers with backticks by default; the SQL-standard
    /// double-quote form is only accepted when SQL mode ANSI_QUOTES is on, which would also
    /// flip the meaning of <c>"text"</c> from string literal to identifier and break the rest
    /// of the spec test corpus. EF Core spec tests author raw SQL with the portable
    /// <c>[name]</c> placeholder and rely on the test store to expand it to the engine's native
    /// delimiter form; override these so <see cref="RelationalTestStore.NormalizeDelimitersInRawString"/>
    /// produces backtick-quoted identifiers.
    /// </summary>
    protected override string OpenDelimiter => "`";

    protected override string CloseDelimiter => "`";

    protected override async Task InitializeAsync(
        Func<DbContext> createContext,
        Func<DbContext, Task>? seed,
        Func<DbContext, Task>? clean
    )
    {
        var initializationLock = s_initializationLocks.GetOrAdd(
            Name,
            static _ => new SemaphoreSlim(1, 1));

        await initializationLock.WaitAsync();

        var initializesSharedStore = false;

        try
        {
            // A named shared store is shared only within this test process. Its database may
            // survive from an earlier IDE or CLI run because the Docker volumes are persistent.
            // Claiming the name once per process preserves intentional cross-fixture sharing
            // while preventing stale rows from changing the next run's outcome.
            initializesSharedStore = Shared
                && s_initializedSharedStores.TryAdd(Name, 0);

            if (!Shared || initializesSharedStore)
            {
                await DropDatabaseAsync();
            }

            var databaseFreshlyCreated = await EnsureDatabaseCreatedIfMissingAsync();

            await using var context = createContext();

            if (databaseFreshlyCreated)
            {
                await context.Database.EnsureCreatedResilientlyAsync();
                if (seed is not null)
                {
                    await seed(context);
                }
            }
            else if (clean is not null)
            {
                await clean(context);
            }
        }
        catch
        {
            if (initializesSharedStore)
            {
                s_initializedSharedStores.TryRemove(Name, out _);
            }

            throw;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    public override async Task CleanAsync(
        DbContext context
    )
    {
        // CleanAsync is invoked when the spec-test framework explicitly requests a per-test
        // reset (rare; mostly via the InitializeAsync clean-callback contract). Drop-and-
        // recreate is the heavy hammer; per-table truncation would be lighter but EF Core
        // provides no public helper that walks the model and emits TRUNCATE statements
        // engine-agnostically. The provider's own MySqlRelationalDatabaseCreator handles
        // the recreate path consistently with the rest of the suite.
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (!Shared)
        {
            await DropDatabaseAsync();
        }

        GC.SuppressFinalize(this);
    }

    private async Task<bool> EnsureDatabaseCreatedIfMissingAsync()
    {
        await using var admin = new MySqlConnection(s_adminConnectionString);
        await admin.OpenAsync();

        if (await DatabaseExistsAsync(admin, Name))
        {
            return false;
        }

        await ExecuteNonQueryAsync(admin, $"CREATE DATABASE `{Name}` CHARACTER SET utf8mb4;");
        return true;
    }

    private async Task DropDatabaseAsync()
    {
        await using var admin = new MySqlConnection(s_adminConnectionString);
        await admin.OpenAsync();
        await ExecuteNonQueryAsync(admin, $"DROP DATABASE IF EXISTS `{Name}`;");
    }

    private static async Task<bool> DatabaseExistsAsync(
        MySqlConnection admin,
        string name
    )
    {
        await using var command = admin.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = @name;";
        command.CommandTimeout = DefaultCommandTimeout;
        command.Parameters.AddWithValue("@name", name);

        var result = await command.ExecuteScalarAsync();
        return result is not null && Convert.ToInt64(result, CultureInfo.InvariantCulture) > 0;
    }

    private static async Task ExecuteNonQueryAsync(
        MySqlConnection admin,
        string sql
    )
    {
        await using var command = admin.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = DefaultCommandTimeout;
        await command.ExecuteNonQueryAsync();
    }

    private static string BuildConnectionString(
        string databaseName
    ) => new MySqlConnectionStringBuilder(MySqlTestEnvironment.ConnectionString)
    {
        Database = databaseName,
        DefaultCommandTimeout = (uint)DefaultCommandTimeout,
        AllowUserVariables = true,
        UseAffectedRows = false,
        // Non-shared EF specification fixtures repeatedly create contexts from the
        // same DbConnection. MySqlConnector redacts the password after the first
        // open unless this flag is set, leaving later database-lifecycle operations
        // unable to create their server-level connection.
        PersistSecurityInfo = true,
        // Match what the provider's MySqlRelationalConnection.CreateDbConnection() sets
        // on connections built from UseMySql(connectionString); the test infrastructure
        // bypasses that path by constructing the MySqlConnection directly and passing it
        // through UseMySql(DbConnection, ...), so the GuidFormat has to be set explicitly
        // on the test connection string. Binary16 matches our MySqlGuidBinaryTypeMapping's
        // RFC 4122 / big-endian X'HEX' literal-emission path so seed inserts (HasData) and
        // parameter-bound writes (AddAsync + SaveChangesAsync) land byte-identical.
        GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
    }.ConnectionString;

    private static string BuildAdminConnectionString()
    {
        var builder = new MySqlConnectionStringBuilder(MySqlTestEnvironment.ConnectionString)
        {
            DefaultCommandTimeout = (uint)DefaultCommandTimeout,
        };

        builder.Remove("Database");
        return builder.ConnectionString;
    }
}
