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

        var detectedServerVersionText = _scaffoldingContext.DetectedServerVersionText;
        var providerOptionsWithSpatial = providerOptions;

        if (string.IsNullOrWhiteSpace(detectedServerVersionText))
        {
            throw new InvalidOperationException(
                "MySQL reverse engineering requires a detected server version before provider configuration code can be generated.");
        }

        if (_scaffoldingContext.UsesNetTopologySuiteScaffolding)
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
