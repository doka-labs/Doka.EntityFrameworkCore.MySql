namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies scalar LIKE translation and execution against every supported engine.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlLikeIntegrationTests
{
    private const string TableName = "IntScalarLikeItems";
    private static readonly Guid s_matchingGuid = new("00112233-4455-6677-8899-aabbccddeeff");

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_preserves_scalar_like_contracts() =>
        AssertScalarLikeContractsAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_preserves_scalar_like_contracts() =>
        AssertScalarLikeContractsAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_preserves_scalar_like_contracts() =>
        AssertScalarLikeContractsAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_preserves_scalar_like_contracts() =>
        AssertScalarLikeContractsAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_preserves_scalar_like_contracts() =>
        AssertScalarLikeContractsAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_preserves_scalar_like_contracts() =>
        AssertScalarLikeContractsAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task AssertScalarLikeContractsAsync(
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<LikeContext>();
        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target));

        await using var context = new LikeContext(builder.Options);
        await context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{TableName}`;");

        try
        {
            await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript());
            context.AddRange(
                new LikeItem
                {
                    Id = 1,
                    ByteValue = 123,
                    SByteValue = -123,
                    Int16Value = -123,
                    UInt16Value = 123,
                    Int32Value = -123,
                    UInt32Value = 123,
                    Int64Value = -123,
                    UInt64Value = 123,
                    SingleValue = -123.5F,
                    DoubleValue = -123.5,
                    DecimalValue = -123.5M,
                    OptionalByteValue = 123,
                    OptionalSByteValue = -123,
                    OptionalInt16Value = -123,
                    OptionalUInt16Value = 123,
                    OptionalInt32Value = -123,
                    OptionalUInt32Value = 123,
                    OptionalInt64Value = -123,
                    OptionalUInt64Value = 123,
                    OptionalSingleValue = -123.5F,
                    OptionalDoubleValue = -123.5,
                    OptionalDecimalValue = -123.5M,
                    CreatedAt = new DateTime(2026, 8, 25, 12, 34, 56, 123),
                    OptionalCreatedAt = new DateTime(2026, 8, 25, 12, 34, 56, 123),
                    BinaryToken = s_matchingGuid,
                    TextToken = s_matchingGuid,
                    OptionalBinaryToken = s_matchingGuid,
                    OptionalTextToken = s_matchingGuid,
                    Text = "sale_25%!",
                    OptionalText = "sale_25%!",
                    Pattern = "00112233-%-aabbccddeeff",
                },
                new LikeItem
                {
                    Id = 2,
                    CreatedAt = new DateTime(2000, 1, 1),
                    Text = "different",
                });

            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            await AssertNumericAsync<byte>(context, nameof(LikeItem.ByteValue));
            await AssertNumericAsync<sbyte>(context, nameof(LikeItem.SByteValue));
            await AssertNumericAsync<short>(context, nameof(LikeItem.Int16Value));
            await AssertNumericAsync<ushort>(context, nameof(LikeItem.UInt16Value));
            await AssertNumericAsync<int>(context, nameof(LikeItem.Int32Value));
            await AssertNumericAsync<uint>(context, nameof(LikeItem.UInt32Value));
            await AssertNumericAsync<long>(context, nameof(LikeItem.Int64Value));
            await AssertNumericAsync<ulong>(context, nameof(LikeItem.UInt64Value));
            await AssertNumericAsync<float>(context, nameof(LikeItem.SingleValue));
            await AssertNumericAsync<double>(context, nameof(LikeItem.DoubleValue));
            await AssertNumericAsync<decimal>(context, nameof(LikeItem.DecimalValue));

            await AssertLikeAsync<DateTime>(context, nameof(LikeItem.CreatedAt), "2026-08-25 12:34:56.123%", [1]);
            await AssertLikeAsync<DateTime?>(context, nameof(LikeItem.OptionalCreatedAt), "2026-08-25%", [1]);
            await AssertLikeAsync<Guid>(context, nameof(LikeItem.BinaryToken), "00112233-%-aabbccddeeff", [1]);
            await AssertLikeAsync<Guid>(context, nameof(LikeItem.TextToken), "00112233-%-aabbccddeeff", [1]);
            await AssertLikeAsync<Guid?>(context, nameof(LikeItem.OptionalBinaryToken), "00112233-%", [1]);
            await AssertLikeAsync<Guid?>(context, nameof(LikeItem.OptionalTextToken), "00112233-%", [1]);
            await AssertLikeAsync<Guid?>(context, nameof(LikeItem.OptionalBinaryToken), "%", [1]);
            await AssertLikeAsync<Guid?>(context, nameof(LikeItem.OptionalTextToken), "%", [1]);
            await AssertLikeAsync<string>(context, nameof(LikeItem.Text), "sale%", [1]);
            await AssertLikeAsync<string?>(context, nameof(LikeItem.OptionalText), "%", [1]);

            var pattern = "00112233-%-aabbccddeeff";
            var formattedGuidMatches = await context
                .Items
                .Where(item => EF.Functions.Like(item.BinaryToken.ToString(), pattern))
                .Select(item => item.Id)
                .ToArrayAsync();
            var scalarGuidMatches = await context
                .Items
                .Where(item => EF.Functions.Like(item.BinaryToken, pattern))
                .Select(item => item.Id)
                .ToArrayAsync();

            Assert.Equal(formattedGuidMatches, scalarGuidMatches);
            Assert.Equal(
                [s_matchingGuid.ToString()],
                await context
                    .Items
                    .Where(item => item.Id == 1)
                    .Select(item => item.BinaryToken.ToString())
                    .ToArrayAsync());

            var token = s_matchingGuid;
            Guid? optionalToken = token;
            var guidParameterMatches = await context
                .Items
                .Where(item => EF.Functions.Like(token, item.Pattern))
                .Select(item => item.Id)
                .ToArrayAsync();
            var nullableGuidParameterMatches = await context
                .Items
                .Where(item => EF.Functions.Like(optionalToken, item.Pattern))
                .Select(item => item.Id)
                .ToArrayAsync();

            Assert.Equal([1], guidParameterMatches);
            Assert.Equal([1], nullableGuidParameterMatches);

            optionalToken = null;
            Assert.Empty(
                await context
                    .Items
                    .Where(item => EF.Functions.Like(optionalToken, item.Pattern))
                    .ToArrayAsync());

            var escapedPattern = "sale!_25!%!!";
            var escapeCharacter = "!";
            var genericStringQuery = context.Items.Where(item =>
                EF.Functions.Like<string>(item.Text, escapedPattern, escapeCharacter));

            var standardStringQuery = context.Items.Where(item =>
                DbFunctionsExtensions.Like(EF.Functions, item.Text, escapedPattern, escapeCharacter));

            Assert.Equal(genericStringQuery.ToQueryString(), standardStringQuery.ToQueryString());
            var genericStringMatches = await genericStringQuery
                .Select(item => item.Id)
                .ToArrayAsync();
            var standardStringMatches = await standardStringQuery
                .Select(item => item.Id)
                .ToArrayAsync();
            var nullableStringMatches = await context
                .Items
                .Where(item => item.OptionalText != null && EF.Functions.Like(item.OptionalText, escapedPattern, "!"))
                .Select(item => item.Id)
                .ToArrayAsync();

            Assert.Equal([1], genericStringMatches);
            Assert.Equal([1], standardStringMatches);
            Assert.Equal([1], nullableStringMatches);

            await AssertEscapedLikeAsync<int?>(context, nameof(LikeItem.OptionalInt32Value), "%23", "!", [1]);
            await AssertEscapedLikeAsync<DateTime?>(context, nameof(LikeItem.OptionalCreatedAt), "2026-08%", "!", [1]);
            await AssertEscapedLikeAsync<Guid?>(context, nameof(LikeItem.OptionalBinaryToken), "00112233-%", "!", [1]);
            await AssertEscapedLikeAsync<Guid?>(context, nameof(LikeItem.OptionalTextToken), "00112233-%", "!", [1]);
            await AssertEscapedLikeAsync<string?>(
                context,
                nameof(LikeItem.OptionalText),
                escapedPattern,
                escapeCharacter,
                [1]);

            await AssertLikeAsync<int?>(context, nameof(LikeItem.OptionalInt32Value), null, []);
            await AssertLikeAsync<DateTime?>(context, nameof(LikeItem.OptionalCreatedAt), null, []);
            await AssertLikeAsync<Guid?>(context, nameof(LikeItem.OptionalBinaryToken), null, []);
            await AssertLikeAsync<Guid?>(context, nameof(LikeItem.OptionalTextToken), null, []);
            await AssertLikeAsync<string?>(context, nameof(LikeItem.OptionalText), null, []);
            await AssertEscapedLikeAsync<int?>(context, nameof(LikeItem.OptionalInt32Value), "%", null, []);
            await AssertEscapedLikeAsync<Guid?>(context, nameof(LikeItem.OptionalBinaryToken), "%", null, []);
            await AssertEscapedLikeAsync<string?>(context, nameof(LikeItem.OptionalText), "%", null, []);

            await AssertLikeAsync<int>(context, nameof(LikeItem.Int32Value), "%' OR 1=1 --", []);
            await AssertLikeAsync<Guid>(context, nameof(LikeItem.BinaryToken), "%' OR 1=1 --", []);
            await AssertLikeAsync<string>(context, nameof(LikeItem.Text), "%' OR 1=1 --", []);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{TableName}`;");
        }
    }

    private static async Task AssertNumericAsync<T>(
        LikeContext context,
        string propertyName
    )
        where T : struct
    {
        await AssertLikeAsync<T>(context, propertyName, "%23%", [1]);
        await AssertLikeAsync<T?>(context, $"Optional{propertyName}", "%23%", [1]);
        await AssertLikeAsync<T?>(context, $"Optional{propertyName}", "%", [1]);
        await AssertLikeAsync<T>(context, propertyName, "absent", []);
    }

    private static async Task AssertLikeAsync<T>(
        LikeContext context,
        string propertyName,
        string? pattern,
        int[] expectedIds
    )
    {
        var query = context
            .Items
            .Where(item => EF.Functions.Like(EF.Property<T>(item, propertyName), pattern))
            .OrderBy(item => item.Id)
            .Select(item => item.Id);
        var sql = query.ToQueryString();

        Assert.InRange(sql.Length, 1, 1024);

        if (pattern is not null)
        {
            Assert.Contains(" LIKE @pattern", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("CAST(", sql, StringComparison.OrdinalIgnoreCase);

            if (propertyName.Contains("BinaryToken", StringComparison.Ordinal))
            {
                Assert.Contains($"HEX(`i`.`{propertyName}`)", sql, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("LOWER(CONCAT(", sql, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.Contains($"`{propertyName}` LIKE @pattern", sql, StringComparison.Ordinal);
                Assert.DoesNotContain("HEX(", sql, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Equal(expectedIds, await query.ToArrayAsync());
    }

    private static async Task AssertEscapedLikeAsync<T>(
        LikeContext context,
        string propertyName,
        string? pattern,
        string? escapeCharacter,
        int[] expectedIds
    )
    {
        var query = context
            .Items
            .Where(item => EF.Functions.Like(EF.Property<T>(item, propertyName), pattern, escapeCharacter))
            .OrderBy(item => item.Id)
            .Select(item => item.Id);

        if (pattern is not null
            && escapeCharacter is not null)
        {
            Assert.Contains("LIKE @pattern", query.ToQueryString(), StringComparison.Ordinal);
            Assert.Contains("ESCAPE @escapeCharacter", query.ToQueryString(), StringComparison.Ordinal);
        }

        Assert.Equal(expectedIds, await query.ToArrayAsync());
    }

    private sealed class LikeContext : DbContext
    {
        public LikeContext(
            DbContextOptions<LikeContext> options
        ) : base(options) { }

        public DbSet<LikeItem> Items => Set<LikeItem>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<LikeItem>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .ValueGeneratedNever();
                entity
                    .Property(item => item.BinaryToken)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity
                    .Property(item => item.OptionalBinaryToken)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity
                    .Property(item => item.TextToken)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity
                    .Property(item => item.OptionalTextToken)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity
                    .Property(item => item.Text)
                    .HasMaxLength(100);
                entity
                    .Property(item => item.OptionalText)
                    .HasMaxLength(100);
                entity
                    .Property(item => item.Pattern)
                    .HasMaxLength(100);
            });
        }
    }

    private sealed class LikeItem
    {
        public int Id { get; set; }
        public byte ByteValue { get; set; }
        public sbyte SByteValue { get; set; }
        public short Int16Value { get; set; }
        public ushort UInt16Value { get; set; }
        public int Int32Value { get; set; }
        public uint UInt32Value { get; set; }
        public long Int64Value { get; set; }
        public ulong UInt64Value { get; set; }
        public float SingleValue { get; set; }
        public double DoubleValue { get; set; }
        public decimal DecimalValue { get; set; }
        public byte? OptionalByteValue { get; set; }
        public sbyte? OptionalSByteValue { get; set; }
        public short? OptionalInt16Value { get; set; }
        public ushort? OptionalUInt16Value { get; set; }
        public int? OptionalInt32Value { get; set; }
        public uint? OptionalUInt32Value { get; set; }
        public long? OptionalInt64Value { get; set; }
        public ulong? OptionalUInt64Value { get; set; }
        public float? OptionalSingleValue { get; set; }
        public double? OptionalDoubleValue { get; set; }
        public decimal? OptionalDecimalValue { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? OptionalCreatedAt { get; set; }
        public Guid BinaryToken { get; set; }
        public Guid TextToken { get; set; }
        public Guid? OptionalBinaryToken { get; set; }
        public Guid? OptionalTextToken { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? OptionalText { get; set; }
        public string Pattern { get; set; } = "not-a-match";
    }
}
