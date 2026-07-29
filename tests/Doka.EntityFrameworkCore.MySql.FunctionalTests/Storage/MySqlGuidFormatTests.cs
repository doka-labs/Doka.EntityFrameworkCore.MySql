using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

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
                         new Char36GuidContext(BuildOptions<Char36GuidContext>(connectionString, dbName)))
            {
                await seedContext.Database.EnsureCreatedAsync();
            }

            await using (var insertContext =
                         new Char36GuidContext(BuildOptions<Char36GuidContext>(connectionString, dbName)))
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
                         new Char36GuidContext(BuildOptions<Char36GuidContext>(connectionString, dbName)))
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
                         new Binary16GuidContext(BuildOptions<Binary16GuidContext>(connectionString, dbName)))
            {
                await seedContext.Database.EnsureCreatedAsync();
            }

            await using (var insertContext =
                         new Binary16GuidContext(BuildOptions<Binary16GuidContext>(connectionString, dbName)))
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
                         new Binary16GuidContext(BuildOptions<Binary16GuidContext>(connectionString, dbName)))
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

    private static DbContextOptions<TContext> BuildOptions<TContext>(
        string baseConnectionString,
        string databaseName
    )
        where TContext : DbContext
    {
        var builder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
        };

        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        optionsBuilder.UseMySql(builder.ConnectionString, MySqlTestEnvironment.ServerVersion);
        return optionsBuilder.Options;
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
