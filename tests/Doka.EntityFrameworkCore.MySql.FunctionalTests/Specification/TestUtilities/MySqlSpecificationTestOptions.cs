namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Configures EF Core options for specification contracts that intentionally
/// construct a distinct internal service provider for each scenario.
/// </summary>
internal static class MySqlSpecificationTestOptions
{
    /// <summary>
    /// Keeps an intentionally transient provider out of EF Core's global cache
    /// and makes its expected isolation diagnostic independent of test order.
    /// </summary>
    /// <remarks>
    /// Disabling caching prevents these providers from increasing the global
    /// provider count. EF Core still evaluates the existing process-wide count
    /// while it builds a transient provider, so the diagnostic must also be
    /// logged locally once another test has already crossed the threshold.
    /// Every other warning retains the specification suite's strict behavior.
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
