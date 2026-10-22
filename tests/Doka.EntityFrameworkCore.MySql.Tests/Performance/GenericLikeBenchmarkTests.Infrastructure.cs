namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class GenericLikeBenchmarkTests
{
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

    /// <summary>
    /// Captures the two independently initialized contexts and validates the benchmark fixture's shared-model boundary.
    /// </summary>
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

    /// <summary>
    /// Holds the context-owned references needed to detect hidden service reconstruction during measured calls.
    /// </summary>
    private sealed record ContextSnapshot(
        Benchmarks.BenchmarkContext Context,
        IServiceProvider ServiceProvider,
        IModel Model,
        IAsyncQueryProvider QueryProvider
    );

    /// <summary>
    /// Captures references immediately after one call so later iterations cannot hide a temporary replacement.
    /// </summary>
    private static ContextSnapshot[] CaptureCurrentContexts(
        ContextSnapshot[] contexts
    ) => contexts.Select(snapshot => new ContextSnapshot(
            snapshot.Context,
            snapshot.Context.GetInfrastructure(),
            snapshot.Context.Model,
            snapshot.Context.GetService<IAsyncQueryProvider>()))
        .ToArray();

    /// <summary>
    /// Verifies one observation retains every context and service reference from initialization.
    /// </summary>
    private static void AssertContextsUnchanged(
        ContextSnapshot[] expected,
        ContextSnapshot[] actual
    )
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Same(expected[index].Context, actual[index].Context);
            Assert.Same(expected[index].ServiceProvider, actual[index].ServiceProvider);
            Assert.Same(expected[index].Model, actual[index].Model);
            Assert.Same(expected[index].QueryProvider, actual[index].QueryProvider);
        }
    }
}
