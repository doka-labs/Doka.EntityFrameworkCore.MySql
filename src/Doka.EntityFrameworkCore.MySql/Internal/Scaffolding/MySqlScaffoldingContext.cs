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
/// The class is registered as a DI singleton because EF Core requires
/// <see cref="IProviderConfigurationCodeGenerator"/> to be a thread-safe singleton.
/// The ambient value is immutable and scoped to the current logical execution context,
/// so concurrent reverse-engineering operations cannot overwrite each other's state.
/// <see cref="Begin"/> starts a fresh state and <see cref="Consume"/> removes it after
/// provider configuration generation. <see cref="Abort"/> provides idempotent cleanup
/// when the enclosing operation exits before code generation.
/// </summary>
internal sealed class MySqlScaffoldingContext
{
    private readonly AsyncLocal<MySqlScaffoldingState?> _current = new();

    /// <summary>
    /// Begins a new scaffolding invocation in the current logical execution context.
    /// </summary>
    public void Begin() => _current.Value = new MySqlScaffoldingState(null, false);

    public void SetDetectedServerVersionText(
        string? detectedServerVersionText
    ) => _current.Value = Current with
    {
        DetectedServerVersionText = detectedServerVersionText,
    };

    public void SetUsesNetTopologySuiteScaffolding(
        bool usesNetTopologySuiteScaffolding
    ) => _current.Value = Current with
    {
        UsesNetTopologySuiteScaffolding = usesNetTopologySuiteScaffolding,
    };

    public void MarkUsesNetTopologySuiteScaffolding() =>
        SetUsesNetTopologySuiteScaffolding(true);

    /// <summary>
    /// Returns and removes the completed state for the current scaffolding operation.
    /// Consuming the state prevents later code-generation calls on the same logical
    /// execution context from reusing stale server or spatial metadata.
    /// </summary>
    public MySqlScaffoldingState Consume()
    {
        var state = Current;
        _current.Value = null;
        return state;
    }

    /// <summary>
    /// Removes any in-flight state from the current logical execution context.
    /// </summary>
    public void Abort() => _current.Value = null;

    private MySqlScaffoldingState Current =>
        _current.Value
        ?? throw new InvalidOperationException(
            "No MySQL scaffolding operation is active in the current execution context.");
}

internal sealed record MySqlScaffoldingState(
    string? DetectedServerVersionText,
    bool UsesNetTopologySuiteScaffolding
);
