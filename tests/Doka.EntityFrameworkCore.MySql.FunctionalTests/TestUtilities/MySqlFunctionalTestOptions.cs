namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Configures EF Core options for functional tests that intentionally create
/// their own internal service-provider graph.
/// </summary>
internal static class MySqlFunctionalTestOptions
{
    /// <summary>
    /// Creates a typed options builder whose internal provider remains local
    /// to the context that consumes it.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> CreateTransientBuilder<TContext>()
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        _ = builder.UseTransientInternalServiceProvider();

        return builder;
    }

    /// <summary>
    /// Keeps an intentionally transient provider out of EF Core's global cache
    /// and makes its expected isolation diagnostic independent of test order.
    /// </summary>
    /// <remarks>
    /// Disabling caching prevents these providers from increasing the global
    /// provider count. EF Core still evaluates the existing process-wide count
    /// while it builds a transient provider, so the diagnostic must also be
    /// logged locally once another test has already crossed the threshold.
    /// Every other warning retains the functional suite's strict behavior.
    /// </remarks>
    public static DbContextOptionsBuilder UseTransientInternalServiceProvider(
        this DbContextOptionsBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning));
    }
}
