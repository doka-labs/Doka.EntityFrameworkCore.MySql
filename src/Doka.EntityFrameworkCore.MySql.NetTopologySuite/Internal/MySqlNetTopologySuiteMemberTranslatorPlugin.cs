namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlNetTopologySuiteMemberTranslatorPlugin : IMemberTranslatorPlugin
{
    public MySqlNetTopologySuiteMemberTranslatorPlugin(
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
            new MySqlNetTopologySuiteMemberTranslator(
                sqlExpressionFactory,
                typeMappingSource,
                loggerFactory.CreateLogger(MySqlLoggerCategory.Spatial),
                supportsMariaDbSpatialFunctions),
        ];
    }

    public IEnumerable<IMemberTranslator> Translators { get; }
}
