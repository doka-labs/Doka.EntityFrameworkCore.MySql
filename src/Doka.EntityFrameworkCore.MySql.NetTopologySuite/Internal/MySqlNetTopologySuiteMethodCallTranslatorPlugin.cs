namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlNetTopologySuiteMethodCallTranslatorPlugin : IMethodCallTranslatorPlugin
{
    public MySqlNetTopologySuiteMethodCallTranslatorPlugin(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource,
        ILoggerFactory loggerFactory,
        IEnumerable<ISingletonOptions> singletonOptions
    )
    {
        ArgumentNullException.ThrowIfNull(sqlExpressionFactory);
        ArgumentNullException.ThrowIfNull(typeMappingSource);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(singletonOptions);

        var profile = singletonOptions
                .OfType<MySqlSingletonOptions>()
                .Single()
                .Profile
            ?? throw new InvalidOperationException("A configured server profile is required for spatial translation.");

        Translators =
        [
            new MySqlNetTopologySuiteMethodCallTranslator(
                sqlExpressionFactory,
                typeMappingSource,
                loggerFactory.CreateLogger(MySqlLoggerCategory.Spatial),
                profile.Engine.Has(EngineCapability.MariaDbSpatialSemantics),
                profile.Engine.Has(EngineCapability.SpatialBufferStrategies)),
        ];
    }

    public IEnumerable<IMethodCallTranslator> Translators { get; }
}
