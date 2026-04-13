namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlNetTopologySuiteMemberTranslatorPlugin : IMemberTranslatorPlugin
{
    public MySqlNetTopologySuiteMemberTranslatorPlugin(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource,
        ILoggerFactory loggerFactory
    )
    {
        ArgumentNullException.ThrowIfNull(sqlExpressionFactory);
        ArgumentNullException.ThrowIfNull(typeMappingSource);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        Translators =
        [
            new MySqlNetTopologySuiteMemberTranslator(
                sqlExpressionFactory,
                typeMappingSource,
                loggerFactory.CreateLogger(MySqlLoggerCategory.Spatial)),
        ];
    }

    public IEnumerable<IMemberTranslator> Translators { get; }
}
