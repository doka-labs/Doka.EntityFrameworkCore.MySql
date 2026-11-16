namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlConnector3CompatibilityTests
{
    /// <summary>Separates prepared and ordinary binary-GUID commands, including escaped control bytes.</summary>
    public static TheoryData<string, bool, Guid> GuidWireCases
    {
        get
        {
            var cases = new TheoryData<string, bool, Guid>();
            var escapeRich = Guid.Parse("00275c1a-0a0d-1020-3040-5060708090a0");
            foreach (var target in GetSelectedSupportedTargets())
            {
                cases.Add(target.ToString(), false, Guid.Empty);
                cases.Add(target.ToString(), true, Guid.Empty);
                cases.Add(target.ToString(), false, Guid.Parse("00010203-0405-0607-0809-0a0b0c0d0e0f"));
                cases.Add(target.ToString(), true, Guid.Parse("00010203-0405-0607-0809-0a0b0c0d0e0f"));
                cases.Add(target.ToString(), true, escapeRich);

                // WHY: Quote-rich unprepared values have a different legacy
                // syntax failure. Keep the exact 2.x NUL controls above, and
                // require this additional positive path only from the fixed driver.
                if (UsesConnector3)
                {
                    cases.Add(target.ToString(), false, escapeRich);
                }
            }

            return cases;
        }
    }

    /// <summary>Requires the fixed unprepared wire path and retains the exact legacy failure control.</summary>
    /// <param name="target">The selected native database engine.</param>
    /// <param name="prepared">Whether the command uses the binary prepared protocol.</param>
    /// <param name="value">The GUID whose stored bytes must be preserved.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(GuidWireCases))]
    public async Task Binary_guid_wire_behavior_matches_the_qualified_driver(
        string target,
        bool prepared,
        Guid value
    )
    {
        // Arrange
        const string table = "C3GuidWire";
        var cancellationToken = CancellationToken.None;
        var settings = new MySqlConnectionStringBuilder(CreateConnectionString(target))
        {
            NoBackslashEscapes = true,
            IgnorePrepare = false,
        };

        await using var connection = new MySqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await EnableNoBackslashEscapesAsync(connection, cancellationToken);
        await ExecuteAsync(
            connection,
            $"CREATE TEMPORARY TABLE `{table}` (`Id` int NOT NULL PRIMARY KEY, `Token` binary(16) NOT NULL);",
            cancellationToken);

        await using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO `{table}` (`Id`, `Token`) VALUES (1, @value);";
        insert.Parameters.Add(new MySqlParameter
        {
            ParameterName = "@value",
            DbType = DbType.Guid,
            Value = value,
        });

        if (prepared)
        {
            await insert.PrepareAsync(cancellationToken);
        }

        Assert.Equal(prepared, insert.IsPrepared);

        // Act
        var failure = await Record.ExceptionAsync(() => insert.ExecuteNonQueryAsync(cancellationToken));
        var stored = await ReadGuidStoreAsync(connection, table, cancellationToken);

        // Assert
        if (UsesConnector3 || prepared)
        {
            Assert.Null(failure);
            Assert.Equal(1, stored.Count);
            Assert.Equal(Convert.ToHexString(value.ToByteArray(bigEndian: true)), stored.Hex);
        }
        else
        {
            // WHY: Upstream #1692 is characterized precisely on the retained 2.x
            // line. Connector 3 must pass the same case, never an arbitrary exception.
            var native = Assert.IsType<MySqlException>(failure);
            Assert.Equal(1406, native.Number);
            Assert.Contains("Token", native.Message, StringComparison.Ordinal);
            Assert.Equal(0, stored.Count);
            Assert.Null(stored.Hex);
        }
    }

    /// <summary>Exercises the public EF save/shaper path with the corrected unprepared GUID transport.</summary>
    /// <param name="target">The selected native database engine.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(Targets))]
    public async Task Ef_binary_guid_write_and_read_preserve_the_qualified_wire_contract(
        string target
    )
    {
        // Arrange
        const string table = "C3EfGuid";
        var cancellationToken = CancellationToken.None;
        var settings = new MySqlConnectionStringBuilder(CreateConnectionString(target))
        {
            NoBackslashEscapes = true,
        };

        await using var connection = new MySqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await EnableNoBackslashEscapesAsync(connection, cancellationToken);
        await ExecuteAsync(
            connection,
            $"CREATE TEMPORARY TABLE `{table}` (`Id` int NOT NULL PRIMARY KEY, `Token` binary(16) NOT NULL);",
            cancellationToken);

        await using var context = new GuidTransportContext(
            IntegrationTestDbContextOptions.Create<GuidTransportContext>()
                .UseMySql(
                    connection,
                    IntegrationTestEnvironment.GetServerVersion(Enum.Parse<IntegrationDatabaseTarget>(target)))
                .Options);

        context.Add(new GuidTransportRow { Id = 1, Token = Guid.Empty });

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(cancellationToken));
        Guid? readBack = null;
        if (failure is null)
        {
            context.ChangeTracker.Clear();
            readBack = await context.Set<GuidTransportRow>()
                .Select(row => row.Token)
                .SingleAsync(cancellationToken);
        }

        var stored = await ReadGuidStoreAsync(connection, table, cancellationToken);

        // Assert
        if (UsesConnector3)
        {
            Assert.Null(failure);
            Assert.Equal(Guid.Empty, readBack);
            Assert.Equal(1, stored.Count);
            Assert.Equal(new string('0', 32), stored.Hex);
        }
        else
        {
            var update = Assert.IsType<DbUpdateException>(failure);
            var native = Assert.IsType<MySqlException>(update.InnerException);
            Assert.Equal(1406, native.Number);
            Assert.Contains("Token", native.Message, StringComparison.Ordinal);
            Assert.Null(readBack);
            Assert.Equal(0, stored.Count);
            Assert.Null(stored.Hex);
        }
    }

    private sealed class GuidTransportContext(
        DbContextOptions<GuidTransportContext> options
    ) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var row = modelBuilder.Entity<GuidTransportRow>();
            row.ToTable("C3EfGuid");
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.Id).ValueGeneratedNever();
            row.Property(entity => entity.Token).HasColumnType("binary(16)");
        }
    }

    private sealed class GuidTransportRow
    {
        /// <summary>Gets or sets the caller-assigned row identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the binary GUID under the transport contract.</summary>
        public Guid Token { get; set; }
    }
}
