namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlProviderConfigurationCodeGenerator : ProviderCodeGenerator
{
    private readonly MySqlScaffoldingContext _scaffoldingContext;

    public MySqlProviderConfigurationCodeGenerator(
        ProviderCodeGeneratorDependencies dependencies,
        MySqlScaffoldingContext scaffoldingContext
    ) : base(dependencies)
    {
        _scaffoldingContext = scaffoldingContext ?? throw new ArgumentNullException(nameof(scaffoldingContext));
    }

    public override MethodCallCodeFragment GenerateUseProvider(
        string connectionString,
        MethodCallCodeFragment? providerOptions
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var scaffoldingState = _scaffoldingContext.Consume();
        var detectedServerVersionText = scaffoldingState.DetectedServerVersionText;
        var providerOptionsWithSpatial = providerOptions;

        if (string.IsNullOrWhiteSpace(detectedServerVersionText))
        {
            throw new InvalidOperationException(
                "MySQL reverse engineering requires a detected server version "
                + "before provider configuration code can be generated.");
        }

        if (scaffoldingState is { UsesNetTopologySuiteScaffolding: true })
        {
            providerOptionsWithSpatial = providerOptionsWithSpatial is null
                ? new MethodCallCodeFragment("UseNetTopologySuite", Array.Empty<object>())
                : providerOptionsWithSpatial.Chain("UseNetTopologySuite", Array.Empty<object>());
        }

        var detectedServerVersion = MySqlServerVersion.AutoDetect(detectedServerVersionText);

        // Preserve unsupported scaffolding as an explicit generated-code decision.
        // The code literal includes AllowUnsupported, so the compatibility risk is
        // visible in the generated DbContext and runtime diagnostics emit a warning.
        var serverVersion = detectedServerVersion.SupportStatus == MySqlServerVersionSupportStatus.Supported
            ? detectedServerVersion
            : detectedServerVersion.IsMariaDb
                ? MySqlServerVersion.MariaDb(
                    detectedServerVersion.Version,
                    MySqlServerVersionCompatibilityMode.AllowUnsupported)
                : MySqlServerVersion.MySql(
                    detectedServerVersion.Version,
                    MySqlServerVersionCompatibilityMode.AllowUnsupported);

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
