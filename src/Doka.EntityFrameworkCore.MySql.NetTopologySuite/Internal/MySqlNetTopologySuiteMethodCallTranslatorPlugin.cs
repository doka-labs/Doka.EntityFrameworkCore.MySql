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

        var supportsMariaDbSpatialFunctions = singletonOptions
            .OfType<MySqlSingletonOptions>()
            .Single()
            .Profile?.Family == EngineFamily.MariaDb;

        Translators =
        [
            new MySqlNetTopologySuiteMethodCallTranslator(
                sqlExpressionFactory,
                typeMappingSource,
                loggerFactory.CreateLogger(MySqlLoggerCategory.Spatial),
                supportsMariaDbSpatialFunctions),
        ];
    }

    public IEnumerable<IMethodCallTranslator> Translators { get; }
}
