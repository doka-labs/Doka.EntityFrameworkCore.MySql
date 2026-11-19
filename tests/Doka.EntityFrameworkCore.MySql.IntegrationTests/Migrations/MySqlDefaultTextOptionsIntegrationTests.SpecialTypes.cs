namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlDefaultTextOptionsIntegrationTests
{
    private const string JsonSpecialTable = "DefaultTextJsonSpecialTypes";
    private const string GuidSpecialTable = "DefaultTextGuidSpecialTypes";
    private const string ScalarSpecialTable = "DefaultTextScalarSpecialTypes";

    /// <summary>Reports the engines on which native UUID must remain an explicit prewrite rejection.</summary>
    public static IEnumerable<TheoryDataRow<string>> UnsupportedSpecialUuidTargets => SelectedTargets
        .Where(target => !IntegrationTestEnvironment.GetServerVersion(ParseTarget(target.Data))
            .IsMariaDb);

    /// <summary>Reports each supported physical GUID representation independently for each selected engine.</summary>
    public static IEnumerable<TheoryDataRow<string, MySqlGuidFormat>> SpecialGuidCases()
    {
        foreach (var target in IntegrationTestEnvironment
            .GetSelectedTargets()
            .Where(target => IntegrationTestEnvironment
                .GetSupportedTargets()
                .Contains(target)))
        {
            yield return new TheoryDataRow<string, MySqlGuidFormat>(target.ToString(), MySqlGuidFormat.Binary16);
            yield return new TheoryDataRow<string, MySqlGuidFormat>(target.ToString(), MySqlGuidFormat.Char36);

            if (IntegrationTestEnvironment.GetServerVersion(target)
                .IsMariaDb)
            {
                yield return new TheoryDataRow<string, MySqlGuidFormat>(target.ToString(), MySqlGuidFormat.Uuid);
            }
        }
    }

    /// <summary>Reports decoded JSON membership and ordinal or JSON-type counterexamples independently.</summary>
    public static IEnumerable<TheoryDataRow<string, string, string?, bool, bool>> SpecialStringMembershipCases()
    {
        (string Document, string? Needle, bool Matches)[] cases =
        [
            ("[\"Caf\\u00E9\"]", "Caf\u00e9", true),
            ("[\"\\uD83D\\uDE42\"]", "\U0001F642", true),
            ("[\"quote \\u0027\"]", "quote '", true),
            ("[\"A\"]", "a", false),
            ("[\"Caf\\u00E9\"]", "Cafe", false),
            ("[\"a \"]", "a", false),
            ("[\"a \"]", "a ", true),
            ("[\"\"]", "", true),
            ("[\"a\"]", "", false),
            ("[null]", null, true),
            ("[\"null\"]", null, false),
            ("[null]", "null", false),
            ("[1]", "1", false),
            ("[true]", "true", false),
            ("[\"1\"]", "1", true),
            ("[\"%_\\\\\"]", "%_\\", true),
            ("[\"ordinary\"]", "%", false),
        ];

        foreach (var target in SelectedTargets)
        {
            foreach (var testCase in cases)
            {
                yield return new TheoryDataRow<string, string, string?, bool, bool>(target.Data,
                    testCase.Document,
                    testCase.Needle,
                    testCase.Matches,
                    false);

                yield return new TheoryDataRow<string, string, string?, bool, bool>(target.Data,
                    testCase.Document,
                    testCase.Needle,
                    testCase.Matches,
                    true);
            }
        }
    }

    /// <summary>Application defaults cannot turn JSON string membership into collation, pattern or scalar coercion.</summary>
    /// <param name="target">The selected engine profile.</param>
    /// <param name="document">The persisted JSON array including deliberate native non-string controls.</param>
    /// <param name="needle">The string or null sought by the LINQ predicate.</param>
    /// <param name="matches">Whether exact JSON string or null membership must succeed.</param>
    /// <param name="negated">Whether the LINQ predicate negates membership.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SpecialStringMembershipCases))]
    public async Task Enabled_defaults_preserve_exact_primitive_string_membership_semantics(
        string target,
        string document,
        string? needle,
        bool matches,
        bool negated
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await using var context =
            new StringMembershipSpecialContext(CreateSpecialOptions<StringMembershipSpecialContext>(database));

        await CreateSpecialSchemaAsync(context, CancellationToken.None);
        context.Add(new JsonSpecialRow
        {
            Id = 1,
            Name = "membership",
            Owned = new JsonSpecialOwned { Label = "owner" },
        });

        await context.SaveChangesAsync(CancellationToken.None);

        // WHY: Native numeric/bool controls must remain distinct from CLR strings; only the predicate
        // is executed against these documents, so invalid List<string> materialization is not involved.
        await using var command =
            new MySqlCommand("UPDATE `DefaultTextJsonSpecialTypes` SET `Tags` = @document WHERE `Id` = 1;",
                database.Connection);

        command.Parameters.AddWithValue("@document", document);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
        var query = negated
            ? context
                .Set<JsonSpecialRow>()
                .Where(row => !row.Tags.Contains(needle!))
            : context
                .Set<JsonSpecialRow>()
                .Where(row => row.Tags.Contains(needle!));

        // Act
        var count = await query.CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(matches != negated
                ? 1
                : 0,
            count);
    }

    /// <summary>Owned JSON and a real primitive List retain persisted values and server-side membership with defaults enabled.</summary>
    /// <param name="target">The selected engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Enabled_defaults_preserve_owned_json_and_primitive_list_query_readback(
        string target
    )
    {
        // Arrange
        const string ownerLabel = "Caf\u00e9 'owner' \U0001F642";
        const string selectedTag = "quote ' \u00e9 \U0001F642";
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await using var context = new JsonSpecialContext(CreateSpecialOptions<JsonSpecialContext>(database));
        await CreateSpecialSchemaAsync(context, CancellationToken.None);
        var matching = new JsonSpecialRow
        {
            Id = 1,
            Name = "selected",
            Owned = new JsonSpecialOwned { Label = ownerLabel },
            Tags = ["alpha", selectedTag, "slash\\path", "double\"quote"],
        };

        context
            .Set<JsonSpecialRow>()
            .AddRange(matching,
                new JsonSpecialRow
                {
                    Id = 2,
                    Name = "other",
                    Owned = new JsonSpecialOwned { Label = "different owner" },
                    Tags = [selectedTag],
                });

        await context.SaveChangesAsync(CancellationToken.None);
        var query = context
            .Set<JsonSpecialRow>()
            .AsNoTracking()
            .Where(row => row.Owned.Label == ownerLabel && row.Tags.Contains(selectedTag));

        // Act
        var actual = await query.SingleAsync(CancellationToken.None);
        var ownedCollation = await ReadColumnFactAsync(database.Connection,
            JsonSpecialTable,
            "Owned",
            false,
            CancellationToken.None);

        var sql = query.ToQueryString();

        // Assert
        Assert.Equal(1, actual.Id);
        Assert.Equal(ownerLabel, actual.Owned.Label);
        Assert.Equal(matching.Tags, actual.Tags);
        Assert.Contains("JSON_EXTRACT(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.True(sql.Contains("JSON_CONTAINS(", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("JSON_TABLE(", StringComparison.OrdinalIgnoreCase),
            sql);

        Assert.DoesNotContain(UnicodeCollation, sql, StringComparison.Ordinal);

        if (IntegrationTestEnvironment.GetServerVersion(database.Target)
            .IsMariaDb)
        {
            Assert.Equal(BinaryCollation, ownedCollation);
        }
    }

    /// <summary>Each qualified GUID mapping keeps its physical type, parameter translation and persisted value.</summary>
    /// <param name="target">The selected engine profile.</param>
    /// <param name="format">The existing GUID storage representation.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SpecialGuidCases))]
    public async Task Enabled_defaults_preserve_guid_format_storage_and_query_readback(
        string target,
        MySqlGuidFormat format
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await using var context = new GuidSpecialContext(CreateSpecialOptions<GuidSpecialContext>(database, format));
        await CreateSpecialSchemaAsync(context, CancellationToken.None);
        context.Add(new GuidSpecialRow
        {
            Id = 1,
            Token = s_externalId,
        });

        await context.SaveChangesAsync(CancellationToken.None);

        // Act
        var actual = await context
            .Set<GuidSpecialRow>()
            .AsNoTracking()
            .SingleAsync(row => row.Token == s_externalId, CancellationToken.None);

        var storeType = await ReadScalarAsync(database.Connection,
            "SELECT COLUMN_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() "
            + "AND TABLE_NAME = 'DefaultTextGuidSpecialTypes' AND COLUMN_NAME = 'Token';",
            CancellationToken.None);

        var collation = await ReadColumnFactAsync(database.Connection,
            GuidSpecialTable,
            "Token",
            false,
            CancellationToken.None);

        // Assert
        Assert.Equal(s_externalId, actual.Token);
        Assert.Equal(format switch
            {
                MySqlGuidFormat.Binary16 => "binary(16)",
                MySqlGuidFormat.Char36 => "char(36)",
                MySqlGuidFormat.Uuid => "uuid",
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            },
            storeType,
            ignoreCase: true);

        if (format == MySqlGuidFormat.Char36)
        {
            Assert.Equal(UnicodeCollation, collation);
        }
        else
        {
            Assert.Null(collation);
        }
    }

    /// <summary>Explicit ASCII, an application GUID converter and arbitrary BLOB bytes survive the opted-in defaults.</summary>
    /// <param name="target">The selected engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Enabled_defaults_preserve_ascii_converter_and_blob_query_readback(
        string target
    )
    {
        // Arrange
        const string ascii = "ASCII 'quoted' \\ path";
        byte[] bytes = [0, 39, 92, 128, 255];
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await using var context = new ScalarSpecialContext(CreateSpecialOptions<ScalarSpecialContext>(database));
        await CreateSpecialSchemaAsync(context, CancellationToken.None);
        context.Add(new ScalarSpecialRow
        {
            Id = 1,
            AsciiText = ascii,
            ConvertedGuid = s_externalId,
            Blob = bytes,
        });

        await context.SaveChangesAsync(CancellationToken.None);

        // Act
        var actual = await context
            .Set<ScalarSpecialRow>()
            .AsNoTracking()
            .SingleAsync(row => row.AsciiText == ascii && row.ConvertedGuid == s_externalId, CancellationToken.None);

        var asciiCharSet = await ReadColumnFactAsync(database.Connection,
            ScalarSpecialTable,
            "AsciiText",
            true,
            CancellationToken.None);

        var asciiCollation = await ReadColumnFactAsync(database.Connection,
            ScalarSpecialTable,
            "AsciiText",
            false,
            CancellationToken.None);

        var blobCollation = await ReadColumnFactAsync(database.Connection,
            ScalarSpecialTable,
            "Blob",
            false,
            CancellationToken.None);

        var convertedLength = await ReadScalarAsync(database.Connection,
            "SELECT OCTET_LENGTH(`ConvertedGuid`) FROM `DefaultTextScalarSpecialTypes` WHERE `Id` = 1;",
            CancellationToken.None);

        // Assert
        Assert.Equal(ascii, actual.AsciiText);
        Assert.Equal(s_externalId, actual.ConvertedGuid);
        Assert.Equal(bytes, actual.Blob);
        Assert.Equal("ascii", asciiCharSet);
        Assert.Equal("ascii_bin", asciiCollation);
        Assert.Null(blobCollation);
        Assert.Equal("36", convertedLength);
    }

    /// <summary>Text defaults cannot silently turn unsupported MySQL native UUID into another GUID format.</summary>
    /// <param name="target">The selected MySQL engine profile.</param>
    [RequiresDatabaseTargetsTheory([IntegrationDatabaseTarget.MySql84, IntegrationDatabaseTarget.MySql97])]
    [MemberData(nameof(UnsupportedSpecialUuidTargets))]
    public async Task Enabled_defaults_do_not_bypass_mysql_native_uuid_rejection(
        string target
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);

        // Act
        var exception = Record.Exception(() =>
        {
            using var context =
                new GuidSpecialContext(CreateSpecialOptions<GuidSpecialContext>(database, MySqlGuidFormat.Uuid));

            _ = context.Model;
        });

        var tables = await ReadScalarAsync(database.Connection,
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE();",
            CancellationToken.None);

        // Assert
        Assert.IsType<NotSupportedException>(exception);
        Assert.Equal("0", tables);
    }

    /// <summary>Uses the same immutable opt-in defaults with each independent special-purpose model.</summary>
    private static DbContextOptions<TContext> CreateSpecialOptions<TContext>(
        TextDatabase database,
        MySqlGuidFormat format = MySqlGuidFormat.Binary16
    )
        where TContext : DbContext => IntegrationTestDbContextOptions
        .Create<TContext>()
        .EnableServiceProviderCaching(false)
        .UseMySql(database.Connection,
            IntegrationTestEnvironment.GetServerVersion(database.Target),
            provider => provider
                .DefaultGuidFormat(format)
                .DefaultCharSet("utf8mb4")
                .DefaultCollation(UnicodeCollation))
        .Options;

    /// <summary>Executes the production schema script for the isolated special-purpose model.</summary>
    private static Task<int> CreateSpecialSchemaAsync(
        DbContext context,
        CancellationToken cancellationToken
    ) => context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), cancellationToken);

    private sealed class JsonSpecialContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public JsonSpecialContext(
            DbContextOptions<JsonSpecialContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<JsonSpecialRow>(entity =>
        {
            entity.ToTable(JsonSpecialTable);
            entity.HasKey(row => row.Id);
            entity
                .Property(row => row.Id)
                .ValueGeneratedNever();

            entity
                .Property(row => row.Name)
                .HasMaxLength(64);

            entity.OwnsOne(row => row.Owned, owned => owned.ToJson());
            entity.PrimitiveCollection(row => row.Tags);
        });
    }

    private sealed class StringMembershipSpecialContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public StringMembershipSpecialContext(
            DbContextOptions<StringMembershipSpecialContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<JsonSpecialRow>(entity =>
        {
            entity.ToTable(JsonSpecialTable);
            entity.HasKey(row => row.Id);
            entity
                .Property(row => row.Id)
                .ValueGeneratedNever();

            entity
                .Property(row => row.Name)
                .HasMaxLength(64);

            entity.OwnsOne(row => row.Owned, owned => owned.ToJson());

            // WHY: Null membership is valid only when the model permits null elements;
            // the ordinary nonnullable List<string> roundtrip keeps its separate model.
            entity
                .PrimitiveCollection(row => row.Tags)
                .ElementType(element => element.IsRequired(false));
        });
    }

    private sealed class GuidSpecialContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public GuidSpecialContext(
            DbContextOptions<GuidSpecialContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<GuidSpecialRow>(entity =>
        {
            entity.ToTable(GuidSpecialTable);
            entity.HasKey(row => row.Id);
            entity
                .Property(row => row.Id)
                .ValueGeneratedNever();
        });
    }

    private sealed class ScalarSpecialContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public ScalarSpecialContext(
            DbContextOptions<ScalarSpecialContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<ScalarSpecialRow>(entity =>
        {
            entity.ToTable(ScalarSpecialTable);
            entity.HasKey(row => row.Id);
            entity
                .Property(row => row.Id)
                .ValueGeneratedNever();

            entity
                .Property(row => row.AsciiText)
                .HasMaxLength(64)
                .IsUnicode(false)
                .UseCollation("ascii_bin");

            entity
                .Property(row => row.ConvertedGuid)
                .HasConversion<GuidToStringConverter>()
                .HasMaxLength(36);

            entity
                .Property(row => row.Blob)
                .HasColumnType("longblob");
        });
    }

    private sealed class JsonSpecialRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public JsonSpecialOwned Owned { get; set; } = new();

        public List<string> Tags { get; set; } = [];
    }

    private sealed class JsonSpecialOwned
    {
        public string Label { get; set; } = string.Empty;
    }

    private sealed class GuidSpecialRow
    {
        public int Id { get; set; }

        public Guid Token { get; set; }
    }

    private sealed class ScalarSpecialRow
    {
        public int Id { get; set; }

        public string AsciiText { get; set; } = string.Empty;

        public Guid ConvertedGuid { get; set; }

        public byte[] Blob { get; set; } = [];
    }
}
