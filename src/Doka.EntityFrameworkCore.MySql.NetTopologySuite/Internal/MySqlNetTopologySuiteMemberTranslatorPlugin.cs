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

        var profile = singletonOptions
                .OfType<MySqlSingletonOptions>()
                .Single()
                .Profile
            ?? throw new InvalidOperationException("A configured server profile is required for spatial translation.");

        Translators =
        [
            new MySqlNetTopologySuiteMemberTranslator(
                sqlExpressionFactory,
                typeMappingSource,
                loggerFactory.CreateLogger(MySqlLoggerCategory.Spatial),
                profile.Engine.Has(EngineCapability.MariaDbSpatialSemantics),
                profile.Engine.Has(EngineCapability.SpatialIsValidFunction)),
        ];
    }

    public IEnumerable<IMemberTranslator> Translators { get; }
}
