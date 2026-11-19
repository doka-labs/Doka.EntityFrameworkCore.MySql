namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies native JSON membership without discarding mapped function semantics.</summary>
[IntegrationDatabaseTest]
public sealed class MySqlJsonMembershipFunctionIdentityIntegrationTests
{
    /// <summary>Gets the selected profiles from the supported six-engine matrix.</summary>
    public static TheoryData<string> SelectedTargets => new(
        IntegrationTestEnvironment
            .GetSelectedTargets()
            .Where(target => IntegrationTestEnvironment
                .GetSupportedTargets()
                .Contains(target))
            .Select(target => target.ToString()));

    /// <summary>Gets literal and captured custom-function argument cases for every selected supported profile.</summary>
    public static TheoryData<string, bool> PrefixCases
    {
        get
        {
            var cases = new TheoryData<string, bool>();

            foreach (var target in SelectedTargets)
            {
                cases.Add(target, false);
                cases.Add(target, true);
            }

            return cases;
        }
    }

    /// <summary>Gets exact AND/OR membership and complement results, including an empty source array.</summary>
    public static TheoryData<string, bool, bool, int[]> LogicalCases
    {
        get
        {
            var cases = new TheoryData<string, bool, bool, int[]>();

            foreach (var target in SelectedTargets)
            {
                cases.Add(target, false, false, [1, 2, 5]);
                cases.Add(target, false, true, [3, 4]);
                cases.Add(target, true, false, [2]);
                cases.Add(target, true, true, [1, 3, 4, 5]);
            }

            return cases;
        }
    }

