namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlNetTopologySuiteMethodCallTranslatorPlugin : IMethodCallTranslatorPlugin
{
    public MySqlNetTopologySuiteMethodCallTranslatorPlugin(
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
            new MySqlNetTopologySuiteMethodCallTranslator(
                sqlExpressionFactory,
                typeMappingSource,
                loggerFactory.CreateLogger(MySqlLoggerCategory.Spatial)),
        ];
    }

    public IEnumerable<IMethodCallTranslator> Translators { get; }
}
