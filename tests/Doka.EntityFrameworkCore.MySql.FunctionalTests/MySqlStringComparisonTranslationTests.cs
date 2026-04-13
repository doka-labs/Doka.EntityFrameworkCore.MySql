namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the string-comparison and collation translation baseline.
/// </summary>
public sealed class MySqlStringComparisonTranslationTests
{
    /// <summary>
    /// Verifies that the supported baseline string comparisons translate server-side.
    /// </summary>
    [Fact]
    public void Supported_string_comparisons_translate_server_side()
    {
        using var context = new StringComparisonContext(CreateOptions());

        var sql = context
            .Entities.Where(entity =>
                entity.Name.Contains("alp")
                || entity.Name.StartsWith("alp")
                || entity.Name.EndsWith("alp")
                || entity.Name == "alp")
            .ToQueryString();

        Assert.Contains("LOCATE(", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT(", sql, StringComparison.Ordinal);
        Assert.Contains("RIGHT(", sql, StringComparison.Ordinal);
        Assert.Contains("`Name` = 'alp'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that explicit collation requests remain translatable.
    /// </summary>
    [Fact]
    public void Collate_escape_hatch_remains_translatable()
    {
        using var context = new StringComparisonContext(CreateOptions());

        var sql = context
            .Entities.Where(entity => EF.Functions.Collate(entity.Name, "utf8mb4_bin") == "alp")
            .ToQueryString();

        Assert.Contains("COLLATE", sql, StringComparison.Ordinal);
        Assert.Contains("utf8mb4_bin", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that unsupported StringComparison overloads still fail explicitly.
    /// </summary>
    [Theory]
    [InlineData(UnsupportedStringComparisonOperation.Contains)]
    [InlineData(UnsupportedStringComparisonOperation.StartsWith)]
    [InlineData(UnsupportedStringComparisonOperation.EndsWith)]
    [InlineData(UnsupportedStringComparisonOperation.Equals)]
    public void Unsupported_stringcomparison_overloads_fail_explicitly(
        UnsupportedStringComparisonOperation operation
    )
    {
        using var context = new StringComparisonContext(CreateOptions());

        var exception = Assert.Throws<InvalidOperationException>(() => BuildUnsupportedQuery(context, operation)
            .ToQueryString());

        Assert.Contains("could not be translated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IQueryable<StringComparisonEntity> BuildUnsupportedQuery(
        StringComparisonContext context,
        UnsupportedStringComparisonOperation operation
    )
    {
        return operation switch
        {
            UnsupportedStringComparisonOperation.Contains =>
                context.Entities.Where(entity => entity.Name.Contains("alp", StringComparison.OrdinalIgnoreCase)),
            UnsupportedStringComparisonOperation.StartsWith =>
                context.Entities.Where(entity => entity.Name.StartsWith("alp", StringComparison.OrdinalIgnoreCase)),
            UnsupportedStringComparisonOperation.EndsWith => context.Entities.Where(entity =>
                entity.Name.EndsWith("alp", StringComparison.OrdinalIgnoreCase)),
            UnsupportedStringComparisonOperation.Equals => context.Entities.Where(entity =>
                entity.Name.Equals("alp", StringComparison.OrdinalIgnoreCase)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    private static DbContextOptions<StringComparisonContext> CreateOptions()
    {
        var builder = new DbContextOptionsBuilder<StringComparisonContext>();

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder.Options;
    }

    private sealed class StringComparisonContext : DbContext
    {
        public StringComparisonContext(
            DbContextOptions<StringComparisonContext> options
        ) : base(options) { }

        public DbSet<StringComparisonEntity> Entities => Set<StringComparisonEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<StringComparisonEntity>(entity =>
            {
                entity.ToTable("Phase2StringComparisonEntities");
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Name)
                    .IsRequired();
            });
        }
    }

    private sealed class StringComparisonEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Identifies the unsupported overload under test.
    /// </summary>
    public enum UnsupportedStringComparisonOperation
    {
        /// <summary>
        /// Uses <see cref="string.Contains(string, StringComparison)"/>.
        /// </summary>
        Contains = 0,

        /// <summary>
        /// Uses <see cref="string.StartsWith(string, StringComparison)"/>.
        /// </summary>
        StartsWith = 1,

        /// <summary>
        /// Uses <see cref="string.EndsWith(string, StringComparison)"/>.
        /// </summary>
        EndsWith = 2,

        /// <summary>
        /// Uses <see cref="string.Equals(string, StringComparison)"/>.
        /// </summary>
        Equals = 3,
    }
}
