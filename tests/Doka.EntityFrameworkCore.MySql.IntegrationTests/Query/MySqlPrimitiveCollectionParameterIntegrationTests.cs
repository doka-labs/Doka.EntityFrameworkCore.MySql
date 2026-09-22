namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies parameterized primitive collections against the stored provider
/// representation on every supported database engine.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlPrimitiveCollectionParameterIntegrationTests
{
    private const string TableName = "IntPrimitiveCollectionItems";
    private const string CharTableName = "IntCharGuidCollectionItems";
    private const string SqlModeTableName = "IntSqlModeStringCollectionItems";
    private const int RowCount = 2200;

    /// <summary>
    /// Verifies collection parameter semantics on MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_preserves_primitive_collection_parameter_contracts() =>
        AssertPrimitiveCollectionParameterContractsAsync(IntegrationDatabaseTarget.MySql84);

    /// <summary>
    /// Verifies collection parameter semantics on MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_preserves_primitive_collection_parameter_contracts() =>
        AssertPrimitiveCollectionParameterContractsAsync(IntegrationDatabaseTarget.MySql97);

    /// <summary>
    /// Verifies collection parameter semantics on MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_preserves_primitive_collection_parameter_contracts() =>
        AssertPrimitiveCollectionParameterContractsAsync(IntegrationDatabaseTarget.MariaDb1011);

    /// <summary>
    /// Verifies collection parameter semantics on MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_preserves_primitive_collection_parameter_contracts() =>
        AssertPrimitiveCollectionParameterContractsAsync(IntegrationDatabaseTarget.MariaDb114);

    /// <summary>
    /// Verifies collection parameter semantics on MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_preserves_primitive_collection_parameter_contracts() =>
        AssertPrimitiveCollectionParameterContractsAsync(IntegrationDatabaseTarget.MariaDb118);

    /// <summary>
    /// Verifies collection parameter semantics on MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_preserves_primitive_collection_parameter_contracts() =>
        AssertPrimitiveCollectionParameterContractsAsync(IntegrationDatabaseTarget.MariaDb123);

    /// <summary>
    /// Verifies JSON-like string values when MariaDB disables SQL-literal
    /// backslash escapes.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_preserves_string_collections_with_no_backslash_escapes()
    {
        var builder = IntegrationTestDbContextOptions.Create<SqlModeStringCollectionContext>();
        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118),
            IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb118));
        await using var context = new SqlModeStringCollectionContext(builder.Options);
        await DropSqlModeTableAsync(context);

        string? previousSqlMode = null;

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None);
            context.AddRange(
                new SqlModeStringCollectionEntity
                {
                    Id = 1,
                    Value = "\"quoted\"",
                    OptionalValue = null,
                    ConvertedValue = "alpha",
                    ConvertedText = new TextValue("\"quoted\""),
                    Values = ["\"quoted\"", "\"\\q\"", string.Empty, "gr\u00FCn", null],
                },
                new SqlModeStringCollectionEntity
                {
                    Id = 2,
                    Value = "\"\\q\"",
                    OptionalValue = "\"quoted\"",
                    ConvertedValue = "beta",
                    ConvertedText = new TextValue("\"\\q\""),
                },
                new SqlModeStringCollectionEntity
                {
                    Id = 3,
                    Value = string.Empty,
                    OptionalValue = "other",
                    ConvertedValue = "gamma",
                    ConvertedText = new TextValue(string.Empty),
                },
                new SqlModeStringCollectionEntity
                {
                    Id = 4,
                    Value = "gr\u00FCn",
                    OptionalValue = "other",
                    ConvertedValue = "delta",
                    ConvertedText = new TextValue("gr\u00FCn"),
                });
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            await context.Database.OpenConnectionAsync(CancellationToken.None);

            var connection = context.Database.GetDbConnection();
            previousSqlMode = await ReadSqlModeAsync(connection);
            await SetSqlModeAsync(connection, "NO_BACKSLASH_ESCAPES");
            string[] values = ["\"quoted\"", "\"\\q\"", string.Empty, "gr\u00FCn"];
            string?[] nullableValues = ["\"quoted\"", null];
            string[] convertedValues = ["alpha"];
            TextValue[] convertedTextValues = [new("\"\\q\""), new("gr\u00FCn")];
            string?[] expectedModelValues = ["\"quoted\"", "\"\\q\"", string.Empty, "gr\u00FCn", null];

            var parameterIds = await context.Items
                .Where(item => values.Contains(item.Value))
                .OrderBy(item => item.Id)
                .Select(item => item.Id)
                .ToArrayAsync(CancellationToken.None);
            var nullableIds = await context.Items
                .Where(item => EF.Parameter(nullableValues).Contains(item.OptionalValue))
                .OrderBy(item => item.Id)
                .Select(item => item.Id)
                .ToArrayAsync(CancellationToken.None);
            var convertedIds = await context.Items
                .Where(item => EF.Parameter(convertedValues).Contains(item.ConvertedValue))
                .Select(item => item.Id)
                .ToArrayAsync(CancellationToken.None);
            var convertedTextIds = await context.Items
                .Where(item => EF.Parameter(convertedTextValues).Contains(item.ConvertedText))
                .OrderBy(item => item.Id)
                .Select(item => item.Id)
                .ToArrayAsync(CancellationToken.None);
            var modelValues = await context.Items
                .Where(item => item.Id == 1)
                .SelectMany(item => item.Values)
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal([1, 2, 3, 4], parameterIds);
            Assert.Equal([1, 2], nullableIds);
            Assert.Equal([1], convertedIds);
            Assert.Equal([2, 4], convertedTextIds);
            Assert.Equal(expectedModelValues, modelValues);
        }
        finally
        {
            if (previousSqlMode is not null)
            {
                await SetSqlModeAsync(context.Database.GetDbConnection(), previousSqlMode);
            }

            await context.Database.CloseConnectionAsync();
            await DropSqlModeTableAsync(context);
        }
    }

    private static async Task AssertPrimitiveCollectionParameterContractsAsync(
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<PrimitiveCollectionContext>();
        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target),
            options => options.DefaultGuidFormat(MySqlGuidFormat.Binary16));
        await using var context = new PrimitiveCollectionContext(builder.Options);

        var charBuilder = IntegrationTestDbContextOptions.Create<CharGuidCollectionContext>();
        charBuilder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target),
            options => options.DefaultGuidFormat(MySqlGuidFormat.Char36));
        await using var charContext = new CharGuidCollectionContext(charBuilder.Options);

        await DropTableAsync(context);
        await DropCharTableAsync(charContext);

        try
        {
            var entities = Enumerable
                .Range(0, RowCount)
                .Select(index => new PrimitiveCollectionEntity
                {
                    Id = index + 1,
                    BinaryId = CreateGuid(index),
                    OptionalBinaryId = index == 1 ? null : CreateGuid(index),
                    OptionalCharId = index == 1 ? null : CreateGuid(index),
                    CharId = CreateGuid(index),
                    Name = index switch
                    {
                        0 => "alpha",
                        1 => "\"quoted\"",
                        2 => "\"\\q\"",
                        _ => $"value-{index}",
                    },
                    OptionalName = index == 1 ? null : index == 0 ? "alpha" : $"optional-{index}",
                    ConvertedName = index switch
                    {
                        0 => "alpha",
                        1 => "beta",
                        _ => $"converted-{index}",
                    },
                    CaseInsensitiveName = index == 0 ? "alpha" : $"case-{index}",
                    ConvertedText = new TextValue(index switch
                    {
                        0 => "\"\\q\"",
                        1 => "gr\u00FCn",
                        _ => $"text-{index}",
                    }),
                    Status = index == 0 ? CollectionStatus.Active : CollectionStatus.Inactive,
                    OptionalStatus = index == 1 ? null : index == 0
                        ? CollectionStatus.Active
                        : CollectionStatus.Inactive,
                    ConvertedPrice = index == 0 ? new PriceValue(3.1m) : new PriceValue(index),
                    ConvertedGuid = new StrongGuid(CreateGuid(index)),
                    ConvertedDuration = index == 0
                        ? new ElapsedValue(TimeSpan.FromHours(27))
                        : new ElapsedValue(TimeSpan.FromHours(index % 24)),
                    ConvertedTimestamp = new DateTimeOffset(
                        2026,
                        9,
                        19,
                        10,
                        0,
                        0,
                        TimeSpan.Zero).AddSeconds(index),
                    ConvertedPayload = new BinaryValue($"payload-{index}"),
                    Payload = BitConverter.GetBytes(index),
                    GuidValues = [CreateGuid(index), Guid.Empty],
                })
                .ToArray();

            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None);
            context.AddRange(entities);
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();

            await charContext.Database.ExecuteSqlRawAsync(
                charContext.Database.GenerateCreateScript(),
                CancellationToken.None);
            charContext.Add(
                new CharGuidCollectionEntity
                {
                    Id = 1,
                    GuidValues = [CreateGuid(10), Guid.Empty],
                });
            await charContext.SaveChangesAsync(CancellationToken.None);
            charContext.ChangeTracker.Clear();

            var oneGuid = new[] { CreateGuid(0) };
            var threeGuids = Enumerable.Range(0, 3).Select(CreateGuid).ToArray();
            var allGuids = Enumerable.Range(0, RowCount).Select(CreateGuid).ToArray();
            var charGuids = new[] { CreateGuid(0), CreateGuid(2) };
            var emptyGuids = Array.Empty<Guid>();
            var duplicateAndAbsentGuids = new[] { CreateGuid(0), CreateGuid(0), CreateGuid(RowCount + 1) };
            Guid?[] nullableGuids = [CreateGuid(0), null];
            var defaultContainsGuids = new[] { CreateGuid(1), CreateGuid(2) };
            var integerKeys = new[] { 1, RowCount };
            string[] names = ["alpha", "\"quoted\"", "\"\\q\""];
            string?[] nullableNames = ["alpha", null];
            string[] convertedNames = ["alpha"];
            string[] differentlyCasedConvertedName = ["Alpha"];
            string[] differentlyCasedNames = ["Alpha"];
            TextValue[] convertedTexts = [new("\"\\q\"")];
            CollectionStatus[] statuses = [CollectionStatus.Active];
            CollectionStatus[] absentStatuses = [(CollectionStatus)42];
            CollectionStatus?[] nullableStatuses = [CollectionStatus.Active, null];
            PriceValue[] exactConvertedPrices = [new(3.1m)];
            PriceValue[] roundedConvertedPrices = [new(3.14159m)];
            StrongGuid[] convertedGuids = [new(CreateGuid(0))];
            StrongGuid[] absentConvertedGuids = [new(CreateGuid(RowCount + 1))];
            ElapsedValue[] convertedDurations = [new(TimeSpan.FromHours(27))];
            ElapsedValue[] truncatedConvertedDurations =
            [
                new(TimeSpan.FromHours(27).Add(TimeSpan.FromMilliseconds(500))),
            ];
            ElapsedValue[] positiveOutOfRangeConvertedDurations = [new(TimeSpan.FromHours(839))];
            ElapsedValue[] negativeOutOfRangeConvertedDurations = [new(TimeSpan.FromHours(-839))];
            DateTimeOffset[] convertedTimestamps =
            [
                new(2026, 9, 19, 10, 0, 0, TimeSpan.Zero),
            ];
            DateTimeOffset[] truncatedConvertedTimestamps =
            [
                new DateTimeOffset(2026, 9, 19, 10, 0, 0, TimeSpan.Zero)
                    .AddMilliseconds(500),
            ];
            BinaryValue[] convertedPayloads = [new("payload-0")];
            BinaryValue[] absentConvertedPayloads = [new("missing")];
            var differentlyCasedName = new[] { "Alpha" };
            byte[][] payloads = [BitConverter.GetBytes(0), BitConverter.GetBytes(2)];
            var stringDecodeCoercibility = await ReadStringDecodeCoercibilityAsync(context);

            var results = new PrimitiveCollectionResults(
                await SelectIdsAsync(context.Items.Where(item => EF.Parameter(oneGuid).Contains(item.BinaryId))),
                await SelectIdsAsync(context.Items.Where(item => EF.Parameter(threeGuids).Contains(item.BinaryId))),
                await SelectIdsAsync(context.Items.Where(item => allGuids.Contains(item.BinaryId))),
                await SelectIdsAsync(context.Items.Where(item => charGuids.Contains(item.CharId))),
                await SelectIdsAsync(context.Items.Where(item => emptyGuids.Contains(item.BinaryId))),
                await SelectIdsAsync(
                    context.Items.Where(item => duplicateAndAbsentGuids.Contains(item.BinaryId))),
                await SelectIdsAsync(
                    context.Items.Where(item => EF.Parameter(nullableGuids).Contains(item.OptionalBinaryId))),
                await SelectIdsAsync(
                    context.Items.Where(item => EF.Parameter(nullableGuids).Contains(item.OptionalCharId))),
                await SelectIdsAsync(context.Items.Where(item => defaultContainsGuids.Contains(item.BinaryId))),
                await SelectIdsAsync(context.Items.Where(item => item.BinaryId == CreateGuid(0))),
                await SelectIdsAsync(context.Items.Where(item => EF.Parameter(integerKeys).Contains(item.Id))),
                await SelectIdsAsync(context.Items.Where(item => EF.Parameter(names).Contains(item.Name))),
                await SelectIdsAsync(
                    context.Items.Where(item => EF.Parameter(nullableNames).Contains(item.OptionalName))),
                await SelectIdsAsync(
                    context.Items.Where(item => EF.Parameter(convertedNames).Contains(item.ConvertedName))),
                await SelectIdsAsync(
                    context.Items.Where(
                        item => EF.Parameter(differentlyCasedConvertedName).Contains(item.ConvertedName))),
                await SelectIdsAsync(
                    context.Items.Where(
                        item => EF.Parameter(differentlyCasedNames).Contains(item.CaseInsensitiveName))),
                await SelectIdsAsync(
                    context.Items.Where(item => EF.Parameter(convertedTexts).Contains(item.ConvertedText))),
                await SelectIdsAsync(context.Items.Where(item => EF.Parameter(statuses).Contains(item.Status))),
                await SelectIdsAsync(
                    context.Items.Where(item => EF.Parameter(absentStatuses).Contains(item.Status))),
                await SelectIdsAsync(
                    context.Items.Where(
                        item => EF.Parameter(nullableStatuses).Contains(item.OptionalStatus))),
                await SelectIdsAsync(
                    context.Items.Where(
                        item => EF.Parameter(exactConvertedPrices).Contains(item.ConvertedPrice))),
                await SelectIdsAsync(
                    context.Items.Where(
                        item => EF.Parameter(roundedConvertedPrices).Contains(item.ConvertedPrice))),
                await SelectIdsAsync(
                    context.Items.Where(item => EF.Parameter(convertedGuids).Contains(item.ConvertedGuid))),
                await SelectIdsAsync(
                    context.Items.Where(
                        item => EF.Parameter(absentConvertedGuids).Contains(item.ConvertedGuid))),
                await SelectIdsAsync(
                    context.Items.Where(
                        item => EF.Parameter(convertedDurations).Contains(item.ConvertedDuration))),
                await SelectIdsAsync(
                    context.Items.Where(
                        item => EF.Parameter(truncatedConvertedDurations).Contains(item.ConvertedDuration))),
                await Record.ExceptionAsync(() => SelectIdsAsync(
                    context.Items.Where(
                        item => EF.Parameter(positiveOutOfRangeConvertedDurations)
                            .Contains(item.ConvertedDuration)))),
                await Record.ExceptionAsync(() => SelectIdsAsync(
                    context.Items.Where(
                        item => EF.Parameter(negativeOutOfRangeConvertedDurations)
                            .Contains(item.ConvertedDuration)))),
                await SelectIdsAsync(
                    context.Items.Where(
                        item => EF.Parameter(convertedTimestamps).Contains(item.ConvertedTimestamp))),
                await SelectIdsAsync(
                    context.Items.Where(
                        item => EF.Parameter(truncatedConvertedTimestamps).Contains(item.ConvertedTimestamp))),
                await SelectIdsAsync(
                    context.Items.Where(
                        item => EF.Parameter(convertedPayloads).Contains(item.ConvertedPayload))),
                await SelectIdsAsync(
                    context.Items.Where(
                        item => EF.Parameter(absentConvertedPayloads).Contains(item.ConvertedPayload))),
                await SelectIdsAsync(
                    context.Items.Where(item => EF.Parameter(differentlyCasedName).Contains(item.Name))),
                await SelectIdsAsync(context.Items.Where(item => EF.Parameter(payloads).Contains(item.Payload))),
                // Project the decoded rowset directly so this assertion covers
                // mapping without adding correlated-IN optimizer semantics.
                await context.Items
                    .Where(item => item.Id == 11)
                    .SelectMany(item => item.GuidValues)
                    .ToArrayAsync(CancellationToken.None),
                await charContext.Items
                    .SelectMany(item => item.GuidValues)
                    .ToArrayAsync(CancellationToken.None),
                stringDecodeCoercibility);

            Assert.Equal([1], results.OneBinaryGuid);
            Assert.Equal([1, 2, 3], results.ThreeBinaryGuids);
            Assert.Equal(Enumerable.Range(1, RowCount), results.AllBinaryGuids);
            Assert.Equal([1, 3], results.CharGuids);
            Assert.Empty(results.EmptyGuids);
            Assert.Equal([1], results.DuplicateAndAbsentGuids);
            Assert.Equal([1, 2], results.NullableBinaryGuids);
            Assert.Equal([1, 2], results.NullableCharGuids);
            Assert.Equal([2, 3], results.DefaultContainsGuids);
            Assert.Equal([1], results.ScalarGuid);
            Assert.Equal([1, RowCount], results.IntegerKeys);
            Assert.Equal([1, 2, 3], results.Names);
            Assert.Equal([1, 2], results.NullableNames);
            Assert.Equal([1], results.ConvertedNames);
            Assert.Empty(results.DifferentlyCasedConvertedName);
            Assert.Equal([1], results.DifferentlyCasedNames);
            Assert.Equal([1], results.ConvertedTexts);
            Assert.Equal([1], results.Statuses);
            Assert.Empty(results.AbsentStatuses);
            Assert.Equal([1, 2], results.NullableStatuses);
            Assert.Equal([1], results.ExactConvertedPrices);
            Assert.Empty(results.RoundedConvertedPrices);
            Assert.Equal([1], results.ConvertedGuids);
            Assert.Empty(results.AbsentConvertedGuids);
            Assert.Equal([1], results.ConvertedDurations);
            Assert.Empty(results.TruncatedConvertedDurations);
            Assert.Contains(
                "exceeds the MySQL TIME range",
                Assert.IsType<InvalidOperationException>(results.PositiveOutOfRangeConvertedDuration).Message,
                StringComparison.Ordinal);
            Assert.Contains(
                "exceeds the MySQL TIME range",
                Assert.IsType<InvalidOperationException>(results.NegativeOutOfRangeConvertedDuration).Message,
                StringComparison.Ordinal);
            Assert.Equal([1], results.ConvertedTimestamps);
            Assert.Empty(results.TruncatedConvertedTimestamps);
            Assert.Equal([1], results.ConvertedPayloads);
            Assert.Empty(results.AbsentConvertedPayloads);
            Assert.Empty(results.DifferentlyCasedName);
            Assert.Equal([1, 3], results.Payloads);
            Assert.Equal([CreateGuid(10), Guid.Empty], results.BinaryModelGuidCollection);
            Assert.Equal([CreateGuid(10), Guid.Empty], results.CharModelGuidCollection);
            Assert.True(
                results.StringDecodeCoercibility > 2,
                $"Expected coercible string decoding, but COERCIBILITY returned {results.StringDecodeCoercibility}.");
        }
        finally
        {
            await DropCharTableAsync(charContext);
            await DropTableAsync(context);
        }
    }

    private static async Task<int[]> SelectIdsAsync(
        IQueryable<PrimitiveCollectionEntity> query
    ) => await query
        .OrderBy(item => item.Id)
        .Select(item => item.Id)
        .ToArrayAsync(CancellationToken.None);

    private static Guid CreateGuid(
        int value
    ) => new(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static async Task DropTableAsync(
        PrimitiveCollectionContext context
    ) => await context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{TableName}`;",
        CancellationToken.None);

    private static async Task DropCharTableAsync(
        CharGuidCollectionContext context
    ) => await context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{CharTableName}`;",
        CancellationToken.None);

    private static async Task DropSqlModeTableAsync(
        SqlModeStringCollectionContext context
    ) => await context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{SqlModeTableName}`;",
        CancellationToken.None);

    private static async Task<string> ReadSqlModeAsync(
        DbConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@SESSION.sql_mode;";

        return Assert.IsType<string>(
            await command.ExecuteScalarAsync(CancellationToken.None));
    }

    private static async Task SetSqlModeAsync(
        DbConnection connection,
        string sqlMode
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SET SESSION sql_mode = @sqlMode;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@sqlMode";
        parameter.Value = sqlMode;
        command.Parameters.Add(parameter);

        _ = await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<int> ReadStringDecodeCoercibilityAsync(
        PrimitiveCollectionContext context
    )
    {
        await context.Database.OpenConnectionAsync(CancellationToken.None);

        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT COERCIBILITY(
                    JSON_UNQUOTE(JSON_QUOTE(CONVERT(FROM_BASE64('YQ==') USING utf8mb4))));
                """;

            return Convert.ToInt32(
                await command.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private sealed record PrimitiveCollectionResults(
        int[] OneBinaryGuid,
        int[] ThreeBinaryGuids,
        int[] AllBinaryGuids,
        int[] CharGuids,
        int[] EmptyGuids,
        int[] DuplicateAndAbsentGuids,
        int[] NullableBinaryGuids,
        int[] NullableCharGuids,
        int[] DefaultContainsGuids,
        int[] ScalarGuid,
        int[] IntegerKeys,
        int[] Names,
        int[] NullableNames,
        int[] ConvertedNames,
        int[] DifferentlyCasedConvertedName,
        int[] DifferentlyCasedNames,
        int[] ConvertedTexts,
        int[] Statuses,
        int[] AbsentStatuses,
        int[] NullableStatuses,
        int[] ExactConvertedPrices,
        int[] RoundedConvertedPrices,
        int[] ConvertedGuids,
        int[] AbsentConvertedGuids,
        int[] ConvertedDurations,
        int[] TruncatedConvertedDurations,
        Exception? PositiveOutOfRangeConvertedDuration,
        Exception? NegativeOutOfRangeConvertedDuration,
        int[] ConvertedTimestamps,
        int[] TruncatedConvertedTimestamps,
        int[] ConvertedPayloads,
        int[] AbsentConvertedPayloads,
        int[] DifferentlyCasedName,
        int[] Payloads,
        Guid[] BinaryModelGuidCollection,
        Guid[] CharModelGuidCollection,
        int StringDecodeCoercibility
    );

    private sealed class PrimitiveCollectionContext(
        DbContextOptions<PrimitiveCollectionContext> options
    ) : DbContext(options)
    {
        public DbSet<PrimitiveCollectionEntity> Items => Set<PrimitiveCollectionEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<PrimitiveCollectionEntity>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .ValueGeneratedNever();
                entity
                    .Property(item => item.BinaryId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity
                    .Property(item => item.OptionalBinaryId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity
                    .Property(item => item.OptionalCharId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity
                    .Property(item => item.CharId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity
                    .Property(item => item.Name)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.OptionalName)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.ConvertedName)
                    .HasConversion(
                        value => "db-" + value,
                        value => value.Substring(3))
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.CaseInsensitiveName)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_unicode_ci");
                entity
                    .Property(item => item.ConvertedText)
                    .HasConversion(
                        value => value.Value,
                        value => new TextValue(value))
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.Status)
                    .HasConversion<string>()
                    .HasMaxLength(16)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.OptionalStatus)
                    .HasConversion<string>()
                    .HasMaxLength(16)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.ConvertedPrice)
                    .HasConversion(
                        value => value.Value,
                        value => new PriceValue(value))
                    .HasPrecision(10, 1);
                entity
                    .Property(item => item.ConvertedGuid)
                    .HasConversion(
                        value => value.Value,
                        value => new StrongGuid(value))
                    .HasColumnType("binary(16)");
                entity
                    .Property(item => item.ConvertedDuration)
                    .HasConversion(
                        value => value.Value,
                        value => new ElapsedValue(value))
                    .HasPrecision(0);
                entity
                    .Property(item => item.ConvertedTimestamp)
                    .HasConversion(
                        value => value.UtcDateTime,
                        value => new DateTimeOffset(value, TimeSpan.Zero))
                    .HasPrecision(0);
                entity
                    .Property(item => item.ConvertedPayload)
                    .HasConversion(
                        value => System.Text.Encoding.UTF8.GetBytes(value.Value),
                        value => new BinaryValue(System.Text.Encoding.UTF8.GetString(value)))
                    .HasColumnType("varbinary(32)");
                entity
                    .Property(item => item.Payload)
                    .HasColumnType("varbinary(32)");
                entity
                    .PrimitiveCollection(item => item.GuidValues)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class CharGuidCollectionContext(
        DbContextOptions<CharGuidCollectionContext> options
    ) : DbContext(options)
    {
        public DbSet<CharGuidCollectionEntity> Items => Set<CharGuidCollectionEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<CharGuidCollectionEntity>(entity =>
            {
                entity.ToTable(CharTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .ValueGeneratedNever();
                entity
                    .PrimitiveCollection(item => item.GuidValues)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class SqlModeStringCollectionContext(
        DbContextOptions<SqlModeStringCollectionContext> options
    ) : DbContext(options)
    {
        public DbSet<SqlModeStringCollectionEntity> Items => Set<SqlModeStringCollectionEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SqlModeStringCollectionEntity>(entity =>
            {
                entity.ToTable(SqlModeTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .ValueGeneratedNever();
                entity
                    .Property(item => item.Value)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.OptionalValue)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.ConvertedValue)
                    .HasConversion(
                        value => "db-" + value,
                        value => value.Substring(3))
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.ConvertedText)
                    .HasConversion(
                        value => value.Value,
                        value => new TextValue(value))
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .PrimitiveCollection(item => item.Values)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class PrimitiveCollectionEntity
    {
        public int Id { get; set; }

        public Guid BinaryId { get; set; }

        public Guid? OptionalBinaryId { get; set; }

        public Guid? OptionalCharId { get; set; }

        public Guid CharId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? OptionalName { get; set; }

        public string ConvertedName { get; set; } = string.Empty;

        public string CaseInsensitiveName { get; set; } = string.Empty;

        public TextValue ConvertedText { get; set; }

        public CollectionStatus Status { get; set; }

        public CollectionStatus? OptionalStatus { get; set; }

        public PriceValue ConvertedPrice { get; set; }

        public StrongGuid ConvertedGuid { get; set; }

        public ElapsedValue ConvertedDuration { get; set; }

        public DateTimeOffset ConvertedTimestamp { get; set; }

        public BinaryValue ConvertedPayload { get; set; }

        public byte[] Payload { get; set; } = [];

        public Guid[] GuidValues { get; set; } = [];
    }

    private enum CollectionStatus
    {
        Inactive,
        Active,
    }

    private readonly record struct PriceValue(decimal Value);

    private readonly record struct StrongGuid(Guid Value);

    private readonly record struct ElapsedValue(TimeSpan Value);

    private readonly record struct TextValue(string Value);

    private readonly record struct BinaryValue(string Value);

    private sealed class CharGuidCollectionEntity
    {
        public int Id { get; set; }

        public Guid[] GuidValues { get; set; } = [];
    }

    private sealed class SqlModeStringCollectionEntity
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;

        public string? OptionalValue { get; set; }

        public string ConvertedValue { get; set; } = string.Empty;

        public TextValue ConvertedText { get; set; }

        public string?[] Values { get; set; } = [];
    }
}
