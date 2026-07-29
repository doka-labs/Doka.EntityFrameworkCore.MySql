namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlMemberTranslatorProvider : RelationalMemberTranslatorProvider
{
    public MySqlMemberTranslatorProvider(
        RelationalMemberTranslatorProviderDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource
    ) : base(dependencies)
    {
        var sqlExpressionFactory = dependencies.SqlExpressionFactory;

        AddTranslators(
            new IMemberTranslator[]
            {
                new MySqlMemberTranslator(sqlExpressionFactory),
                new MySqlTemporalMemberTranslator(sqlExpressionFactory, typeMappingSource),
            });
    }
}
