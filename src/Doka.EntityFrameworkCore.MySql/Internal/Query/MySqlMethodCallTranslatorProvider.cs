namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlMethodCallTranslatorProvider : RelationalMethodCallTranslatorProvider
{
    public MySqlMethodCallTranslatorProvider(
        RelationalMethodCallTranslatorProviderDependencies dependencies
    ) : base(dependencies)
    {
        var sqlExpressionFactory = dependencies.SqlExpressionFactory;

        AddTranslators(
            new IMethodCallTranslator[]
            {
                new MySqlObjectToStringTranslator(sqlExpressionFactory, dependencies.RelationalTypeMappingSource),
                new MySqlMethodCallTranslator(sqlExpressionFactory),
            });
    }
}
