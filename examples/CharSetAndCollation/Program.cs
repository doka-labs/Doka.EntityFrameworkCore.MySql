var database = ExampleDatabaseConfiguration.Create("doka_example_charset_collation");
var cancellationToken = CancellationToken.None;

// WHY: Options supply missing model defaults; explicit column contracts retain precedence.
var options = new DbContextOptionsBuilder<CollationContext>()
    .UseMySql(database.ConnectionString,
        database.ServerVersion,
        provider => provider
            .DefaultCharSet("utf8mb4")
            .DefaultCollation("utf8mb4_unicode_ci"))
    .Options;

await using var context = new CollationContext(options);
await context.Database.EnsureDeletedAsync(cancellationToken);

try
{
    await context.Database.EnsureCreatedAsync(cancellationToken);
    var model = context.GetService<IDesignTimeModel>()
        .Model;

    var schema = context.Database.GenerateCreateScript();

    if (model.GetCollation() != "utf8mb4_unicode_ci"
        || !schema.Contains("ALTER DATABASE CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci;",
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("The options did not supply the expected canonical model defaults.");
    }

    // WHY: Check actual column facts too, so a coincidentally matching server default cannot satisfy the example.
    var collations = await context
        .Database
        .SqlQueryRaw<string>(
            "SELECT CONCAT(COLUMN_NAME, ':', CHARACTER_SET_NAME, ':', COLLATION_NAME) AS `Value` "
            + "FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='Labels' "
            + "AND COLUMN_NAME IN ('FallbackText','SearchCode','Title') ORDER BY COLUMN_NAME;")
        .ToArrayAsync(cancellationToken);

    string[] expectedCollations =
    [
        "FallbackText:utf8mb4:utf8mb4_unicode_ci",
        "SearchCode:utf8mb4:utf8mb4_unicode_ci",
        "Title:utf8mb4:utf8mb4_bin",
    ];

    if (!collations.SequenceEqual(expectedCollations, StringComparer.Ordinal))
    {
        throw new InvalidOperationException(
            "The physical text defaults or explicit column overrides were not preserved.");
    }

    context.Labels.AddRange(
        new Label
        {
            Title = "Alpha",
            SearchCode = "DOKA",
            FallbackText = "Alpha",
        },
        new Label
        {
            Title = "alpha",
            SearchCode = "provider",
            FallbackText = "alpha",
        });

    await context.SaveChangesAsync(cancellationToken);

    var binaryMatches = await context
        .Labels
        .AsNoTracking()
        .Where(label => label.Title == "alpha")
        .CountAsync(cancellationToken);

    var caseInsensitiveMatches = await context
        .Labels
        .AsNoTracking()
        .Where(label => label.SearchCode == "doka")
        .CountAsync(cancellationToken);

    var fallbackMatches = await context
        .Labels
        .AsNoTracking()
        .Where(label => label.FallbackText == "alpha")
        .CountAsync(cancellationToken);

    var binaryUppercaseMatches = await context
        .Labels
        .AsNoTracking()
        .Where(label => label.Title == "ALPHA")
        .CountAsync(cancellationToken);

    if (binaryMatches != 1
        || caseInsensitiveMatches != 1
        || fallbackMatches != 2
        || binaryUppercaseMatches != 0)
    {
        throw new InvalidOperationException("The configured collation semantics were not preserved.");
    }

    Console.WriteLine(
        $"{database.Target}: binaryMatches={binaryMatches}, caseInsensitiveMatches={caseInsensitiveMatches}, "
        + $"fallbackMatches={fallbackMatches}, binaryUppercaseMatches={binaryUppercaseMatches}");
}
finally
{
    await context.Database.EnsureDeletedAsync(cancellationToken);
}

internal sealed class CollationContext : DbContext
{
    public CollationContext(
        DbContextOptions<CollationContext> options
    ) : base(options) { }

    public DbSet<Label> Labels => Set<Label>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<Label>(entity =>
        {
            // WHY: Inherit the default pair; a charset-only table override can select its own native collation instead.
            entity.ToTable("Labels");
            entity.UseStorageEngine("InnoDB");
            entity.HasKey(label => label.Id);

            // Identity-like titles remain byte- and case-sensitive, while the
            // search code models a user-facing case-insensitive lookup.
            entity
                .Property(label => label.Title)
                .HasMaxLength(200)
                .UseCollation("utf8mb4_bin");

            entity
                .Property(label => label.SearchCode)
                .HasMaxLength(64)
                .UseCollation("utf8mb4_unicode_ci");

            // The prefix is part of the provider's index metadata and avoids
            // indexing all bytes of a long utf8mb4 value.
            entity
                .HasIndex(label => label.Title)
                .HasPrefixLength(32);
        });
    }
}

internal sealed class Label
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string SearchCode { get; set; } = string.Empty;

    /// <summary>Inherits the application text defaults without a per-property charset or collation override.</summary>
    public string FallbackText { get; set; } = string.Empty;
}
