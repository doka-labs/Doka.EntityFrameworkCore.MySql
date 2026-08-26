namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies GUID formatting, matching, and generated values against the stored
/// representation on every supported database engine.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlGuidTextIntegrationTests
{
    private const string TableName = "IntGuidTextItems";
    private const string MatchingText = "00112233-4455-6677-8899-aabbccddeeff";
    private static readonly Guid s_matchingGuid = new(MatchingText);

    private static readonly string[] s_properties =
    [
        "DefaultToken",
        "BinaryToken",
        "TextToken",
        "BinaryColumnToken",
        "CharColumnToken",
        "VarCharColumnToken",
        "ConvertedTextToken",
    ];

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_preserves_guid_text_mapping_contracts() =>
        AssertGuidTextContractsAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_preserves_guid_text_mapping_contracts() =>
        AssertGuidTextContractsAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_preserves_guid_text_mapping_contracts() =>
        AssertGuidTextContractsAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_preserves_guid_text_mapping_contracts() =>
        AssertGuidTextContractsAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_preserves_guid_text_mapping_contracts() =>
        AssertGuidTextContractsAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_preserves_guid_text_mapping_contracts() =>
        AssertGuidTextContractsAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task AssertGuidTextContractsAsync(
        IntegrationDatabaseTarget target
    )
    {
        foreach (var defaultFormat in new[] { MySqlGuidFormat.Binary16, MySqlGuidFormat.Char36 })
        {
            var builder = IntegrationTestDbContextOptions.Create<GuidContext>();
            builder
                .EnableServiceProviderCaching(false)
                .UseMySql(
                    IntegrationTestEnvironment.GetConnectionString(target),
                    IntegrationTestEnvironment.GetServerVersion(target),
                    options => options.DefaultGuidFormat(defaultFormat));

            await using var context = new GuidContext(builder.Options);
            await DropTableAsync(context);

            try
            {
                await context.Database
                    .ExecuteSqlRawAsync(
                        context.Database
                            .GenerateCreateScript(),
                        CancellationToken.None);

                foreach (var id in new[] { 1, 2 })
                {
                    var entry = context.Add(new GuidItem
                    {
                        Id = id,
                        Pattern = "00112233-%-aabbccddeeff",
                    });

                    foreach (var propertyName in s_properties)
                    {
                        entry.Property(propertyName).CurrentValue = id == 1 ? s_matchingGuid : Guid.Empty;
                        entry.Property($"Optional{propertyName}").CurrentValue = id == 1 ? s_matchingGuid : null;
                    }
                }

                await context.SaveChangesAsync(CancellationToken.None);
                context.ChangeTracker
                    .Clear();

                foreach (var propertyName in s_properties)
                {
                    await AssertPropertyAsync(context, propertyName);
                }

                await AssertUnboundValuesAsync(context);
                await AssertStoredTextCollationAsync(context);
            }
            finally
            {
                await DropTableAsync(context);
            }
        }
    }

    private static async Task AssertPropertyAsync(
        GuidContext context,
        string propertyName
    )
    {
        var formattedValues = await context
            .Set<GuidItem>()
            .OrderBy(item => item.Id)
            .Select(item => EF
                .Property<Guid>(item, propertyName)
                .ToString())
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal([MatchingText, Guid.Empty.ToString()], formattedValues);

        var matchingIds = await context
            .Set<GuidItem>()
            .Where(item => EF
                .Property<Guid>(item, propertyName)
                .ToString() == MatchingText)
            .Select(item => item.Id)
            .ToArrayAsync(CancellationToken.None);

        var nonMatchingIds = await context
            .Set<GuidItem>()
            .Where(item => EF
                .Property<Guid>(item, propertyName)
                .ToString() != MatchingText)
            .Select(item => item.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal([1], matchingIds);
        Assert.Equal([2], nonMatchingIds);

        await AssertLikeAsync<Guid>(context, propertyName, "00112233-%-aabbccddeeff", [1]);
        await AssertLikeAsync<Guid>(context, propertyName, "ffffffff-%", []);

        var optionalName = $"Optional{propertyName}";
        await AssertLikeAsync<Guid?>(context, optionalName, "00112233-%-aabbccddeeff", [1]);
        await AssertLikeAsync<Guid?>(context, optionalName, "%", [1]);
        await AssertLikeAsync<Guid?>(context, optionalName, "ffffffff-%", []);
        await AssertLikeAsync<Guid?>(context, optionalName, null, []);

        var escapedIds = await context
            .Set<GuidItem>()
            .Where(item => EF.Functions.Like(EF.Property<Guid?>(item, optionalName), "00112233-%", "!"))
            .Select(item => item.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal([1], escapedIds);
    }

    private static async Task AssertUnboundValuesAsync(
        GuidContext context
    )
    {
        var token = s_matchingGuid;
        Guid? optionalToken = token;
        var matchingIds = await context
            .Set<GuidItem>()
            .Where(item => EF.Functions.Like(token, item.Pattern)
                && EF.Functions.Like(optionalToken, item.Pattern))
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal([1, 2], matchingIds);

        optionalToken = null;

        Assert.Empty(await context
            .Set<GuidItem>()
            .Where(item => EF.Functions.Like(optionalToken, item.Pattern))
            .Select(item => item.Id)
            .ToArrayAsync(CancellationToken.None));

        var constantMatches = await context
            .Set<GuidItem>()
            .Where(item => EF.Functions.Like(new Guid(MatchingText), item.Pattern))
            .Select(item => item.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal([1, 2], constantMatches.Order());

        var generatedValues = await context
            .Set<GuidItem>()
            .Select(item => Guid.NewGuid())
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(2, generatedValues.Length);
        Assert.All(generatedValues, value => Assert.NotEqual(Guid.Empty, value));
        Assert.NotEqual(generatedValues[0], generatedValues[1]);

        var generatedText = await context
            .Set<GuidItem>()
            .Select(item => Guid
                .NewGuid()
                .ToString())
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(2, generatedText.Length);
        Assert.All(generatedText, value =>
        {
            Assert.True(Guid.TryParseExact(value, "D", out var guid));
            Assert.NotEqual(Guid.Empty, guid);
            Assert.Equal(guid.ToString(), value);
        });
    }

    private static async Task AssertStoredTextCollationAsync(
        GuidContext context
    )
    {
        await context.Database
            .ExecuteSqlRawAsync(
                $"UPDATE `{TableName}` SET `CharColumnToken` = UPPER(`CharColumnToken`) WHERE `Id` = 1;",
                CancellationToken.None);

        var text = await context
            .Set<GuidItem>()
            .Where(item => item.Id == 1)
            .Select(item => EF
                .Property<Guid>(item, "CharColumnToken")
                .ToString())
            .SingleAsync(CancellationToken.None);

        Assert.Equal(MatchingText, text);
        await AssertLikeAsync<Guid>(context, "CharColumnToken", "%AABBCCDDEEFF", [1]);
        await AssertLikeAsync<Guid>(context, "CharColumnToken", "%aabbccddeeff", []);
    }

    private static async Task AssertLikeAsync<T>(
        GuidContext context,
        string propertyName,
        string? pattern,
        int[] expected
    )
    {
        var actual = await context
            .Set<GuidItem>()
            .Where(item => EF.Functions.Like(EF.Property<T>(item, propertyName), pattern))
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(expected, actual);
    }

    private static Task<int> DropTableAsync(
        GuidContext context
    ) => context.Database
        .ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{TableName}`;", CancellationToken.None);

    private sealed class GuidContext : DbContext
    {
        public GuidContext(
            DbContextOptions<GuidContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("utf8mb4");
            var entity = modelBuilder.Entity<GuidItem>();
            entity.ToTable(TableName);

            foreach (var prefix in new[] { string.Empty, "Optional" })
            {
                var type = prefix.Length == 0 ? typeof(Guid) : typeof(Guid?);

                entity.Property(type, $"{prefix}DefaultToken");
                entity
                    .Property(type, $"{prefix}BinaryToken")
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);

                entity
                    .Property(type, $"{prefix}TextToken")
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);

                entity
                    .Property(type, $"{prefix}BinaryColumnToken")
                    .HasColumnType("binary(16)");

                entity
                    .Property(type, $"{prefix}CharColumnToken")
                    .HasColumnType("char(36)")
                    .UseCollation("utf8mb4_bin");

                entity
                    .Property(type, $"{prefix}VarCharColumnToken")
                    .HasColumnType("varchar(36)");

                entity
                    .Property(type, $"{prefix}ConvertedTextToken")
                    .HasConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.GuidToStringConverter>();
            }
        }
    }

    private sealed class GuidItem
    {
        public int Id { get; set; }
        public string Pattern { get; set; } = string.Empty;
    }
}
