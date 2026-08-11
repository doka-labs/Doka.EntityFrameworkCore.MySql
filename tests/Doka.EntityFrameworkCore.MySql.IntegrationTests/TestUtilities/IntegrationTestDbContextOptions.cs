namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Creates context options for the complete, multi-profile integration suite.
/// </summary>
internal static class IntegrationTestDbContextOptions
{
    /// <summary>
    /// Preserves EF Core's service-provider diagnostic as a log event when all
    /// supported engine profiles execute in one test process.
    /// </summary>
    /// <remarks>
    /// Each engine profile intentionally builds its own provider service graph.
    /// Rider therefore crosses EF Core's generic service-provider threshold
    /// when it runs the complete matrix in one process. Logging this one event
    /// keeps the diagnostic observable without making test order decide which
    /// otherwise valid profile fails. Other warning behaviors remain unchanged.
    /// </remarks>
    public static DbContextOptionsBuilder<TContext> Create<TContext>()
        where TContext : DbContext
    {
        return new DbContextOptionsBuilder<TContext>().ConfigureWarnings(warnings =>
            warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning));
    }
}
