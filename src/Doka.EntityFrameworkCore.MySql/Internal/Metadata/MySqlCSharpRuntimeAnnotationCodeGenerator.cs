namespace Doka.EntityFrameworkCore.MySql;

#pragma warning disable EF1001
internal sealed class MySqlCSharpRuntimeAnnotationCodeGenerator : RelationalCSharpRuntimeAnnotationCodeGenerator
{
    public MySqlCSharpRuntimeAnnotationCodeGenerator(
        CSharpRuntimeAnnotationCodeGeneratorDependencies dependencies,
        RelationalCSharpRuntimeAnnotationCodeGeneratorDependencies relationalDependencies
    ) : base(dependencies, relationalDependencies) { }
}
#pragma warning restore EF1001
