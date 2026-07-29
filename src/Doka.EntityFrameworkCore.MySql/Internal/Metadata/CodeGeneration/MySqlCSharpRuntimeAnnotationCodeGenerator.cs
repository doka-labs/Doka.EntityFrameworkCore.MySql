// File-local using: RelationalCSharpRuntimeAnnotationCodeGenerator and its dependencies live
// in the Design.Internal namespace and are referenced by only two src files (this one plus
// MySqlServiceCollectionExtensions). Keeping the import file-local avoids polluting the
// global-using surface with a namespace the rest of the codebase does not touch.
using Microsoft.EntityFrameworkCore.Design.Internal;

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
