namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlQueryTranslationExtendedTests
{
    private enum QueryStatus
    {
        Inactive = 0,
        Active = 1
    }

    private sealed class EnumQueryEntity
    {
        public int Id { get; set; }
        public QueryStatus Status { get; set; }
    }

    private sealed class EnumQueryContext : DbContext
    {
        public EnumQueryContext(
            DbContextOptions<EnumQueryContext> options
        ) : base(options) { }

        public DbSet<EnumQueryEntity> Items => Set<EnumQueryEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<EnumQueryEntity>()
                .ToTable("EnumQueryEntities");
        }
    }

    private static DbContextOptions<EnumQueryContext> CreateEnumOptions()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<EnumQueryContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder.Options;
    }

    private sealed class AutoIncludeBlog
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<AutoIncludePost> Posts { get; set; } = [];
    }

    private sealed class AutoIncludePost
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public int BlogId { get; set; }
    }

    private sealed class AutoIncludeContext : DbContext
    {
        public AutoIncludeContext(
            DbContextOptions<AutoIncludeContext> options
        ) : base(options) { }

        public DbSet<AutoIncludeBlog> Blogs => Set<AutoIncludeBlog>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<AutoIncludeBlog>(entity =>
            {
                entity.ToTable("AutoIncludeBlogs");
                entity
                    .Navigation(b => b.Posts)
                    .AutoInclude();
            });

            modelBuilder
                .Entity<AutoIncludePost>()
                .ToTable("AutoIncludePosts");
        }
    }

    private static DbContextOptions<AutoIncludeContext> CreateAutoIncludeOptions()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<AutoIncludeContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder.Options;
    }

    private static DbContextOptions<JsonPropertyContext> CreateJsonOptions()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<JsonPropertyContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder.Options;
    }

    private sealed class JsonPropertyContext : DbContext
    {
        public JsonPropertyContext(
            DbContextOptions<JsonPropertyContext> options
        ) : base(options) { }

        public DbSet<JsonPropertyEntity> Items => Set<JsonPropertyEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonPropertyEntity>(entity =>
            {
                entity.ToTable("JsonPropertyEntities");
                entity.HasKey(e => e.Id);
                entity
                    .Property(e => e.Data)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class JsonPropertyEntity
    {
        public int Id { get; set; }
        public JsonElement Data { get; set; }
    }

    private static TranslationTestContext CreateContext()
        => CreateContext(MySqlServerVersion.MySql(new Version(8, 4, 0)));

    private static TranslationTestContext CreateContext(
        MySqlServerVersion serverVersion
    )
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TranslationTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion);

        return new TranslationTestContext(builder.Options);
    }

    private static TranslationTestContext CreateMariaDbContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TranslationTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)));

        return new TranslationTestContext(builder.Options);
    }

    private sealed class TranslationTestContext : DbContext
    {
        public TranslationTestContext(
            DbContextOptions<TranslationTestContext> options
        ) : base(options) { }

        public DbSet<TranslationTestItem> Items => Set<TranslationTestItem>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TranslationTestItem>(entity =>
            {
                entity.ToTable("TranslationTestItems");
                entity.HasKey(e => e.Id);
            });
        }
    }

    private sealed class TranslationTestItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NullableName { get; set; }
        public uint? NullableUnsignedValue { get; set; }
        public double Value { get; set; }
        public float SingleValue { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
