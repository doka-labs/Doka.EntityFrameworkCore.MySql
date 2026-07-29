namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlMethodCallTranslatorProvider : RelationalMethodCallTranslatorProvider
{
    public MySqlMethodCallTranslatorProvider(
        RelationalMethodCallTranslatorProviderDependencies dependencies
    ) : base(dependencies)
    {
        var sqlExpressionFactory = dependencies.SqlExpressionFactory;
        var typeMappingSource = dependencies.RelationalTypeMappingSource;
        var objectToStringTranslator = new MySqlObjectToStringTranslator(sqlExpressionFactory, typeMappingSource);

        AddTranslators(
            new IMethodCallTranslator[]
            {
                new MySqlByteArrayMethodTranslator(sqlExpressionFactory),
                new MySqlConvertMethodTranslator(sqlExpressionFactory, typeMappingSource, objectToStringTranslator),
                new MySqlGuidMethodTranslator(sqlExpressionFactory, typeMappingSource),
                new MySqlMathMethodTranslator(sqlExpressionFactory),
                new MySqlStringMethodTranslator(sqlExpressionFactory),
                new MySqlTemporalMethodCallTranslator(sqlExpressionFactory, typeMappingSource),
                objectToStringTranslator,
                new MySqlMethodCallTranslator(sqlExpressionFactory),
            });
    }
}
