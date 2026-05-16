namespace Doka.EntityFrameworkCore.MySql;

#pragma warning disable EF1001 // EF Core internal API surface -- see ADR D-001 for the coupling rationale.

/// <summary>
/// Wraps the registered implementation of an EF Core internal service with a Doka
/// decorator. Concentrates the EF1001 surface in this one helper so a Microsoft
/// patch release that changes the inner-service shape breaks here -- with a clear
/// diagnostic -- instead of silently degrading every consumer that used to inline
/// the same LastOrDefault + ActivatorUtilities.CreateInstance pattern. See ADR
/// D-001 for the full rationale and the re-evaluation triggers.
/// </summary>
internal static class EfCoreServiceDecorator
{
    /// <summary>
    /// Replaces the most-recently-registered <typeparamref name="TService"/> descriptor
    /// with one that resolves the inner instance via the captured descriptor and hands
    /// it to <paramref name="factory"/> for wrapping. The inner descriptor's lifetime
    /// is preserved so the decorator inherits singleton- or scoped-scoping from EF Core.
    /// </summary>
    /// <typeparam name="TService">The EF Core service interface being wrapped.</typeparam>
    /// <typeparam name="TDecorator">The Doka decorator type that wraps the inner instance.</typeparam>
    /// <param name="services">The service collection holding the inner registration.</param>
    /// <param name="factory">
    /// Decorator factory invoked per resolve. Receives the inner instance and the
    /// active service provider; returns the wrapped decorator.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no inner registration is found, or when the captured descriptor
    /// does not expose an instantiable implementation. The diagnostic includes the
    /// service-type name so an EF Core patch that drops or restructures the inner
    /// service surfaces here as a hard failure rather than as a silent no-op decorator.
    /// </exception>
    public static void Decorate<TService, TDecorator>(
        IServiceCollection services,
        Func<TService, IServiceProvider, TDecorator> factory
    )
        where TService : class
        where TDecorator : class, TService
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        var innerDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(TService))
            ?? throw new InvalidOperationException(
                $"No inner '{typeof(TService).Name}' registration was found to decorate. "
                + "The EF Core service graph may have changed; review the decorator setup against "
                + "the current EF Core patch version (see ADR D-001).");

        services.Replace(
            ServiceDescriptor.Describe(
                typeof(TService),
                serviceProvider => factory(ResolveInner<TService>(innerDescriptor, serviceProvider), serviceProvider),
                innerDescriptor.Lifetime));
    }

    private static TService ResolveInner<TService>(
        ServiceDescriptor descriptor,
        IServiceProvider serviceProvider
    )
        where TService : class
    {
        if (descriptor.ImplementationInstance is TService implementationInstance)
        {
            return implementationInstance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (TService)descriptor.ImplementationFactory(serviceProvider);
        }

        if (descriptor.ImplementationType is not null)
        {
            return (TService)ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType);
        }

        throw new InvalidOperationException(
            $"The captured '{typeof(TService).Name}' descriptor did not expose an instantiable implementation. "
            + "This likely indicates an EF Core patch that changed the inner-service descriptor shape "
            + "(see ADR D-001 for the re-evaluation triggers).");
    }
}

#pragma warning restore EF1001
