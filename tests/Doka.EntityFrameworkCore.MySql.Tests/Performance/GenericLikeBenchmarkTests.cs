namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies generic LIKE benchmark measurements retain their initialized service and query-cache boundaries.
/// </summary>
public sealed partial class GenericLikeBenchmarkTests
{
    /// <summary>
    /// Verifies repeated compilation uses one initialized context and preserves its services.
    /// </summary>
    /// <param name="isMariaDb">Whether to use the MariaDB engine profile.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compilation_repeats_on_the_initialized_context_without_rebuilding_services(
        bool isMariaDb
    )
    {
        // Arrange
        var events = new List<EventData>();
        using var benchmark = new Benchmarks.GenericLikeBenchmark();
        benchmark.Initialize(CreateOptions(isMariaDb, events));
        var contexts = CaptureInitializedContexts(events);
        var observations = new (string Sql, EventData[] Events, ContextSnapshot[] Contexts)[4];
        events.Clear();

        // Act
        // WHY: Repeated calls must compile every time while retaining the initialized service graph.
        for (var iteration = 0; iteration < observations.Length; iteration++)
        {
            var sql = benchmark.CompileScalarQueryToQueryString();
            observations[iteration] = (sql, events.ToArray(), CaptureCurrentContexts(contexts));
            events.Clear();
        }

        // Assert
        DbContext? compilationContext = null;
        Assert.All(observations, observation =>
        {
            Assert.False(string.IsNullOrWhiteSpace(observation.Sql));
            var compilation = Assert.IsType<QueryExpressionEventData>(Assert.Single(observation.Events));
            Assert.Equal(CoreEventId.QueryCompilationStarting, compilation.EventId);
            var context = Assert.Single(contexts, snapshot => ReferenceEquals(snapshot.Context, compilation.Context));
            compilationContext ??= context.Context;
            Assert.Same(compilationContext, compilation.Context);
            AssertContextsUnchanged(contexts, observation.Contexts);
        });
    }

    /// <summary>
    /// Verifies the first measured cached call and later calls require no compilation or service rebuilding.
    /// </summary>
    /// <param name="isMariaDb">Whether to use the MariaDB engine profile.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cached_query_is_warmed_before_the_first_measured_call(
        bool isMariaDb
    )
    {
        // Arrange
        var events = new List<EventData>();
        using var benchmark = new Benchmarks.GenericLikeBenchmark();
        benchmark.Initialize(CreateOptions(isMariaDb, events));
        var contexts = CaptureInitializedContexts(events);
        var observations = new (string Sql, EventData[] Events, ContextSnapshot[] Contexts)[4];
        events.Clear();

        // Act
        var expectedSql = benchmark.CachedScalarQueryToQueryString();
        var initialEvents = events.ToArray();
        for (var iteration = 0; iteration < observations.Length; iteration++)
        {
            var sql = benchmark.CachedScalarQueryToQueryString();
            observations[iteration] = (sql, events.ToArray(), CaptureCurrentContexts(contexts));
        }

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(expectedSql));
        Assert.Empty(initialEvents);
        Assert.All(observations, observation =>
        {
            Assert.Equal(expectedSql, observation.Sql);
            Assert.Empty(observation.Events);
            AssertContextsUnchanged(contexts, observation.Contexts);
        });
    }

    /// <summary>
    /// Verifies alternating fresh compilation and cached execution preserves SQL and cache isolation.
    /// </summary>
    /// <param name="isMariaDb">Whether to use the MariaDB engine profile.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Alternating_compilation_and_cached_calls_preserves_full_SQL_and_the_warm_cache(
        bool isMariaDb
    )
    {
        // Arrange
        var events = new List<EventData>();
        using var benchmark = new Benchmarks.GenericLikeBenchmark();
        benchmark.Initialize(CreateOptions(isMariaDb, events));
        var observations = new (
            string CompiledSql,
            EventData[] CompileEvents,
            string CachedSql,
            EventData[] CacheEvents)[4];

        events.Clear();

        // Act
        var expectedSql = benchmark.CachedScalarQueryToQueryString();
        var initialEvents = events.ToArray();
        // WHY: Alternation catches a fresh compilation accidentally invalidating the independently warmed cache.
        for (var iteration = 0; iteration < observations.Length; iteration++)
        {
            var compiledSql = benchmark.CompileScalarQueryToQueryString();
            var compileEvents = events.ToArray();
            events.Clear();

            var cachedSql = benchmark.CachedScalarQueryToQueryString();
            observations[iteration] = (compiledSql, compileEvents, cachedSql, events.ToArray());
        }

        // Assert
        Assert.Empty(initialEvents);
        Assert.Contains("FROM `TranslationEntities`", expectedSql, StringComparison.Ordinal);
        Assert.Contains("`SignedValue` LIKE @numberPattern", expectedSql, StringComparison.Ordinal);
        Assert.Contains("`CreatedAt` LIKE @datePattern", expectedSql, StringComparison.Ordinal);
        Assert.Contains("ESCAPE @escape", expectedSql, StringComparison.Ordinal);
        Assert.Contains("LOWER(CONCAT(", expectedSql, StringComparison.Ordinal);
        Assert.Contains("HEX(", expectedSql, StringComparison.Ordinal);
        Assert.Contains("`Token`", expectedSql, StringComparison.Ordinal);
        Assert.Contains("LIKE @guidPattern", expectedSql, StringComparison.Ordinal);
        Assert.All(observations, observation =>
        {
            Assert.Equal(expectedSql, observation.CompiledSql);
            Assert.Equal(CoreEventId.QueryCompilationStarting, Assert.Single(observation.CompileEvents).EventId);
            Assert.Equal(expectedSql, observation.CachedSql);
            Assert.Empty(observation.CacheEvents);
        });
    }

    /// <summary>
    /// Verifies cleanup disposes both initialized contexts and closes their public model access.
    /// </summary>
    /// <param name="isMariaDb">Whether to use the MariaDB engine profile.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cleanup_disposes_both_initialized_contexts(
        bool isMariaDb
    )
    {
        // Arrange
        var events = new List<EventData>();
        using var benchmark = new Benchmarks.GenericLikeBenchmark();
        benchmark.Initialize(CreateOptions(isMariaDb, events));
        var contexts = CaptureInitializedContexts(events);
        events.Clear();

        // Act
        benchmark.Dispose();
        var exceptions = contexts
            .Select(snapshot => Record.Exception(() => snapshot.Context.Model))
            .ToArray();

        // Assert
        Assert.Equal(contexts.Length, events.Count);
        for (var index = 0; index < contexts.Length; index++)
        {
            var snapshot = contexts[index];
            var disposed = Assert.Single(events.OfType<DbContextEventData>(), eventData =>
                ReferenceEquals(eventData.Context, snapshot.Context));

            Assert.Equal(CoreEventId.ContextDisposed, disposed.EventId);
            Assert.IsType<ObjectDisposedException>(exceptions[index]);
        }
    }

    /// <summary>
    /// Verifies exactly two string benchmarks are bound to their allocation controls.
    /// </summary>
    [Fact]
    public void Exactly_two_string_benchmarks_are_bound_to_their_allocation_controls()
    {
        // Arrange
        var benchmarkMethods = typeof(Benchmarks.GenericLikeBenchmark)
            .GetMethods()
            .Where(method => method.IsDefined(typeof(BenchmarkDotNet.Attributes.BenchmarkAttribute), inherit: true))
            .ToArray();

        var expectedMethods = new[]
        {
            nameof(Benchmarks.GenericLikeBenchmark.CachedScalarQueryToQueryString),
            nameof(Benchmarks.GenericLikeBenchmark.CompileScalarQueryToQueryString),
        };

        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "performance-contract.json")));

        // Act
        var actual = benchmarkMethods.Select(method => method.Name).Order(StringComparer.Ordinal).ToArray();
        var controls = document.RootElement
            .GetProperty("benchmarkDotNetControls")
            .EnumerateArray()
            .Where(control => control.GetProperty("type").GetString() == nameof(Benchmarks.GenericLikeBenchmark))
            .ToArray();

        // Assert
        Assert.Equal(expectedMethods.Order(StringComparer.Ordinal), actual);
        Assert.All(benchmarkMethods, method =>
        {
            Assert.Equal(typeof(string), method.ReturnType);
            Assert.Empty(method.GetParameters());
        });

        Assert.Equal(expectedMethods.Length, controls.Length);
        AssertControlBinding(
            controls,
            "generic-like-cached-query-allocation",
            nameof(Benchmarks.GenericLikeBenchmark.CachedScalarQueryToQueryString));
        AssertControlBinding(
            controls,
            "generic-like-compilation-allocation",
            nameof(Benchmarks.GenericLikeBenchmark.CompileScalarQueryToQueryString));
    }
}
