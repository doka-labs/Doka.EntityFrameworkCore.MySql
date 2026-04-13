namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlProviderConfigurationCodeGenerator : ProviderCodeGenerator
{
    private readonly MySqlScaffoldingState _scaffoldingState;

    public MySqlProviderConfigurationCodeGenerator(
        ProviderCodeGeneratorDependencies dependencies,
        MySqlScaffoldingState scaffoldingState
    ) : base(dependencies)
    {
        _scaffoldingState = scaffoldingState ?? throw new ArgumentNullException(nameof(scaffoldingState));
    }

    public override MethodCallCodeFragment GenerateUseProvider(
        string connectionString,
        MethodCallCodeFragment? providerOptions
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var detectedServerVersionText = _scaffoldingState.DetectedServerVersionText;
        var providerOptionsWithSpatial = providerOptions;

        if (string.IsNullOrWhiteSpace(detectedServerVersionText))
        {
            throw new InvalidOperationException(
                "MySQL reverse engineering requires a detected server version before provider configuration code can be generated.");
        }

        if (_scaffoldingState.UsesNetTopologySuiteScaffolding)
        {
            providerOptionsWithSpatial = providerOptionsWithSpatial is null
                ? new MethodCallCodeFragment("UseNetTopologySuite", Array.Empty<object>())
                : providerOptionsWithSpatial.Chain("UseNetTopologySuite", Array.Empty<object>());
        }

        var serverVersion = MySqlServerVersion.AutoDetect(detectedServerVersionText);

        return providerOptionsWithSpatial is null
            ? new MethodCallCodeFragment(
                nameof(MySqlDbContextOptionsBuilderExtensions.UseMySql),
                connectionString,
                serverVersion)
            : new MethodCallCodeFragment(
                nameof(MySqlDbContextOptionsBuilderExtensions.UseMySql),
                connectionString,
                serverVersion,
                new NestedClosureCodeFragment("mySqlOptions", providerOptionsWithSpatial));
    }
}
