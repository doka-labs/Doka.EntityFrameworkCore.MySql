namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains compact models for opted-in text-default compatibility with specialized mappings.</summary>
public sealed partial class MySqlDefaultTextModelTests
{
    private static DbContextOptions<TContext> SpecialTypeOptions<TContext>(
        bool mariaDb,
        MySqlGuidFormat guidFormat = MySqlGuidFormat.Binary16,
        bool textDefaults = true
    )
        where TContext : DbContext
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>();
        builder.UseMySql(ConnectionString,
            mariaDb
                ? MySqlServerVersion.MariaDb(new Version(12, 3, 0))
                : MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options =>
            {
                options
                    .DefaultGuidFormat(guidFormat)
                    .UseNetTopologySuite();

                if (textDefaults)
                {
                    options
                        .DefaultCharSet("utf8mb4")
                        .DefaultCollation("utf8mb4_unicode_ci");
                }
            });

        return builder.Options;
    }

    /// <summary>Finds the exact column line rather than confusing a table default with a column override.</summary>
    private static string ColumnDefinition(
        string script,
        string column
    ) => script
        .Split('\n')
        .Single(line => line
            .TrimStart()
            .StartsWith($"`{column}` ", StringComparison.Ordinal));

    private sealed class JsonDefaultsContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public JsonDefaultsContext(
            DbContextOptions<JsonDefaultsContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<JsonDefaultsRow>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity
                .Property(row => row.Name)
                .HasMaxLength(80);

            entity.OwnsOne(row => row.Owned, owned => owned.ToJson());
            entity.PrimitiveCollection(row => row.Tags);
        });
    }

    private sealed class JsonDefaultsRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public JsonDefaultsOwned Owned { get; set; } = new();

        public string[] Tags { get; set; } = [];
    }

    private sealed class JsonDefaultsOwned
    {
        public string Label { get; set; } = string.Empty;
    }

    private sealed class GuidDefaultsContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public GuidDefaultsContext(
            DbContextOptions<GuidDefaultsContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder
            .Entity<GuidDefaultsRow>()
            .HasKey(row => row.Id);
    }

    private sealed class GuidDefaultsRow
    {
        public int Id { get; set; }

        public Guid Value { get; set; }
    }

    private sealed class ScalarDefaultsContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public ScalarDefaultsContext(
            DbContextOptions<ScalarDefaultsContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<ScalarDefaultsRow>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity
                .Property(row => row.HintedText)
                .HasMaxLength(32)
                .IsUnicode(false);

            entity
                .Property(row => row.AsciiText)
                .HasMaxLength(32)
                .IsUnicode(false)
                .UseCollation("ascii_general_ci");

            entity
                .Property(row => row.ConvertedGuid)
                .HasConversion<GuidToStringConverter>()
                .HasMaxLength(36);

            entity
                .Property(row => row.Bytes)
                .HasColumnType("varbinary(32)");

            entity
                .Property(row => row.Blob)
                .HasColumnType("longblob");

            entity
                .Property(row => row.Location)
                .HasColumnType("point")
                .HasSrid(4326);
        });
    }

    private sealed class ScalarDefaultsRow
    {
        public int Id { get; set; }

        public string HintedText { get; set; } = string.Empty;

        public string AsciiText { get; set; } = string.Empty;

        public Guid ConvertedGuid { get; set; }

        public byte[] Bytes { get; set; } = [];

        public byte[] Blob { get; set; } = [];

        public Point Location { get; set; } = new(0, 0) { SRID = 4326 };
    }
}
