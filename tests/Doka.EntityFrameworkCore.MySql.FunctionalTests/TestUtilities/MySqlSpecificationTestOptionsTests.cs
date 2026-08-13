using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the internal-service-provider isolation policy used by contracts
/// that intentionally construct a different EF Core service graph per case.
/// </summary>
public sealed class MySqlSpecificationTestOptionsTests
{
    /// <summary>
    /// Verifies that isolated providers remain transient while only their
    /// expected process-wide threshold diagnostic is relaxed.
    /// </summary>
    [Fact]
    public void Transient_provider_policy_is_narrow_and_does_not_cache_providers()
    {
        var optionsBuilder = new DbContextOptionsBuilder()
            .ConfigureWarnings(warnings => warnings.Default(WarningBehavior.Throw))
            .UseTransientInternalServiceProvider();
        MySqlTestHelpers.Instance.UseProviderOptions(optionsBuilder);

        using var firstContext = new DbContext(optionsBuilder.Options);
        using var secondContext = new DbContext(optionsBuilder.Options);
        var firstProvider = ((IInfrastructure<IServiceProvider>)firstContext).Instance;
        var secondProvider = ((IInfrastructure<IServiceProvider>)secondContext).Instance;
        var loggingOptions = firstProvider.GetRequiredService<ILoggingOptions>();

        Assert.NotSame(firstProvider, secondProvider);
        Assert.Equal(
            WarningBehavior.Log,
            loggingOptions.WarningsConfiguration.GetBehavior(CoreEventId.ManyServiceProvidersCreatedWarning));
        Assert.Equal(
            WarningBehavior.Throw,
            loggingOptions.WarningsConfiguration.GetBehavior(CoreEventId.DetachedLazyLoadingWarning));
    }
}
