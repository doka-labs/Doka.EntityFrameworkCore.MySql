namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed class GenericLikeBenchmarkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compilation_repeats_on_the_initialized_context_without_rebuilding_services(
        bool isMariaDb
    )
    {
        var events = new List<EventData>();
        using var benchmark = new Benchmarks.GenericLikeBenchmark();
        benchmark.Initialize(CreateOptions(isMariaDb, events));
        var contexts = CaptureInitializedContexts(events);
        events.Clear();

        DbContext? compilationContext = null;
        for (var iteration = 0; iteration < 4; iteration++)
        {
            var sql = benchmark.CompileScalarQueryToQueryString();

            Assert.False(string.IsNullOrWhiteSpace(sql));
            var compilation = Assert.IsType<QueryExpressionEventData>(Assert.Single(events));
            Assert.Equal(CoreEventId.QueryCompilationStarting, compilation.EventId);
            var context = Assert.Single(contexts, snapshot => ReferenceEquals(snapshot.Context, compilation.Context));
            compilationContext ??= context.Context;
            Assert.Same(compilationContext, compilation.Context);
            Assert.All(contexts, snapshot => snapshot.AssertUnchanged());
            events.Clear();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cached_query_is_warmed_before_the_first_measured_call(
        bool isMariaDb
    )
    {
        var events = new List<EventData>();
        using var benchmark = new Benchmarks.GenericLikeBenchmark();
        benchmark.Initialize(CreateOptions(isMariaDb, events));
        var contexts = CaptureInitializedContexts(events);
        events.Clear();

        var expectedSql = benchmark.CachedScalarQueryToQueryString();
        Assert.False(string.IsNullOrWhiteSpace(expectedSql));
        Assert.Empty(events);

        for (var iteration = 0; iteration < 4; iteration++)
        {
            Assert.Equal(expectedSql, benchmark.CachedScalarQueryToQueryString());
            Assert.Empty(events);
            Assert.All(contexts, snapshot => snapshot.AssertUnchanged());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Alternating_compilation_and_cached_calls_preserves_full_SQL_and_the_warm_cache(
        bool isMariaDb
    )
    {
        var events = new List<EventData>();
        using var benchmark = new Benchmarks.GenericLikeBenchmark();
        benchmark.Initialize(CreateOptions(isMariaDb, events));
        events.Clear();

        var expectedSql = benchmark.CachedScalarQueryToQueryString();
        Assert.Empty(events);
        Assert.Contains("FROM `TranslationEntities`", expectedSql, StringComparison.Ordinal);
        Assert.Contains("`SignedValue` LIKE @numberPattern", expectedSql, StringComparison.Ordinal);
        Assert.Contains("`CreatedAt` LIKE @datePattern", expectedSql, StringComparison.Ordinal);
        Assert.Contains("ESCAPE @escape", expectedSql, StringComparison.Ordinal);
        Assert.Contains("LOWER(CONCAT(", expectedSql, StringComparison.Ordinal);
        Assert.Contains("HEX(", expectedSql, StringComparison.Ordinal);
        Assert.Contains("`Token`", expectedSql, StringComparison.Ordinal);
        Assert.Contains("LIKE @guidPattern", expectedSql, StringComparison.Ordinal);

        for (var iteration = 0; iteration < 4; iteration++)
        {
            Assert.Equal(expectedSql, benchmark.CompileScalarQueryToQueryString());
            Assert.Equal(CoreEventId.QueryCompilationStarting, Assert.Single(events).EventId);
            events.Clear();

            Assert.Equal(expectedSql, benchmark.CachedScalarQueryToQueryString());
            Assert.Empty(events);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cleanup_disposes_both_initialized_contexts(
        bool isMariaDb
    )
    {
        var events = new List<EventData>();
        using var benchmark = new Benchmarks.GenericLikeBenchmark();
        benchmark.Initialize(CreateOptions(isMariaDb, events));
        var contexts = CaptureInitializedContexts(events);
        events.Clear();

        benchmark.Dispose();

        Assert.Equal(contexts.Length, events.Count);
        foreach (var snapshot in contexts)
        {
            var disposed = Assert.Single(events.OfType<DbContextEventData>(), eventData =>
                ReferenceEquals(eventData.Context, snapshot.Context));
            Assert.Equal(CoreEventId.ContextDisposed, disposed.EventId);
            Assert.Throws<ObjectDisposedException>(() => snapshot.Context.Model);
        }
    }

    [Fact]
    public void Exactly_two_string_benchmarks_are_bound_to_their_allocation_controls()
    {
        var benchmarkMethods = typeof(Benchmarks.GenericLikeBenchmark)
            .GetMethods()
            .Where(method => method.IsDefined(typeof(BenchmarkDotNet.Attributes.BenchmarkAttribute), inherit: true))
            .ToArray();
        var expectedMethods = new[]
        {
            nameof(Benchmarks.GenericLikeBenchmark.CachedScalarQueryToQueryString),
            nameof(Benchmarks.GenericLikeBenchmark.CompileScalarQueryToQueryString),
        };

        Assert.Equal(
            expectedMethods.Order(StringComparer.Ordinal),
            benchmarkMethods.Select(method => method.Name).Order(StringComparer.Ordinal));
        Assert.All(benchmarkMethods, method =>
        {
            Assert.Equal(typeof(string), method.ReturnType);
            Assert.Empty(method.GetParameters());
        });

        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "performance-contract.json")));
        var controls = document.RootElement
            .GetProperty("benchmarkDotNetControls")
            .EnumerateArray()
            .Where(control => control.GetProperty("type").GetString() == nameof(Benchmarks.GenericLikeBenchmark))
            .ToArray();

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

    private static DbContextOptions<Benchmarks.BenchmarkContext> CreateOptions(
        bool isMariaDb,
        List<EventData> events
    )
    {
        var serverVersion = isMariaDb
            ? MySqlServerVersion.MariaDb(new Version(11, 4, 0))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

        return new DbContextOptionsBuilder<Benchmarks.BenchmarkContext>()
            .UseMySql(
                "Server=127.0.0.1;Database=benchmark_measurement_tests;",
                serverVersion,
                options => options.UseNetTopologySuite())
            .LogTo(
                (eventId, _) => eventId == CoreEventId.QueryCompilationStarting
                    || eventId == CoreEventId.ContextInitialized
                    || eventId == CoreEventId.ContextDisposed,
                events.Add)
            .Options;
    }

    private static ContextSnapshot[] CaptureInitializedContexts(
        List<EventData> events
    )
    {
        var contexts = events
            .OfType<ContextInitializedEventData>()
            .Select(eventData => Assert.IsType<Benchmarks.BenchmarkContext>(eventData.Context))
            .Select(context => new ContextSnapshot(
                context,
                context.GetInfrastructure(),
                context.Model,
                context.GetService<IAsyncQueryProvider>()))
            .ToArray();

        Assert.Equal(2, contexts.Length);
        Assert.NotSame(contexts[0].Context, contexts[1].Context);
        Assert.NotSame(contexts[0].ServiceProvider, contexts[1].ServiceProvider);
        Assert.NotSame(contexts[0].QueryProvider, contexts[1].QueryProvider);
        Assert.Same(contexts[0].Model, contexts[1].Model);
        return contexts;
    }

    private static void AssertControlBinding(
        JsonElement[] controls,
        string controlId,
        string methodName
    )
    {
        var control = Assert.Single(controls, entry => entry.GetProperty("id").GetString() == controlId);

        Assert.Equal(methodName, control.GetProperty("method").GetString());
        Assert.Equal("allocatedBytes", control.GetProperty("metric").GetString());
    }

    private sealed record ContextSnapshot(
        Benchmarks.BenchmarkContext Context,
        IServiceProvider ServiceProvider,
        IModel Model,
        IAsyncQueryProvider QueryProvider
    )
    {
        public void AssertUnchanged()
        {
            Assert.Same(ServiceProvider, Context.GetInfrastructure());
            Assert.Same(Model, Context.Model);
            Assert.Same(QueryProvider, Context.GetService<IAsyncQueryProvider>());
        }
    }
}
