using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using static Doka.EntityFrameworkCore.MySql.FunctionalTests.MySqlGuidFormatTestOptions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Regression coverage for the two Guid column-shape contracts the provider supports:
/// the string-form <c>char(36)</c> / <c>varchar(36)</c> family, where each Guid round-trips
/// as the canonical <c>'00003803-ce08-4029-b639-200564ae1dd2'</c> literal, and the
/// binary-form <c>binary(16)</c> family, where each Guid round-trips as the 16-byte
/// MySQL hex-binary literal <c>X'00003803CE084029B639200564AE1DD2'</c>. The HasData seed
/// path and the explicit per-row INSERT path are both exercised so future drift on either
/// the literal-emission side or the parameter-binding side surfaces here rather than at
/// the spec-suite-only failure surface.
/// </summary>
[Trait("Category", "Live")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class MySqlGuidFormatTests
{
    private static readonly Guid s_seededGuid = Guid.Parse("00003803-ce08-4029-b639-200564ae1dd2");

    private static readonly Guid s_insertedGuid = Guid.Parse("11112222-3333-4444-5555-666677778888");

    private static readonly Guid s_defaultGuid = Guid.Parse("12345678-90ab-cdef-1234-567890abcdef");

    private static readonly Guid s_explicitBinaryGuid = Guid.Parse("fedcba09-8765-4321-fedc-ba0987654321");

    /// <summary>
    /// A context-level Char36 default round-trips unannotated keys and foreign
    /// keys through every supported connection bootstrap. The string path also
    /// proves that migrations and EnsureCreated have the same runtime contract.
    /// </summary>
    [Theory]
    [InlineData("connection-string", false)]
    [InlineData("db-connection", false)]
    [InlineData("data-source", false)]
    [InlineData("connection-string", true)]
    public async Task Default_char36_roundtrips_across_connection_paths_and_schema_creation(
        string bootstrap,
        bool useMigrations
    )
    {
        var dbName = $"doka_guid_default_{Guid.NewGuid():N}";
        var connectionString = CreateDatabaseConnectionString(MySqlTestEnvironment.ConnectionString, dbName);

        try
        {
            await CreateDatabaseAsync(MySqlTestEnvironment.ConnectionString, dbName);

            switch (bootstrap)
            {
                case "connection-string":
                    await AssertDefaultChar36ContractAsync(
                        BuildDefaultChar36Options<DefaultChar36GuidContext>(
                            connectionString,
                            MySqlTestEnvironment.ServerVersion),
                        connectionString,
                        useMigrations);
                    break;
                case "db-connection":
                    await using (var connection = new MySqlConnection(connectionString))
                    {
                        await AssertDefaultChar36ContractAsync(
                            BuildDefaultChar36Options<DefaultChar36GuidContext>(
                                connection,
                                MySqlTestEnvironment.ServerVersion),
                            connectionString,
                            useMigrations);
                    }

                    break;
                case "data-source":
                    await using (var dataSource = new MySqlDataSourceBuilder(connectionString).Build())
                    {
                        await AssertDefaultChar36ContractAsync(
                            BuildDefaultChar36Options<DefaultChar36GuidContext>(
                                dataSource,
                                MySqlTestEnvironment.ServerVersion),
                            connectionString,
                            useMigrations);
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(bootstrap), bootstrap, "Unknown Guid bootstrap path.");
            }
        }
        finally
        {
            await DropDatabaseAsync(MySqlTestEnvironment.ConnectionString, dbName);
        }
    }

    /// <summary>
    /// Verifies that a Guid property mapped to <c>char(36)</c> stores and reads back the
    /// canonical string-form literal exactly, with the original case-folded
    /// <c>'00003803-ce08-4029-b639-200564ae1dd2'</c> representation preserved verbatim
    /// at the storage layer.
    /// </summary>
    [Fact]
    public async Task Char36_guid_roundtrips_as_canonical_string_literal()
    {
        var dbName = $"doka_guid_char36_{Guid.NewGuid():N}";
        var connectionString = MySqlTestEnvironment.ConnectionString;

        try
        {
            await using (var seedContext =
                         new Char36GuidContext(
                             BuildOptions<Char36GuidContext>(
                                 connectionString,
                                 dbName,
                                 MySqlTestEnvironment.ServerVersion)))
            {
                await seedContext.Database.EnsureCreatedAsync();
            }

            await using (var insertContext =
                         new Char36GuidContext(
                             BuildOptions<Char36GuidContext>(
                                 connectionString,
                                 dbName,
                                 MySqlTestEnvironment.ServerVersion)))
            {
                insertContext.Entities.Add(
                    new Char36Entity
                    {
                        Id = s_insertedGuid,
                        Name = "inserted"
                    });
                await insertContext.SaveChangesAsync();
            }

            await using (var readContext =
                         new Char36GuidContext(
                             BuildOptions<Char36GuidContext>(
                                 connectionString,
                                 dbName,
                                 MySqlTestEnvironment.ServerVersion)))
            {
                var seeded = await readContext.Entities.SingleAsync(e => e.Id == s_seededGuid);
                var inserted = await readContext.Entities.SingleAsync(e => e.Id == s_insertedGuid);

                Assert.Equal("seeded", seeded.Name);
                Assert.Equal("inserted", inserted.Name);

                var storageShape = await ReadRawIdStringsAsync(connectionString, dbName, "Char36Entities");
                Assert.Contains("00003803-ce08-4029-b639-200564ae1dd2", storageShape, StringComparer.Ordinal);
                Assert.Contains("11112222-3333-4444-5555-666677778888", storageShape, StringComparer.Ordinal);
            }
        }
        finally
        {
            await DropDatabaseAsync(connectionString, dbName);
        }
    }

    /// <summary>
    /// Verifies that a Guid property mapped to <c>binary(16)</c> stores the value as the
    /// 16-byte binary form (MySQL hex literal <c>X'HEX16'</c>) for HasData-seeded rows
    /// and reads back as the equivalent <see cref="Guid"/> instance for both seeded and
    /// runtime-inserted rows.
    /// </summary>
    [Fact]
    public async Task Binary16_guid_roundtrips_as_16_byte_hex_literal()
    {
        var dbName = $"doka_guid_binary16_{Guid.NewGuid():N}";
        var connectionString = MySqlTestEnvironment.ConnectionString;

        try
        {
            await using (var seedContext =
                         new Binary16GuidContext(
                             BuildOptions<Binary16GuidContext>(
                                 connectionString,
                                 dbName,
                                 MySqlTestEnvironment.ServerVersion)))
            {
                await seedContext.Database.EnsureCreatedAsync();
            }

            await using (var insertContext =
                         new Binary16GuidContext(
                             BuildOptions<Binary16GuidContext>(
                                 connectionString,
                                 dbName,
                                 MySqlTestEnvironment.ServerVersion)))
            {
                insertContext.Entities.Add(
                    new Binary16Entity
                    {
                        Id = s_insertedGuid,
                        Name = "inserted"
                    });
                await insertContext.SaveChangesAsync();
            }

            await using (var readContext =
                         new Binary16GuidContext(
                             BuildOptions<Binary16GuidContext>(
                                 connectionString,
                                 dbName,
                                 MySqlTestEnvironment.ServerVersion)))
            {
                var seeded = await readContext.Entities.SingleAsync(e => e.Id == s_seededGuid);
                var inserted = await readContext.Entities.SingleAsync(e => e.Id == s_insertedGuid);

                Assert.Equal("seeded", seeded.Name);
                Assert.Equal("inserted", inserted.Name);

                var rawHex = await ReadRawIdHexAsync(connectionString, dbName, "Binary16Entities");
                Assert.Contains("00003803CE084029B639200564AE1DD2", rawHex, StringComparer.OrdinalIgnoreCase);
                Assert.Contains("11112222333344445555666677778888", rawHex, StringComparer.OrdinalIgnoreCase);
            }
        }
        finally
        {
            await DropDatabaseAsync(connectionString, dbName);
        }
    }

    private static async Task AssertDefaultChar36ContractAsync(
        DbContextOptions<DefaultChar36GuidContext> options,
        string connectionString,
        bool useMigrations
    )
    {
        if (useMigrations)
        {
            await ApplyInitialMigrationAsync(connectionString);
        }
        else
        {
            await using var createContext = new DefaultChar36GuidContext(options);
            _ = await createContext.Database.EnsureCreatedAsync();
        }

        await using (var insertContext = new DefaultChar36GuidContext(options))
        {
            insertContext.Principals.Add(
                new DefaultChar36Principal
                {
                    Id = s_defaultGuid,
                    Name = "principal",
                    Dependents =
                    {
                        new DefaultChar36Dependent
                        {
                            Name = "dependent",
                        },
                    },
                });

            insertContext.BinaryEntities.Add(
                new ExplicitBinary16UnderChar36
                {
                    Id = s_explicitBinaryGuid,
                    Name = "binary",
                });

            await insertContext.SaveChangesAsync();
        }

        await using (var readContext = new DefaultChar36GuidContext(options))
        {
            var principal = await readContext
                .Principals
                .Include(item => item.Dependents)
                .SingleAsync(item => item.Id == s_defaultGuid);
            var dependent = Assert.Single(principal.Dependents);
            var binary = await readContext
                .BinaryEntities
                .AsNoTracking()
                .SingleAsync(item => item.Id == s_explicitBinaryGuid);

            Assert.Equal(s_defaultGuid, dependent.PrincipalId);
            Assert.Same(principal, dependent.Principal);
            Assert.Equal(s_explicitBinaryGuid, binary.Id);
        }

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(`Id` AS CHAR(36)) FROM `DefaultChar36Principals`;";
        Assert.Equal(
            s_defaultGuid.ToString("D", CultureInfo.InvariantCulture),
            Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));

        command.CommandText = "SELECT CAST(`PrincipalId` AS CHAR(36)) FROM `DefaultChar36Dependents`;";
        Assert.Equal(
            s_defaultGuid.ToString("D", CultureInfo.InvariantCulture),
            Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));

        command.CommandText = "SELECT HEX(`Id`) FROM `ExplicitBinary16UnderChar36`;";
        Assert.Equal(
            "FEDCBA0987654321FEDCBA0987654321",
            Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    private static async Task ApplyInitialMigrationAsync(
        string connectionString
    )
    {
        await using var source = new EmptyDefaultChar36GuidContext(
            BuildDefaultChar36Options<EmptyDefaultChar36GuidContext>(
                connectionString,
                MySqlTestEnvironment.ServerVersion));

        await using var target = new DefaultChar36GuidContext(
            BuildDefaultChar36Options<DefaultChar36GuidContext>(
                connectionString,
                MySqlTestEnvironment.ServerVersion));

        var operations = target
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                source
                    .GetService<IDesignTimeModel>()
                    .Model
                    .GetRelationalModel(),
                target
                    .GetService<IDesignTimeModel>()
                    .Model
                    .GetRelationalModel());

        var commands = target
            .GetService<IMigrationsSqlGenerator>()
            .Generate(
                operations,
                target.GetService<IDesignTimeModel>()
                    .Model);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var migrationCommand in commands)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migrationCommand.CommandText;
            _ = await command.ExecuteNonQueryAsync();
        }
    }

    private static string CreateDatabaseConnectionString(
        string baseConnectionString,
        string databaseName
    ) => new MySqlConnectionStringBuilder(baseConnectionString)
    {
        Database = databaseName,
    }.ConnectionString;

    private static async Task CreateDatabaseAsync(
        string baseConnectionString,
        string databaseName
    )
    {
        var builder = new MySqlConnectionStringBuilder(baseConnectionString);
        builder.Remove("Database");

        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE `{databaseName}`;";
        _ = await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> ReadRawIdStringsAsync(
        string baseConnectionString,
        string databaseName,
        string tableName
    )
    {
        var builder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
            GuidFormat = MySqlConnector.MySqlGuidFormat.None,
        };

        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT `Id` FROM `{tableName}`;";

        var results = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    private static async Task<List<string>> ReadRawIdHexAsync(
        string baseConnectionString,
        string databaseName,
        string tableName
    )
    {
        var builder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
        };

        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT HEX(`Id`) FROM `{tableName}`;";

        var results = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    private static async Task DropDatabaseAsync(
        string baseConnectionString,
        string databaseName
    )
    {
        var builder = new MySqlConnectionStringBuilder(baseConnectionString);
        builder.Remove("Database");

        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`;";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class Char36Entity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class Binary16Entity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class DefaultChar36Principal
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public ICollection<DefaultChar36Dependent> Dependents { get; } = [];
    }

    private sealed class DefaultChar36Dependent
    {
        public int Id { get; set; }

        public Guid PrincipalId { get; set; }

        public string Name { get; set; } = string.Empty;

        public DefaultChar36Principal Principal { get; set; } = null!;
    }

    private sealed class ExplicitBinary16UnderChar36
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class EmptyDefaultChar36GuidContext : DbContext
    {
        public EmptyDefaultChar36GuidContext(
            DbContextOptions<EmptyDefaultChar36GuidContext> options
        ) : base(options) { }
    }

    private sealed class DefaultChar36GuidContext : DbContext
    {
        public DefaultChar36GuidContext(
            DbContextOptions<DefaultChar36GuidContext> options
        ) : base(options) { }

        public DbSet<DefaultChar36Principal> Principals => Set<DefaultChar36Principal>();

        public DbSet<ExplicitBinary16UnderChar36> BinaryEntities => Set<ExplicitBinary16UnderChar36>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<DefaultChar36Principal>(entity =>
            {
                entity.ToTable("DefaultChar36Principals");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Name).HasMaxLength(80);
            });

            modelBuilder.Entity<DefaultChar36Dependent>(entity =>
            {
                entity.ToTable("DefaultChar36Dependents");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Name).HasMaxLength(80);
                entity
                    .HasOne(item => item.Principal)
                    .WithMany(item => item.Dependents)
                    .HasForeignKey(item => item.PrincipalId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ExplicitBinary16UnderChar36>(entity =>
            {
                entity.ToTable("ExplicitBinary16UnderChar36");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity.Property(item => item.Name).HasMaxLength(80);
            });
        }
    }

    private sealed class Char36GuidContext : DbContext
    {
        public Char36GuidContext(
            DbContextOptions<Char36GuidContext> options
        ) : base(options) { }

        public DbSet<Char36Entity> Entities => Set<Char36Entity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<Char36Entity>(entity =>
            {
                entity.ToTable("Char36Entities");
                entity.HasKey(e => e.Id);
                entity
                    .Property(e => e.Id)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity
                    .Property(e => e.Name)
                    .IsRequired();
                entity.HasData(
                    new Char36Entity
                    {
                        Id = s_seededGuid,
                        Name = "seeded",
                    });
            });
        }
    }

    private sealed class Binary16GuidContext : DbContext
    {
        public Binary16GuidContext(
            DbContextOptions<Binary16GuidContext> options
        ) : base(options) { }

        public DbSet<Binary16Entity> Entities => Set<Binary16Entity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<Binary16Entity>(entity =>
            {
                entity.ToTable("Binary16Entities");
                entity.HasKey(e => e.Id);
                entity
                    .Property(e => e.Id)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity
                    .Property(e => e.Name)
                    .IsRequired();
                entity.HasData(
                    new Binary16Entity
                    {
                        Id = s_seededGuid,
                        Name = "seeded",
                    });
            });
        }
    }
}
