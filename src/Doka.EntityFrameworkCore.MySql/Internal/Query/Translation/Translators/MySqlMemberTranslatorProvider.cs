namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlMemberTranslatorProvider : RelationalMemberTranslatorProvider
{
    public MySqlMemberTranslatorProvider(
        RelationalMemberTranslatorProviderDependencies dependencies
    ) : base(dependencies)
    {
        var sqlExpressionFactory = dependencies.SqlExpressionFactory;

        AddTranslators(
            new IMemberTranslator[]
            {
                new MySqlMemberTranslator(sqlExpressionFactory),
            });
    }
}
