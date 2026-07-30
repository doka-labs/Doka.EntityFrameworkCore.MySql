using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Infrastructure;

/// <summary>
/// Gives the imported EF Core specification tests the same deterministic locale
/// used by the upstream provider test projects.
/// </summary>
internal static class TestCultureInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }
}
