namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Holds mutable scaffolding-pipeline state. This is a design-time singleton
/// accessed only from the sequential scaffolding flow -- no concurrent access.
/// </summary>
internal sealed class MySqlScaffoldingState
{
    public string? DetectedServerVersionText { get; private set; }

    public bool UsesNetTopologySuiteScaffolding { get; private set; }

    public void SetDetectedServerVersionText(
        string? detectedServerVersionText
    ) => DetectedServerVersionText = detectedServerVersionText;

    public void SetUsesNetTopologySuiteScaffolding(
        bool usesNetTopologySuiteScaffolding
    ) => UsesNetTopologySuiteScaffolding = usesNetTopologySuiteScaffolding;

    public void MarkUsesNetTopologySuiteScaffolding() => UsesNetTopologySuiteScaffolding = true;

    public void Reset()
    {
        DetectedServerVersionText = null;
        UsesNetTopologySuiteScaffolding = false;
    }
}
