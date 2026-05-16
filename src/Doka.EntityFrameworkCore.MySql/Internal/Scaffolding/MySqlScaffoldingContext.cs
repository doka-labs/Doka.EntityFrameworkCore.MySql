namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Carries cross-service scaffolding state for a single design-time reverse-engineering
/// invocation. The two fields cross three EF Core service boundaries -- written by
/// <see cref="MySqlDatabaseModelFactory"/> and <see cref="MySqlScaffoldingModelFactory"/>,
/// read by <see cref="MySqlProviderConfigurationCodeGenerator"/> -- which cannot share
/// the in-flight <c>IModel</c> because the EF Core
/// <c>ProviderCodeGenerator.GenerateUseProvider</c> contract takes only the connection
/// string and an optional provider-options fragment. Until that contract surfaces a
/// model parameter the cross-service hand-off lives here.
///
/// The class is registered as a DI singleton because the dotnet-ef tooling resolves
/// every design-time service from the same provider per scaffolding invocation.
/// <see cref="Begin"/> is invoked once at the start of every reverse-engineering pass
/// so the state from a prior invocation does not leak into the next one.
/// </summary>
internal sealed class MySqlScaffoldingContext
{
    public string? DetectedServerVersionText { get; private set; }

    public bool UsesNetTopologySuiteScaffolding { get; private set; }

    /// <summary>
    /// Begins a new scaffolding invocation. Clears all cross-service state so the
    /// previous invocation cannot leak into the new one. Callers invoke this exactly
    /// once at the start of every reverse-engineering pass before any writer touches
    /// the context.
    /// </summary>
    public void Begin()
    {
        DetectedServerVersionText = null;
        UsesNetTopologySuiteScaffolding = false;
    }

    public void SetDetectedServerVersionText(
        string? detectedServerVersionText
    ) => DetectedServerVersionText = detectedServerVersionText;

    public void SetUsesNetTopologySuiteScaffolding(
        bool usesNetTopologySuiteScaffolding
    ) => UsesNetTopologySuiteScaffolding = usesNetTopologySuiteScaffolding;

    public void MarkUsesNetTopologySuiteScaffolding() => UsesNetTopologySuiteScaffolding = true;
}
