namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Exposes the MySQL-family spatial aggregate translators to EF Core.
/// </summary>
internal sealed class MySqlNetTopologySuiteAggregateMethodCallTranslatorPlugin : IAggregateMethodCallTranslatorPlugin
{
    public MySqlNetTopologySuiteAggregateMethodCallTranslatorPlugin(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource
    )
    {
        Translators =
        [
            new MySqlNetTopologySuiteAggregateMethodTranslator(sqlExpressionFactory, typeMappingSource),
        ];
    }

    public IEnumerable<IAggregateMethodCallTranslator> Translators { get; }
}
