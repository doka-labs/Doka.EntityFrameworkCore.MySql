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

    [Theory]
    [InlineData("connection-string", false)]
    [InlineData("connection-string", true)]
    [InlineData("db-connection", false)]
    [InlineData("db-connection", true)]
    [InlineData("data-source", false)]
    [InlineData("data-source", true)]
    public async Task Matched_row_semantics_preserve_semantically_unchanged_updates(
        string bootstrap,
        bool async
    )
    {
        await using var lease = CreateOptionsLease<MatchedRowContext>(
            bootstrap,
            requireUserVariables: false);
        await using var context = new MatchedRowContext(lease.Options);

        await ResetMatchedRowTableAsync(context);

        try
        {
            context.Items.AddRange(
                new MatchedRowItem
                {
                    Id = 1,
                    Value = "unchanged",
                    NormalizedValue = "stable",
                    Token = "token-a",
                },
                new MatchedRowItem
                {
                    Id = 2,
                    Value = "converter",
                    NormalizedValue = "stable",
                    Token = "token-b",
                });

            if (async)
            {
                Assert.Equal(2, await context.SaveChangesAsync(CancellationToken.None));
            }
            else
            {
                Assert.Equal(2, context.SaveChanges());
            }

            context.ChangeTracker.Clear();

            var unchanged = async
                ? await context.Items.SingleAsync(item => item.Id == 1, CancellationToken.None)
                : context.Items.Single(item => item.Id == 1);

            context.Entry(unchanged).Property(item => item.Value).IsModified = true;

            if (async)
            {
                Assert.Equal(1, await context.SaveChangesAsync(CancellationToken.None));
            }
            else
            {
                Assert.Equal(1, context.SaveChanges());
            }

            context.ChangeTracker.Clear();

            var converterBacked = async
                ? await context.Items.SingleAsync(item => item.Id == 2, CancellationToken.None)
                : context.Items.Single(item => item.Id == 2);

            converterBacked.NormalizedValue = "STABLE";

            if (async)
            {
                Assert.Equal(1, await context.SaveChangesAsync(CancellationToken.None));
            }
            else
            {
                Assert.Equal(1, context.SaveChanges());
            }
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                $"DROP TABLE IF EXISTS `{MatchedRowTable}`;",
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task Matched_row_semantics_preserve_real_concurrency_conflicts()
    {
        await using var lease = CreateOptionsLease<MatchedRowContext>(
            "connection-string",
            requireUserVariables: false);
        await using var context = new MatchedRowContext(lease.Options);

        await ResetMatchedRowTableAsync(context);

        try
        {
            context.Items.AddRange(
                new MatchedRowItem
                {
                    Id = 1,
                    Value = "deleted",
                    NormalizedValue = "first",
                    Token = "token-a",
                },
                new MatchedRowItem
                {
                    Id = 2,
                    Value = "changed",
                    NormalizedValue = "second",
                    Token = "token-b",
                });

            Assert.Equal(2, await context.SaveChangesAsync(CancellationToken.None));
            context.ChangeTracker.Clear();

            var deleted = await context.Items.SingleAsync(
                item => item.Id == 1,
                CancellationToken.None);

            await context.Database.ExecuteSqlRawAsync(
                $"DELETE FROM `{MatchedRowTable}` WHERE `Id` = 1;",
                CancellationToken.None);

            deleted.Value = "local-update";

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                context.SaveChangesAsync(CancellationToken.None));

            context.ChangeTracker.Clear();

            var changed = await context.Items.SingleAsync(
                item => item.Id == 2,
                CancellationToken.None);

            await context.Database.ExecuteSqlRawAsync(
                $"UPDATE `{MatchedRowTable}` SET `Token` = 'token-c' WHERE `Id` = 2;",
                CancellationToken.None);

            changed.Value = "local-update";

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                context.SaveChangesAsync(CancellationToken.None));
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                $"DROP TABLE IF EXISTS `{MatchedRowTable}`;",
                CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("connection-string")]
    [InlineData("db-connection")]
    [InlineData("data-source")]
    public async Task Required_user_variables_execute_a_session_local_prepared_program(
        string bootstrap
    )
    {
        await using var lease = CreateOptionsLease<DbContext>(
            bootstrap,
            requireUserVariables: true);
        await using var context = new DbContext(lease.Options);

        await context.Database.OpenConnectionAsync(CancellationToken.None);

        try
        {
            var connection = context.Database.GetDbConnection();

            await ExecuteNonQueryAsync(
                connection,
                "SET @doka_contract_value = 41;",
                CancellationToken.None);

            Assert.Equal(
                42L,
                Convert.ToInt64(
                    await ExecuteScalarAsync(
                        connection,
                        "SELECT @doka_contract_value + 1;",
                        CancellationToken.None),
                    CultureInfo.InvariantCulture));

            await ExecuteNonQueryAsync(
                connection,
                "SET @doka_contract_sql = 'SELECT 43';",
                CancellationToken.None);
            await ExecuteNonQueryAsync(
                connection,
                "PREPARE doka_contract_statement FROM @doka_contract_sql;",
                CancellationToken.None);

            Assert.Equal(
                43L,
                Convert.ToInt64(
                    await ExecuteScalarAsync(
                        connection,
                        "EXECUTE doka_contract_statement;",
                        CancellationToken.None),
                    CultureInfo.InvariantCulture));

            await ExecuteNonQueryAsync(
                connection,
                "DEALLOCATE PREPARE doka_contract_statement;",
                CancellationToken.None);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [Theory]
    [InlineData("connection-string")]
    [InlineData("db-connection")]
    [InlineData("data-source")]
    public async Task Binary16_transport_preserves_default_and_char36_guid_mappings(
        string bootstrap
    )
    {
        await using var lease = CreateOptionsLease<GuidContext>(
            bootstrap,
            requireUserVariables: false);
        await using var context = new GuidContext(lease.Options);

        await ResetGuidTablesAsync(context);

        var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var alternateId = Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f");
        var binaryValue = Guid.Parse("20314253-6475-8697-a8b9-cadbecfd0e1f");
        var textValue = Guid.Parse("30415263-7485-96a7-b8c9-daebfc0d1e2f");
        var childValue = Guid.Parse("40516273-8495-a6b7-c8d9-eafb0c1d2e3f");

        try
        {
            context.Items.Add(
                new GuidItem
                {
                    Id = id,
                    AlternateId = alternateId,
                    BinaryValue = binaryValue,
                    OptionalBinaryValue = null,
                    TextValue = textValue,
                    OptionalTextValue = null,
                    Children =
                    {
                        new GuidChild
                        {
                            Sequence = 1,
                            ExternalId = childValue,
                        },
                    },
                });

            Assert.Equal(2, await context.SaveChangesAsync(CancellationToken.None));
            context.ChangeTracker.Clear();

            var tracked = await context.Items
                .Include(item => item.Children)
                .SingleAsync(item => item.Id == id, CancellationToken.None);

            Assert.Equal(alternateId, tracked.AlternateId);
            Assert.Equal(binaryValue, tracked.BinaryValue);
            Assert.Null(tracked.OptionalBinaryValue);
            Assert.Equal(textValue, tracked.TextValue);
            Assert.Null(tracked.OptionalTextValue);
            Assert.Equal(childValue, Assert.Single(tracked.Children).ExternalId);

            context.ChangeTracker.Clear();

            var noTracking = await context.Items
                .AsNoTracking()
                .SingleAsync(item => item.AlternateId == alternateId, CancellationToken.None);

            var found = await context.Items.FindAsync([id], CancellationToken.None);

            Assert.Equal(id, noTracking.Id);
            Assert.NotNull(found);
            Assert.Equal(id, found.Id);

            context.ChangeTracker.Clear();

            var compiledQuery = EF.CompileAsyncQuery((GuidContext queryContext, Guid queryId) =>
                queryContext.Items
                    .Include(item => item.Children)
                    .Where(item => item.Id == queryId));

            var compiled = await compiledQuery(context, id)
                .SingleAsync(CancellationToken.None);

            Assert.Equal(id, compiled.Id);
            Assert.Single(compiled.Children);

            compiled.OptionalBinaryValue = childValue;
            compiled.OptionalTextValue = textValue;

            Assert.Equal(1, await context.SaveChangesAsync(CancellationToken.None));

            var connection = context.Database.GetDbConnection();
            await context.Database.OpenConnectionAsync(CancellationToken.None);

            Assert.Equal(
                "00112233445566778899AABBCCDDEEFF",
                Convert.ToString(
                    await ExecuteScalarAsync(
                        connection,
                        $"SELECT HEX(`Id`) FROM `{GuidItemTable}`;",
                        CancellationToken.None),
                    CultureInfo.InvariantCulture));

            Assert.Equal(
                textValue.ToString("D", CultureInfo.InvariantCulture),
                Convert.ToString(
                    await ExecuteScalarAsync(
                        connection,
                        $"SELECT CAST(`TextValue` AS CHAR(36)) FROM `{GuidItemTable}`;",
                        CancellationToken.None),
                    CultureInfo.InvariantCulture));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            await DropGuidTablesAsync(context);
        }
    }

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