    /// <summary>A custom decoder-shaped function preserves transformed matches and rejects numeric near misses.</summary>
    /// <param name="targetName">The selected database engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Custom_unquote_projection_preserves_native_membership_results(
        string targetName
    )
    {
        // Arrange
        await using var store = await FunctionIdentityStore.CreateAsync(targetName, CancellationToken.None);
        await using var context = store.CreateContext();
        var query = context.Rows.Where(row => row
            .Values
            .Select(value => CustomUnquote(value))
            .Contains("mapped:12"));

        var sql = query.ToQueryString();

        // Act
        var ids = await query
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 5], ids);
        Assert.Contains($"`{store.Database}`.`JSON_UNQUOTE`(", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>A custom function retains both its row-dependent value and literal or captured prefix.</summary>
    /// <param name="targetName">The selected database engine profile.</param>
    /// <param name="parameterized">Whether the prefix is a captured query parameter.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(PrefixCases))]
    public async Task Custom_function_with_prefix_preserves_native_membership_results(
        string targetName,
        bool parameterized
    )
    {
        // Arrange
        await using var store = await FunctionIdentityStore.CreateAsync(targetName, CancellationToken.None);
        await using var context = store.CreateContext();
        var prefix = "mapped:";
        var query = parameterized
            ? context.Rows.Where(row => row
                .Values
                .Select(value => CustomFormatWithPrefix(value, prefix))
                .Contains("mapped:12"))
            : context.Rows.Where(row => row
                .Values
                .Select(value => CustomFormatWithPrefix(value, "mapped:"))
                .Contains("mapped:12"));

        var sql = query.ToQueryString();

        // Act
        var ids = await query
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 5], ids);
        Assert.Contains($"`{store.Database}`.`FORMAT_MEMBER_PREFIX`(", sql, StringComparison.Ordinal);
        Assert.Contains(parameterized
                ? "@prefix"
                : "'mapped:'",
            sql,
            StringComparison.Ordinal);

        Assert.Contains("JSON_TABLE(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>A builtin outer decoder must preserve a custom inner function's transformed values.</summary>
    /// <param name="targetName">The selected database engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Custom_quote_under_builtin_unquote_preserves_native_membership_results(
        string targetName
    )
    {
        // Arrange
        await using var store = await FunctionIdentityStore.CreateAsync(targetName, CancellationToken.None);
        await using var context = store.CreateContext();
        var query = context.Rows.Where(row => row
            .Values
            .Select(value => BuiltinUnquote(CustomQuote(value)))
            .Contains("mapped:12"));

        var sql = query.ToQueryString();

        // Act
        var ids = await query
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 5], ids);
        Assert.Contains($"JSON_UNQUOTE(`{store.Database}`.`JSON_QUOTE`(", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>A builtin removes user-string quotes without discarding the provider's identity decoder.</summary>
    /// <param name="targetName">The selected database engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Builtin_unquote_text_projection_preserves_native_membership_results(
        string targetName
    )
    {
        // Arrange
        await using var store = await FunctionIdentityStore.CreateAsync(targetName, CancellationToken.None);
        await using var context = store.CreateContext();
        var query = context.Rows.Where(row => row
            .Texts
            .Select(value => BuiltinUnquote(value))
            .Contains("12"));

        var sql = query.ToQueryString();

        // Act
        var ids = await query
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 5], ids);

        // WHY: The inner quote/unquote pair preserves each stored string; the additional
        // user-requested unquote removes its literal quotes and is not an identity.
        Assert.Contains("JSON_UNQUOTE(JSON_UNQUOTE(JSON_QUOTE(", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>A genuine scalar transform evaluates negative JSON members before matching positive values.</summary>
    /// <param name="targetName">The selected database engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Absolute_value_projection_preserves_native_membership_results(
        string targetName
    )
    {
        // Arrange
        await using var store = await FunctionIdentityStore.CreateAsync(targetName, CancellationToken.None);
        await using var context = store.CreateContext();
        var query = context.Rows.Where(row => row
            .Negatives
            .Select(value => Math.Abs(value))
            .Contains(12));

        var sql = query.ToQueryString();

        // Act
        var ids = await query
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 5], ids);
        Assert.Contains("ABS(", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>A mapped function retains an argument from the current row outside the JSON collection.</summary>
    /// <param name="targetName">The selected database engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Custom_function_with_outer_column_preserves_native_membership_results(
        string targetName
    )
    {
        // Arrange
        await using var store = await FunctionIdentityStore.CreateAsync(targetName, CancellationToken.None);
        await using var context = store.CreateContext();
        var query = context.Rows.Where(row => row
            .Values
            .Select(value => CustomFormatWithPrefix(value, row.Prefix))
            .Contains("mapped:12"));

        var sql = query.ToQueryString();

        // Act
        var ids = await query
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 5], ids);
        Assert.Contains($"`{store.Database}`.`FORMAT_MEMBER_PREFIX`(", sql, StringComparison.Ordinal);
        Assert.Contains(", `r`.`Prefix`)", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>Logical projected values retain their precedence before null-safe membership comparison.</summary>
    /// <param name="targetName">The selected database engine profile.</param>
    /// <param name="useOr">Whether the projected boolean expression uses OR rather than AND.</param>
    /// <param name="negated">Whether the query selects the complement of matching membership.</param>
    /// <param name="expectedIds">The exact sorted row identifiers required by the native seed values.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(LogicalCases))]
    public async Task Logical_projection_preserves_native_membership_precedence(
        string targetName,
        bool useOr,
        bool negated,
        int[] expectedIds
    )
    {
        // Arrange
        await using var store = await FunctionIdentityStore.CreateAsync(targetName, CancellationToken.None);
        await using var context = store.CreateContext();
        var query = (useOr, negated) switch
        {
            (false, false) => context.Rows.Where(row => row
                .Negatives
                .Select(value => value < 0 && value != -12)
                .Contains(false)),
            (false, true) => context.Rows.Where(row => !row
                .Negatives
                .Select(value => value < 0 && value != -12)
                .Contains(false)),
            (true, false) => context.Rows.Where(row => row
                .Negatives
                .Select(value => value < 0 || value == -12)
                .Contains(false)),
            (true, true) => context.Rows.Where(row => !row
                .Negatives
                .Select(value => value < 0 || value == -12)
                .Contains(false)),
        };

        var sql = query.ToQueryString();

        // Act
        var ids = await query
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expectedIds, ids);
        Assert.Contains(useOr
                ? " OR "
                : " AND ",
            sql,
            StringComparison.Ordinal);

        Assert.Contains("JSON_TABLE(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>Represents the schema-qualified function whose name collides with a builtin decoder.</summary>
    private static string CustomUnquote(
        int value
    ) => throw new InvalidOperationException("This function must be translated to SQL.");

    /// <summary>Represents a custom JSON string producer whose name collides with a builtin encoder.</summary>
    private static string CustomQuote(
        int value
    ) => throw new InvalidOperationException("This function must be translated to SQL.");

    /// <summary>Represents the genuine builtin decoder around a custom text expression.</summary>
    private static string BuiltinUnquote(
        string value
    ) => throw new InvalidOperationException("This function must be translated to SQL.");

    /// <summary>Represents a custom projection with one row-dependent and one literal or parameter argument.</summary>
    private static string CustomFormatWithPrefix(
        int value,
        string prefix
    ) => throw new InvalidOperationException("This function must be translated to SQL.");

    /// <summary>Maps the functions into the owning test database rather than a shared server schema.</summary>
    private sealed class FunctionIdentityContext : DbContext
    {
        private readonly string _database;

        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public FunctionIdentityContext(
            DbContextOptions<FunctionIdentityContext> options,
            string database
        ) : base(options)
        {
            _database = database;
        }

        /// <summary>Gets the primitive-collection query root.</summary>
        public DbSet<FunctionIdentityRow> Rows => Set<FunctionIdentityRow>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<FunctionIdentityRow>(entity =>
            {
                entity.ToTable("Rows");
                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .PrimitiveCollection(row => row.Values)
                    .HasColumnType("json");

                entity
                    .PrimitiveCollection(row => row.Texts)
                    .HasColumnType("json");

                entity
                    .PrimitiveCollection(row => row.Negatives)
                    .HasColumnType("json");

                entity
                    .Property(row => row.Prefix)
                    .HasMaxLength(32)
                    .UseCollation("utf8mb4_bin");
            });

            modelBuilder
                .HasDbFunction(() => CustomUnquote(default))
                .HasName("JSON_UNQUOTE")
                .HasSchema(_database)
                .IsBuiltIn(false);

            modelBuilder
                .HasDbFunction(() => CustomQuote(default))
                .HasName("JSON_QUOTE")
                .HasSchema(_database)
                .IsBuiltIn(false);

            modelBuilder
                .HasDbFunction(() => BuiltinUnquote(default!))
                .HasName("JSON_UNQUOTE")
                .IsBuiltIn();

            modelBuilder
                .HasDbFunction(() => CustomFormatWithPrefix(default, default!))
                .HasName("FORMAT_MEMBER_PREFIX")
                .HasSchema(_database)
                .IsBuiltIn(false);
        }
    }

    /// <summary>Stores numeric and quoted-text JSON values independently of the projection under test.</summary>
    private sealed class FunctionIdentityRow
    {
        /// <summary>Gets or sets the row identifier used by the result oracle.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets JSON numeric members.</summary>
        public int[] Values { get; set; } = [];

        /// <summary>Gets or sets strings whose literal quotes require an additional builtin transformation.</summary>
        public string[] Texts { get; set; } = [];

        /// <summary>Gets or sets negative JSON members requiring a real numeric transformation.</summary>
        public int[] Negatives { get; set; } = [];

        /// <summary>Gets or sets a custom-function argument belonging to the enclosing row.</summary>
        public string Prefix { get; set; } = string.Empty;
    }

    /// <summary>Owns one unique database, its stored functions and its isolated EF model cache.</summary>
    private sealed class FunctionIdentityStore : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly string _database;
        private readonly MySqlConnection _observer;
        private readonly ServiceProvider _services;
        private readonly MySqlServerVersion _version;

        /// <summary>Takes ownership of the supplied database resources and configuration.</summary>
        public FunctionIdentityStore(
            string database,
            string connectionString,
            MySqlServerVersion version,
            MySqlConnection observer,
            ServiceProvider services
        )
        {
            _database = database;
            _connectionString = connectionString;
            _version = version;
            _observer = observer;
            _services = services;
        }

        /// <summary>Gets the uniquely owned function schema.</summary>
        internal string Database => _database;

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_observer.State == ConnectionState.Open)
                {
                    // WHY: Cancellation cannot abandon the test's own database or stored functions.
                    await using var command = _observer.CreateCommand();
                    command.CommandText = $"DROP DATABASE IF EXISTS `{_database}`;";
                    await command.ExecuteNonQueryAsync(CancellationToken.None);
                }
            }
            finally
            {
                try
                {
                    await _observer.DisposeAsync();
                }
                finally
                {
                    await _services.DisposeAsync();
                }
            }
        }

        /// <summary>Creates native fixtures without exercising collection translation during setup.</summary>
        internal static async Task<FunctionIdentityStore> CreateAsync(
            string targetName,
            CancellationToken cancellationToken
        )
        {
            var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
            var ownedDatabase = "DokaJsonIdentity_"
                + Guid
                    .NewGuid()
                    .ToString("N", CultureInfo.InvariantCulture);

            var builder = new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetBaseConnectionString(target))
            {
                Database = string.Empty,
                Pooling = false,
                AutoEnlist = false,
            };

            var observerConnection = new MySqlConnection(builder.ConnectionString);
            var ownedServices = new ServiceCollection()
                .AddEntityFrameworkDokaMySql()
                .BuildServiceProvider();

            builder.Database = ownedDatabase;
            var store = new FunctionIdentityStore(
                ownedDatabase,
                builder.ConnectionString,
                IntegrationTestEnvironment.GetServerVersion(target),
                observerConnection,
                ownedServices);

            try
            {
                await observerConnection.OpenAsync(cancellationToken);
                await using var command = observerConnection.CreateCommand();
                command.CommandText =
                    $"CREATE DATABASE `{ownedDatabase}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";

                await command.ExecuteNonQueryAsync(cancellationToken);
                await observerConnection.ChangeDatabaseAsync(ownedDatabase, cancellationToken);
                await using var context = store.CreateContext();
                command.CommandText = context.Database.GenerateCreateScript();
                await command.ExecuteNonQueryAsync(cancellationToken);

                // WHY: Explicit binary return collation avoids inheriting different server defaults;
                // deterministic NO SQL functions avoid changing global binary-log trust settings.
                command.CommandText = $"CREATE FUNCTION `{ownedDatabase}`.`JSON_UNQUOTE`(value INT) "
                    + "RETURNS VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin "
                    + "DETERMINISTIC NO SQL RETURN CONCAT('mapped:', value);";

                await command.ExecuteNonQueryAsync(cancellationToken);
                command.CommandText = $"CREATE FUNCTION `{ownedDatabase}`.`JSON_QUOTE`(value INT) "
                    + "RETURNS VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin "
                    + "DETERMINISTIC NO SQL RETURN CONCAT(CHAR(34), 'mapped:', value, CHAR(34));";

                await command.ExecuteNonQueryAsync(cancellationToken);
                command.CommandText = $"CREATE FUNCTION `{ownedDatabase}`.`FORMAT_MEMBER_PREFIX`(value INT, "
                    + "prefix_text VARCHAR(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin) "
                    + "RETURNS VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin "
                    + "DETERMINISTIC NO SQL RETURN CONCAT(prefix_text, value);";

                await command.ExecuteNonQueryAsync(cancellationToken);

                // WHY: Empty arrays, absent members and the near miss 112 distinguish exact membership
                // from nonempty-array checks or substring matching; both real matches must survive.
                // WHY: Native parameters preserve literal user quotes independently of SQL backslash mode.
                command.Parameters.AddWithValue("@texts1", """["\"1\"","\"12\""]""");
                command.Parameters.AddWithValue("@texts2", """["\"2\"","\"3\""]""");
                command.Parameters.AddWithValue("@texts4", """["\"112\""]""");
                command.Parameters.AddWithValue("@texts5", """["\"12\""]""");
                command.CommandText = "INSERT INTO `Rows` (`Id`, `Values`, `Texts`, `Negatives`, `Prefix`) VALUES "
                    + "(1, '[1,12]', @texts1, '[-1,-12]', 'mapped:'), "
                    + "(2, '[2,3]', @texts2, '[-2,0]', 'other:'), (3, '[]', '[]', '[]', 'mapped:'), "
                    + "(4, '[112]', @texts4, '[-112]', 'mapped:'), (5, '[12]', @texts5, '[-12]', 'mapped:');";

                await command.ExecuteNonQueryAsync(cancellationToken);

                return store;
            }
            catch
            {
                await store.DisposeAsync();

                throw;
            }
        }

        /// <summary>Uses the store-owned provider so function-schema models cannot leak between test cases.</summary>
        internal FunctionIdentityContext CreateContext()
        {
            // WHY: Each schema has different mapped function names; its model cache must share that ownership.
            var options = IntegrationTestDbContextOptions
                .Create<FunctionIdentityContext>()
                .UseInternalServiceProvider(_services)
                .UseMySql(_connectionString, _version)
                .Options;

            return new FunctionIdentityContext(options, _database);
        }
    }
}
