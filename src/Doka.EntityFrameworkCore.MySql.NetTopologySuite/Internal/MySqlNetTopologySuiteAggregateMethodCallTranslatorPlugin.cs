namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Exposes the MySQL-family spatial aggregate translators to EF Core.
/// </summary>
internal sealed class MySqlNetTopologySuiteAggregateMethodCallTranslatorPlugin : IAggregateMethodCallTranslatorPlugin
{
    public MySqlNetTopologySuiteAggregateMethodCallTranslatorPlugin(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource,
        ILoggerFactory loggerFactory,
        IEnumerable<ISingletonOptions> singletonOptions
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(singletonOptions);

        var profile = singletonOptions
                .OfType<MySqlSingletonOptions>()
                .Single()
                .Profile
            ?? throw new InvalidOperationException("A configured server profile is required for spatial translation.");

        Translators =
        [
            new MySqlNetTopologySuiteAggregateMethodTranslator(
                sqlExpressionFactory,
                typeMappingSource,
                loggerFactory.CreateLogger(MySqlLoggerCategory.Spatial),
                profile.Engine.Has(EngineCapability.SpatialCollectAggregate)),
        ];
    }

    public IEnumerable<IAggregateMethodCallTranslator> Translators { get; }
}
