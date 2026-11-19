namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies model cache, singleton, pooled-context and concurrent initialization boundaries.</summary>
public sealed partial class MySqlDefaultTextOptionsTests
{
    /// <summary>Same-type contexts use their own model when schema defaults differ.</summary>
    [Fact]
    public void Same_context_type_has_correct_cached_models_for_each_default_pair()
    {
        // Arrange
        var firstOptions = GlobalCacheOptions("utf8mb4_unicode_ci");
        var secondOptions = GlobalCacheOptions("utf8mb4_bin");
        using var first = new CachedTextContext(firstOptions);
        using var second = new CachedTextContext(secondOptions);
        using var repeated = new CachedTextContext(firstOptions);

        // Act
        var firstModel = first.Model;
        var secondModel = second.Model;
        var repeatedModel = repeated.Model;

        // Assert
        Assert.NotSame(firstModel, secondModel);
        Assert.Same(firstModel, repeatedModel);
        Assert.Equal("utf8mb4_unicode_ci",
            first
                .GetService<IDesignTimeModel>()
                .Model
                .GetCollation());

        Assert.Equal("utf8mb4_bin",
            second
                .GetService<IDesignTimeModel>()
                .Model
                .GetCollation());
    }

    /// <summary>Explicitly sharing an internal provider does not bypass its immutable text snapshot.</summary>
    [Fact]
    public void Shared_internal_provider_rejects_another_default_pair()
    {
        // Arrange
        using var services = new ServiceCollection()
            .AddEntityFrameworkDokaMySql()
            .BuildServiceProvider();

        using var original = new CachedTextContext(
            new DbContextOptionsBuilder<CachedTextContext>(ContextOptions("utf8mb4_unicode_ci"))
                .UseInternalServiceProvider(services)
                .Options);

        _ = original.Model;

        void act()
        {
            using var changed = new CachedTextContext(
                new DbContextOptionsBuilder<CachedTextContext>(ContextOptions("utf8mb4_bin"))
                    .UseInternalServiceProvider(services)
                    .Options);

            _ = changed.Model;
        }

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>Pooled contexts retain configured model facets on reuse.</summary>
    [Fact]
    public void Pooled_contexts_keep_their_text_defaults()
    {
        // Arrange
        using var provider = new ServiceCollection()
            .AddEntityFrameworkDokaMySql()
            .BuildServiceProvider();

        // WHY: Pool reuse is the subject, not EF's process-wide provider cache. Own the internal
        // graph explicitly so this fixture neither accumulates cached providers nor depends on their count.
        using var services = new ServiceCollection()
            .AddDbContextPool<CachedTextContext>(builder => builder
                .UseInternalServiceProvider(provider)
                .UseMySql(ConnectionString,
                    MySqlServerVersion.MariaDb(new Version(12, 3, 0)),
                    options => options
                        .DefaultCharSet("utf8mb4")
                        .DefaultCollation("utf8mb4_unicode_ci")))
            .BuildServiceProvider();

        IModel original;
        using (var scope = services.CreateScope())
        {
            original = scope.ServiceProvider.GetRequiredService<CachedTextContext>()
                .Model;
        }

        // Act
        using var reusedScope = services.CreateScope();
        var reused = reusedScope.ServiceProvider.GetRequiredService<CachedTextContext>();
        var actual = reused.Model;

        // Assert
        Assert.Same(original, actual);
        Assert.Equal("utf8mb4_unicode_ci",
            reused
                .GetService<IDesignTimeModel>()
                .Model
                .GetCollation());
    }

    /// <summary>Pooled factories retain configured model facets after each context is returned.</summary>
    [Fact]
    public void Pooled_factories_keep_their_text_defaults()
    {
        // Arrange
        using var provider = new ServiceCollection()
            .AddEntityFrameworkDokaMySql()
            .BuildServiceProvider();

        // WHY: The factory must retain its pooled model while the fixture owns and disposes
        // its EF graph instead of registering another provider in the process-wide cache.
        using var services = new ServiceCollection()
            .AddPooledDbContextFactory<CachedTextContext>(builder => builder
                .UseInternalServiceProvider(provider)
                .UseMySql(ConnectionString,
                    MySqlServerVersion.MariaDb(new Version(12, 3, 0)),
                    options => options
                        .DefaultCharSet("utf8mb4")
                        .DefaultCollation("utf8mb4_bin")))
            .BuildServiceProvider();

        var factory = services.GetRequiredService<IDbContextFactory<CachedTextContext>>();
        IModel original;
        using (var first = factory.CreateDbContext())
        {
            original = first.Model;
        }

        // Act
        using var reused = factory.CreateDbContext();
        var actual = reused.Model;

        // Assert
        Assert.Same(original, actual);
        Assert.Equal("utf8mb4_bin",
            reused
                .GetService<IDesignTimeModel>()
                .Model
                .GetCollation());
    }

    /// <summary>Ordinary scoped registration carries the same canonical defaults as pooling.</summary>
    [Fact]
    public void Scoped_context_registration_keeps_text_defaults()
    {
        // Arrange
        using var provider = new ServiceCollection()
            .AddEntityFrameworkDokaMySql()
            .BuildServiceProvider();

        // WHY: Scoped registration verifies option propagation; the fixture owns its provider
        // to keep unrelated test history out of this non-cache assertion.
        using var services = new ServiceCollection()
            .AddDbContext<CachedTextContext>(builder => builder
                .UseInternalServiceProvider(provider)
                .UseMySql(ConnectionString,
                    MySqlServerVersion.MariaDb(new Version(12, 3, 0)),
                    options => options
                        .DefaultCharSet("utf8mb4")
                        .DefaultCollation("utf8mb4_unicode_ci")))
            .BuildServiceProvider();

        using var scope = services.CreateScope();

        // Act
        var context = scope.ServiceProvider.GetRequiredService<CachedTextContext>();
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        // Assert
        Assert.Equal("utf8mb4", model.GetMySqlCharSet());
        Assert.Equal("utf8mb4_unicode_ci", model.GetCollation());
    }

    /// <summary>Concurrent same-provider model initialization cannot observe a partial text-options snapshot.</summary>
    [Fact]
    public void Parallel_model_initialization_keeps_one_consistent_default_pair()
    {
        // Arrange
        using var services = new ServiceCollection()
            .AddEntityFrameworkDokaMySql()
            .BuildServiceProvider();

        var options = new DbContextOptionsBuilder<CachedTextContext>(ContextOptions("utf8mb4_unicode_ci"))
            .UseInternalServiceProvider(services)
            .Options;

        var models = new IModel?[16];

        // Act
        Parallel.For(0,
            models.Length,
            index =>
            {
                using var context = new CachedTextContext(options);
                models[index] = context.GetService<IDesignTimeModel>()
                    .Model;
            });

        // Assert
        Assert.All(models,
            model =>
            {
                Assert.NotNull(model);
                Assert.Same(models[0], model);
                Assert.Equal("utf8mb4", model.GetMySqlCharSet());
                Assert.Equal("utf8mb4_unicode_ci", model.GetCollation());
            });
    }

    /// <summary>Parallel mixed defaults and distinct context types retain separate cached models without opening a connection.</summary>
    [Fact]
    public void Parallel_mixed_default_pairs_and_context_types_keep_distinct_models_and_offline_queries()
    {
        // Arrange
        using var unicodeServices = new ServiceCollection()
            .AddEntityFrameworkDokaMySql()
            .BuildServiceProvider();

        using var latinServices = new ServiceCollection()
            .AddEntityFrameworkDokaMySql()
            .BuildServiceProvider();

        var opens = new MixedTextConnectionAttempts();
        var firstUnicode = MixedTextOptions<CachedTextContext>(unicodeServices, "utf8mb4", "utf8mb4_unicode_ci", opens);
        var firstLatin = MixedTextOptions<CachedTextContext>(latinServices, "latin1", "latin1_swedish_ci", opens);
        var secondUnicode =
            MixedTextOptions<AlternateCachedTextContext>(unicodeServices, "utf8mb4", "utf8mb4_unicode_ci", opens);

        var secondLatin =
            MixedTextOptions<AlternateCachedTextContext>(latinServices, "latin1", "latin1_swedish_ci", opens);

        var results = new MixedTextResult?[16];

        // Act
        Parallel.For(0,
            results.Length,
            index =>
            {
                using DbContext context = (index % 4) switch
                {
                    0 => new CachedTextContext(firstUnicode),
                    1 => new CachedTextContext(firstLatin),
                    2 => new AlternateCachedTextContext(secondUnicode),
                    _ => new AlternateCachedTextContext(secondLatin),
                };

                results[index] = new MixedTextResult(context.Model,
                    context.GetService<IDesignTimeModel>()
                        .Model,
                    context
                        .Set<CachedTextRow>()
                        .Where(row => row.Id > 0)
                        .Select(row => row.Name)
                        .ToQueryString(),
                    context.Database.GetDbConnection()
                        .State);
            });

        // Assert
        Assert.Equal(0, opens.OpenAttempts);

        // WHY: The ordinal records both selected option pair and context type, so each
        // result is checked against its own input instead of whichever model finished first.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < results.Length; index++)
        {
            var result = results[index];
            Assert.NotNull(result);
            Assert.Equal(index % 2 == 0
                    ? "utf8mb4"
                    : "latin1",
                result.Design.GetMySqlCharSet());

            Assert.Equal(index % 2 == 0
                    ? "utf8mb4_unicode_ci"
                    : "latin1_swedish_ci",
                result.Design.GetCollation());

            var first = results[index % 4];
            Assert.NotNull(first);
            Assert.Same(first.Runtime, result.Runtime);
            Assert.Same(first.Design, result.Design);
            Assert.Contains(index % 4 < 2
                    ? "`CachedTextRow`"
                    : "`AlternateCachedTextRows`",
                result.Sql,
                StringComparison.Ordinal);

            Assert.DoesNotContain("COLLATE", result.Sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SET NAMES", result.Sql, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ConnectionState.Closed, result.State);
        }

        Assert.Equal(4,
            results
                .Take(4)
                .Select(result => result?.Runtime)
                .Distinct(ReferenceEqualityComparer.Instance)
                .Count());
    }

    private static DbContextOptions<TContext> MixedTextOptions<TContext>(
        IServiceProvider services,
        string charSet,
        string collation,
        MixedTextConnectionAttempts opens
    )
        where TContext : DbContext => new DbContextOptionsBuilder<TContext>()

        // WHY: Each immutable default pair owns its service graph; both context types
        // share only a compatible graph, preserving real EF model-cache identity.
        .UseInternalServiceProvider(services)
        .UseMySql(ConnectionString,
            MySqlServerVersion.MariaDb(new Version(12, 3, 0)),
            provider => provider
                .DefaultCharSet(charSet)
                .DefaultCollation(collation))
        .AddInterceptors(opens)
        .Options;

    private static DbContextOptions<CachedTextContext> ContextOptions(
        string collation
    ) => new DbContextOptionsBuilder<CachedTextContext>()
        .UseMySql(ConnectionString,
            MySqlServerVersion.MariaDb(new Version(12, 3, 0)),
            options => options
                .DefaultCharSet("utf8mb4")
                .DefaultCollation(collation))
        .Options;

    /// <summary>Retains the actual global cache while isolating its expected process-history diagnostic.</summary>
    private static DbContextOptions<CachedTextContext> GlobalCacheOptions(
        string collation
    ) => new DbContextOptionsBuilder<CachedTextContext>(ContextOptions(collation))

        // WHY: This test must exercise the global cache. The suite may already have filled its
        // provider count; log only that unrelated diagnostic locally, retaining every other warning.
        .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
        .Options;

    private sealed record MixedTextResult(
        IModel Runtime,
        IModel Design,
        string Sql,
        ConnectionState State
    );

    private sealed class AlternateCachedTextContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public AlternateCachedTextContext(
            DbContextOptions<AlternateCachedTextContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder
            .Entity<CachedTextRow>()
            .ToTable("AlternateCachedTextRows");
    }

    private sealed class MixedTextConnectionAttempts : DbConnectionInterceptor
    {
        private int _openAttempts;
        internal int OpenAttempts => Volatile.Read(ref _openAttempts);

        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result
        )
        {
            Interlocked.Increment(ref _openAttempts);

            throw new InvalidOperationException("Mixed text-model initialization must not open a database connection.");
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _openAttempts);

            return ValueTask.FromException<InterceptionResult>(
                new InvalidOperationException("Mixed text-model initialization must not open a database connection."));
        }
    }

    /// <summary>Provides one cacheable context type for lifetime and concurrency controls.</summary>
    public sealed class CachedTextContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        /// <param name="options">The provider options.</param>
        public CachedTextContext(
            DbContextOptions<CachedTextContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder
            .Entity<CachedTextRow>()
            .HasKey(row => row.Id);
    }

    private sealed class CachedTextRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
