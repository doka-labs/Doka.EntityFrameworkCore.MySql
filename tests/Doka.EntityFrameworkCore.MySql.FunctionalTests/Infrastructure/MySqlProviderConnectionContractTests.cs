using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies connection contracts whose behavior depends on the live
/// MySqlConnector handshake and server row-count semantics.
/// </summary>
[Trait("Category", "Live")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class MySqlProviderConnectionContractTests
{
    private const string MatchedRowTable = "ProviderContractMatchedRows";
    private const string GuidItemTable = "ProviderContractGuidItems";
    private const string GuidChildTable = "ProviderContractGuidChildren";

    /// <summary>Verifies a matched-row update succeeds even when its stored value does not change.</summary>
    /// <param name="bootstrap">The configured connection ownership path.</param>
    /// <param name="useAsync">Whether to exercise asynchronous persistence and reads.</param>
    /// <param name="converterBacked">Whether normalization makes a CLR change store-equivalent.</param>
    [Theory]
    [InlineData("connection-string", false, false)]
    [InlineData("connection-string", true, false)]
    [InlineData("connection-string", false, true)]
    [InlineData("connection-string", true, true)]
    [InlineData("db-connection", false, false)]
    [InlineData("db-connection", true, false)]
    [InlineData("db-connection", false, true)]
    [InlineData("db-connection", true, true)]
    [InlineData("data-source", false, false)]
    [InlineData("data-source", true, false)]
    [InlineData("data-source", false, true)]
    [InlineData("data-source", true, true)]
    public async Task Matched_row_semantics_preserve_semantically_unchanged_updates(
        string bootstrap,
        bool useAsync,
        bool converterBacked
    )
    {
        // Arrange
        await using var lease = CreateOptionsLease<MatchedRowContext>(bootstrap, requireUserVariables: false);
        await using var context = new MatchedRowContext(lease.Options);

        await ResetMatchedRowTableAsync(context);

        try
        {
            context.Items.AddRange(CreateMatchedRowItems());

            int inserted;

            if (useAsync)
            {
                inserted = await context.SaveChangesAsync(CancellationToken.None);
            }
            else
            {
                // WHY: The synchronous case qualifies the complete sync persistence path.
                // ReSharper disable once MethodHasAsyncOverload
                inserted = context.SaveChanges();
            }

            // WHY: Both rows must exist before either unchanged-update case runs.
            Assert.Equal(2, inserted);
            context.ChangeTracker.Clear();

            var id = converterBacked ? 2 : 1;
            MatchedRowItem item;

            if (useAsync)
            {
                item = await context.Items.SingleAsync(row => row.Id == id, CancellationToken.None);
            }
            else
            {
                // WHY: Sync reads are part of this independently qualified API path.
                // ReSharper disable once MethodHasAsyncOverload
                item = context.Items.Single(row => row.Id == id);
            }

            if (converterBacked)
            {
                item.NormalizedValue = "STABLE";
            }
            else
            {
                context.Entry(item).Property(row => row.Value).IsModified = true;
            }

            // Act
            int saved;

            if (useAsync)
            {
                saved = await context.SaveChangesAsync(CancellationToken.None);
            }
            else
            {
                // WHY: This row-count assertion must exercise synchronous SaveChanges.
                // ReSharper disable once MethodHasAsyncOverload
                saved = context.SaveChanges();
            }

            // Assert
            Assert.Equal(1, saved);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                $"DROP TABLE IF EXISTS `{MatchedRowTable}`;",
                CancellationToken.None);
        }
    }

    /// <summary>Verifies matched-row semantics still reject each real concurrency conflict.</summary>
    /// <param name="deletedRow">Whether the row was deleted rather than its token changed.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Matched_row_semantics_preserve_real_concurrency_conflicts(
        bool deletedRow
    )
    {
        // Arrange
        await using var lease = CreateOptionsLease<MatchedRowContext>(
            "connection-string",
            requireUserVariables: false);

        await using var context = new MatchedRowContext(lease.Options);

        await ResetMatchedRowTableAsync(context);

        try
        {
            context.Items.AddRange(CreateMatchedRowItems());

            // WHY: Seed completeness prevents a missing fixture row from
            // masquerading as the intended optimistic-concurrency conflict.
            Assert.Equal(2, await context.SaveChangesAsync(CancellationToken.None));
            context.ChangeTracker.Clear();

            var id = deletedRow ? 1 : 2;
            var item = await context.Items.SingleAsync(row => row.Id == id, CancellationToken.None);
            var interference = deletedRow
                ? $"DELETE FROM `{MatchedRowTable}` WHERE `Id` = 1;"
                : $"UPDATE `{MatchedRowTable}` SET `Token` = 'token-c' WHERE `Id` = 2;";

            await context.Database.ExecuteSqlRawAsync(interference, CancellationToken.None);
            item.Value = "local-update";

            // Act
            var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

            // Assert
            Assert.IsType<DbUpdateConcurrencyException>(exception);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                $"DROP TABLE IF EXISTS `{MatchedRowTable}`;",
                CancellationToken.None);
        }
    }

    /// <summary>Verifies user variables retain their value on the provider's open session.</summary>
    /// <param name="bootstrap">The configured connection ownership path.</param>
    [Theory]
    [InlineData("connection-string")]
    [InlineData("db-connection")]
    [InlineData("data-source")]
    public async Task Required_user_variables_execute_session_local_arithmetic(
        string bootstrap
    )
    {
        // Arrange
        await using var lease = CreateOptionsLease<DbContext>(bootstrap, requireUserVariables: true);
        await using var context = new DbContext(lease.Options);

        await context.Database.OpenConnectionAsync(CancellationToken.None);

        try
        {
            var connection = context.Database.GetDbConnection();

            // Act
            await ExecuteNonQueryAsync(connection, "SET @doka_contract_value = 41;", CancellationToken.None);

            var result = Convert.ToInt64(
                await ExecuteScalarAsync(connection, "SELECT @doka_contract_value + 1;", CancellationToken.None),
                CultureInfo.InvariantCulture);

            // Assert
            Assert.Equal(42L, result);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Verifies preparation, execution and deallocation remain on the same provider session.</summary>
    /// <param name="bootstrap">The configured connection ownership path.</param>
    [Theory]
    [InlineData("connection-string")]
    [InlineData("db-connection")]
    [InlineData("data-source")]
    public async Task Required_user_variables_execute_a_session_local_prepared_program(
        string bootstrap
    )
    {
        // Arrange
        await using var lease = CreateOptionsLease<DbContext>(bootstrap, requireUserVariables: true);
        await using var context = new DbContext(lease.Options);

        await context.Database.OpenConnectionAsync(CancellationToken.None);

        try
        {
            var connection = context.Database.GetDbConnection();

            // Act
            // WHY: Prepared statements are session-owned; these commands form
            // one program and cannot be split across independent connections.
            await ExecuteNonQueryAsync(connection, "SET @doka_contract_sql = 'SELECT 43';", CancellationToken.None);
            await ExecuteNonQueryAsync(
                connection,
                "PREPARE doka_contract_statement FROM @doka_contract_sql;",
                CancellationToken.None);

            var result = Convert.ToInt64(
                await ExecuteScalarAsync(connection, "EXECUTE doka_contract_statement;", CancellationToken.None),
                CultureInfo.InvariantCulture);

            await ExecuteNonQueryAsync(connection, "DEALLOCATE PREPARE doka_contract_statement;", CancellationToken.None);

            // Assert
            Assert.Equal(43L, result);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Verifies tracked materialization preserves binary and Char36 GUID mappings together.</summary>
    /// <param name="bootstrap">The configured connection ownership path.</param>
    [Theory]
    [InlineData("connection-string")]
    [InlineData("db-connection")]
    [InlineData("data-source")]
    public async Task Binary16_transport_materializes_tracked_guid_mappings(
        string bootstrap
    )
    {
        // Arrange
        await using var lease = CreateOptionsLease<GuidContext>(bootstrap, requireUserVariables: false);
        await using var context = new GuidContext(lease.Options);

        await ResetGuidTablesAsync(context);

        try
        {
            var expected = await SeedGuidItemAsync(context);

            // Act
            var tracked = await context.Items.Include(item => item.Children)
                .SingleAsync(item => item.Id == expected.Id, CancellationToken.None);

            // Assert
            Assert.Equal(expected.AlternateId, tracked.AlternateId);
            Assert.Equal(expected.BinaryValue, tracked.BinaryValue);
            Assert.Null(tracked.OptionalBinaryValue);
            Assert.Equal(expected.TextValue, tracked.TextValue);
            Assert.Null(tracked.OptionalTextValue);
            Assert.Equal(expected.Children.Single().ExternalId, Assert.Single(tracked.Children).ExternalId);
        }
        finally
        {
            await DropGuidTablesAsync(context);
        }
    }

    /// <summary>Verifies a no-tracking alternate-key query preserves the GUID primary key.</summary>
    /// <param name="bootstrap">The configured connection ownership path.</param>
    [Theory]
    [InlineData("connection-string")]
    [InlineData("db-connection")]
    [InlineData("data-source")]
    public async Task Binary16_transport_materializes_no_tracking_guid_query(
        string bootstrap
    )
    {
        // Arrange
        await using var lease = CreateOptionsLease<GuidContext>(bootstrap, requireUserVariables: false);
        await using var context = new GuidContext(lease.Options);

        await ResetGuidTablesAsync(context);

        try
        {
            var expected = await SeedGuidItemAsync(context);

            // Act
            var loaded = await context.Items.AsNoTracking()
                .SingleAsync(item => item.AlternateId == expected.AlternateId, CancellationToken.None);

            // Assert
            Assert.Equal(expected.Id, loaded.Id);
        }
        finally
        {
            await DropGuidTablesAsync(context);
        }
    }

    /// <summary>Verifies Find translates a GUID key through the configured binary transport.</summary>
    /// <param name="bootstrap">The configured connection ownership path.</param>
    [Theory]
    [InlineData("connection-string")]
    [InlineData("db-connection")]
    [InlineData("data-source")]
    public async Task Binary16_transport_finds_guid_primary_key(
        string bootstrap
    )
    {
        // Arrange
        await using var lease = CreateOptionsLease<GuidContext>(bootstrap, requireUserVariables: false);
        await using var context = new GuidContext(lease.Options);

        await ResetGuidTablesAsync(context);

        try
        {
            var expected = await SeedGuidItemAsync(context);

            // Act
            var found = await context.Items.FindAsync([expected.Id], CancellationToken.None);

            // Assert
            Assert.NotNull(found);
            Assert.Equal(expected.Id, found.Id);
        }
        finally
        {
            await DropGuidTablesAsync(context);
        }
    }

    /// <summary>Verifies compiled GUID queries retain their included relationship.</summary>
    /// <param name="bootstrap">The configured connection ownership path.</param>
    [Theory]
    [InlineData("connection-string")]
    [InlineData("db-connection")]
    [InlineData("data-source")]
    public async Task Binary16_transport_materializes_compiled_guid_query(
        string bootstrap
    )
    {
        // Arrange
        await using var lease = CreateOptionsLease<GuidContext>(bootstrap, requireUserVariables: false);
        await using var context = new GuidContext(lease.Options);

        await ResetGuidTablesAsync(context);

        try
        {
            var expected = await SeedGuidItemAsync(context);
            var query = CreateCompiledGuidQuery();

            // Act
            var compiled = await query(context, expected.Id).SingleAsync(CancellationToken.None);

            // Assert
            Assert.Equal(expected.Id, compiled.Id);
            Assert.Single(compiled.Children);
        }
        finally
        {
            await DropGuidTablesAsync(context);
        }
    }

    /// <summary>Verifies updates preserve both nullable binary and Char36 GUID values.</summary>
    /// <param name="bootstrap">The configured connection ownership path.</param>
    [Theory]
    [InlineData("connection-string")]
    [InlineData("db-connection")]
    [InlineData("data-source")]
    public async Task Binary16_transport_updates_optional_guid_values(
        string bootstrap
    )
    {
        // Arrange
        await using var lease = CreateOptionsLease<GuidContext>(bootstrap, requireUserVariables: false);
        await using var context = new GuidContext(lease.Options);

        await ResetGuidTablesAsync(context);

        try
        {
            var expected = await SeedGuidItemAsync(context);
            var query = CreateCompiledGuidQuery();
            var compiled = await query(context, expected.Id).SingleAsync(CancellationToken.None);
            var optionalBinary = expected.Children.Single().ExternalId;
            compiled.OptionalBinaryValue = optionalBinary;
            compiled.OptionalTextValue = expected.TextValue;

            // Act
            var saved = await context.SaveChangesAsync(CancellationToken.None);

            context.ChangeTracker.Clear();

            var loaded = await context.Items.SingleAsync(item => item.Id == expected.Id, CancellationToken.None);

            // Assert
            Assert.Equal(1, saved);
            Assert.Equal(optionalBinary, loaded.OptionalBinaryValue);
            Assert.Equal(expected.TextValue, loaded.OptionalTextValue);
        }
        finally
        {
            await DropGuidTablesAsync(context);
        }
    }

    /// <summary>Verifies raw binary and character storage independently of EF materialization.</summary>
    /// <param name="bootstrap">The configured connection ownership path.</param>
    [Theory]
    [InlineData("connection-string")]
    [InlineData("db-connection")]
    [InlineData("data-source")]
    public async Task Binary16_transport_persists_binary_and_char36_storage(
        string bootstrap
    )
    {
        // Arrange
        await using var lease = CreateOptionsLease<GuidContext>(bootstrap, requireUserVariables: false);
        await using var context = new GuidContext(lease.Options);

        await ResetGuidTablesAsync(context);

        try
        {
            var expected = await SeedGuidItemAsync(context);
            var connection = context.Database.GetDbConnection();

            await context.Database.OpenConnectionAsync(CancellationToken.None);

            // Act
            var binaryHex = Convert.ToString(
                await ExecuteScalarAsync(connection, $"SELECT HEX(`Id`) FROM `{GuidItemTable}`;", CancellationToken.None),
                CultureInfo.InvariantCulture);

            var text = Convert.ToString(
                await ExecuteScalarAsync(
                    connection,
                    $"SELECT CAST(`TextValue` AS CHAR(36)) FROM `{GuidItemTable}`;",
                    CancellationToken.None),
                CultureInfo.InvariantCulture);

            // Assert
            Assert.Equal("00112233445566778899AABBCCDDEEFF", binaryHex);
            Assert.Equal(expected.TextValue.ToString("D", CultureInfo.InvariantCulture), text);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            await DropGuidTablesAsync(context);
        }
    }

    private static MatchedRowItem[] CreateMatchedRowItems() =>
    [
        new()
        {
            Id = 1,
            Value = "unchanged",
            NormalizedValue = "stable",
            Token = "token-a",
        },
        new()
        {
            Id = 2,
            Value = "converter",
            NormalizedValue = "stable",
            Token = "token-b",
        },
    ];

    private static async Task<GuidItem> SeedGuidItemAsync(
        GuidContext context
    )
    {
        var item = new GuidItem
        {
            Id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            AlternateId = Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f"),
            BinaryValue = Guid.Parse("20314253-6475-8697-a8b9-cadbecfd0e1f"),
            OptionalBinaryValue = null,
            TextValue = Guid.Parse("30415263-7485-96a7-b8c9-daebfc0d1e2f"),
            OptionalTextValue = null,
            Children =
            {
                new GuidChild
                {
                    Sequence = 1,
                    ExternalId = Guid.Parse("40516273-8495-a6b7-c8d9-eafb0c1d2e3f"),
                },
            },
        };

        context.Items.Add(item);

        // WHY: Every transport case requires the same complete parent/child
        // graph; qualify the seed before observing a particular query path.
        Assert.Equal(2, await context.SaveChangesAsync(CancellationToken.None));
        context.ChangeTracker.Clear();

        return item;
    }

    private static Func<GuidContext, Guid, IAsyncEnumerable<GuidItem>> CreateCompiledGuidQuery() =>
        EF.CompileAsyncQuery((GuidContext queryContext, Guid queryId) => queryContext.Items
            .Include(item => item.Children)
            .Where(item => item.Id == queryId));

    private static ProviderOptionsLease<TContext> CreateOptionsLease<TContext>(
        string bootstrap,
        bool requireUserVariables
    )
        where TContext : DbContext
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>();
        var serverVersion = MySqlTestEnvironment.ServerVersion;

        void Configure(
            MySqlDbContextOptionsBuilder providerOptions
        )
        {
            if (requireUserVariables)
            {
                providerOptions.RequireUserVariables();
            }
        }

        switch (bootstrap)
        {
            case "connection-string":
                builder.UseMySql(
                    MySqlTestEnvironment.ConnectionString,
                    serverVersion,
                    Configure);

                return new ProviderOptionsLease<TContext>(builder.Options, null, null);
            case "db-connection":
                var connection = new MySqlConnection(
                    CreateBorrowedConnectionString(requireUserVariables));

                builder.UseMySql(connection, serverVersion, Configure);

                return new ProviderOptionsLease<TContext>(builder.Options, connection, null);
            case "data-source":
                var dataSource = new MySqlDataSourceBuilder(
                    CreateBorrowedConnectionString(requireUserVariables)).Build();

                builder.UseMySql(dataSource, serverVersion, Configure);

                return new ProviderOptionsLease<TContext>(builder.Options, null, dataSource);
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(bootstrap),
                    bootstrap,
                    "Unknown connection bootstrap path.");
        }
    }

    private static string CreateBorrowedConnectionString(
        bool allowUserVariables
    ) => new MySqlConnectionStringBuilder(MySqlTestEnvironment.ConnectionString)
    {
        AllowUserVariables = allowUserVariables,
        GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
        UseAffectedRows = false,
    }.ConnectionString;

    private static async Task ResetMatchedRowTableAsync(
        DbContext context
    )
    {
        await context.Database.ExecuteSqlRawAsync(
            $"DROP TABLE IF EXISTS `{MatchedRowTable}`;",
            CancellationToken.None);

        await context.Database.ExecuteSqlRawAsync(
            $"CREATE TABLE `{MatchedRowTable}` ("
            + "`Id` int NOT NULL, "
            + "`Value` varchar(64) NOT NULL, "
            + "`NormalizedValue` varchar(64) NOT NULL, "
            + "`Token` varchar(64) NOT NULL, "
            + "PRIMARY KEY (`Id`));",
            CancellationToken.None);
    }

    private static async Task ResetGuidTablesAsync(
        DbContext context
    )
    {
        await DropGuidTablesAsync(context);

        await context.Database.ExecuteSqlRawAsync(
            $"CREATE TABLE `{GuidItemTable}` ("
            + "`Id` binary(16) NOT NULL, "
            + "`AlternateId` binary(16) NOT NULL, "
            + "`BinaryValue` binary(16) NOT NULL, "
            + "`OptionalBinaryValue` binary(16) NULL, "
            + "`TextValue` char(36) NOT NULL, "
            + "`OptionalTextValue` char(36) NULL, "
            + "PRIMARY KEY (`Id`), "
            + "UNIQUE KEY `AK_ProviderContractGuidItems_AlternateId` (`AlternateId`));",
            CancellationToken.None);

        await context.Database.ExecuteSqlRawAsync(
            $"CREATE TABLE `{GuidChildTable}` ("
            + "`OwnerId` binary(16) NOT NULL, "
            + "`Sequence` int NOT NULL, "
            + "`ExternalId` binary(16) NULL, "
            + "PRIMARY KEY (`OwnerId`, `Sequence`), "
            + "CONSTRAINT `FK_ProviderContractGuidChildren_Items` FOREIGN KEY (`OwnerId`) "
            + $"REFERENCES `{GuidItemTable}` (`Id`) ON DELETE CASCADE);",
            CancellationToken.None);
    }

    private static async Task DropGuidTablesAsync(
        DbContext context
    )
    {
        await context.Database.ExecuteSqlRawAsync(
            $"DROP TABLE IF EXISTS `{GuidChildTable}`;",
            CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync(
            $"DROP TABLE IF EXISTS `{GuidItemTable}`;",
            CancellationToken.None);
    }

    private static async Task ExecuteNonQueryAsync(
        DbConnection connection,
        string commandText,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<object?> ExecuteScalarAsync(
        DbConnection connection,
        string commandText,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;

        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private sealed class ProviderOptionsLease<TContext> : IAsyncDisposable
        where TContext : DbContext
    {
        private readonly DbConnection? _connection;
        private readonly MySqlDataSource? _dataSource;

        public ProviderOptionsLease(
            DbContextOptions<TContext> options,
            DbConnection? connection,
            MySqlDataSource? dataSource
        )
        {
            Options = options;
            _connection = connection;
            _dataSource = dataSource;
        }

        public DbContextOptions<TContext> Options { get; }

        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            if (_dataSource is not null)
            {
                await _dataSource.DisposeAsync();
            }
        }
    }

    private sealed class MatchedRowContext : DbContext
    {
        public MatchedRowContext(
            DbContextOptions<MatchedRowContext> options
        ) : base(options) { }

        public DbSet<MatchedRowItem> Items => Set<MatchedRowItem>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<MatchedRowItem>();
            entity.ToTable(MatchedRowTable);
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Value).HasMaxLength(64);
            entity.Property(item => item.Token).HasMaxLength(64).IsConcurrencyToken();
            entity.Property(item => item.NormalizedValue)
                .HasMaxLength(64)
                .HasConversion(
                    value => value.ToLowerInvariant(),
                    value => value);
        }
    }

    private sealed class MatchedRowItem
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;

        public string NormalizedValue { get; set; } = string.Empty;

        public string Token { get; set; } = string.Empty;
    }

    private sealed class GuidContext : DbContext
    {
        public GuidContext(
            DbContextOptions<GuidContext> options
        ) : base(options) { }

        public DbSet<GuidItem> Items => Set<GuidItem>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var item = modelBuilder.Entity<GuidItem>();
            item.ToTable(GuidItemTable);
            item.HasKey(entity => entity.Id);
            item.HasAlternateKey(entity => entity.AlternateId);
            item.Property(entity => entity.TextValue)
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
            item.Property(entity => entity.OptionalTextValue)
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36);

            var child = modelBuilder.Entity<GuidChild>();
            child.ToTable(GuidChildTable);
            child.HasKey(entity => new
            {
                entity.OwnerId,
                entity.Sequence,
            });
            child.HasOne(entity => entity.Owner)
                .WithMany(entity => entity.Children)
                .HasForeignKey(entity => entity.OwnerId);
        }
    }

    private sealed class GuidItem
    {
        public Guid Id { get; set; }

        public Guid AlternateId { get; set; }

        public Guid BinaryValue { get; set; }

        public Guid? OptionalBinaryValue { get; set; }

        public Guid TextValue { get; set; }

        public Guid? OptionalTextValue { get; set; }

        public ICollection<GuidChild> Children { get; } = [];
    }

    private sealed class GuidChild
    {
        public Guid OwnerId { get; set; }

        public int Sequence { get; set; }

        public Guid? ExternalId { get; set; }

        public GuidItem Owner { get; set; } = null!;
    }
}
