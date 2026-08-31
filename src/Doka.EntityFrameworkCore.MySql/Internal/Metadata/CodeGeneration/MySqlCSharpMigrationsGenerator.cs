using Microsoft.EntityFrameworkCore.Migrations.Design;

namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlCSharpMigrationsGenerator : CSharpMigrationsGenerator
{
    public MySqlCSharpMigrationsGenerator(
        MigrationsCodeGeneratorDependencies dependencies,
        CSharpMigrationsGeneratorDependencies csharpDependencies
    ) : base(dependencies, csharpDependencies) { }

    protected override IEnumerable<string> GetNamespaces(
        IModel model
    ) => base
        .GetNamespaces(model)
        .Concat(
            model
                .GetEntityTypes()
                .SelectMany(GetProperties)
                .Where(RequiresModelClrNamespace)
                .Select(property => (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType).Namespace)
                .OfType<string>());

    private static IEnumerable<IProperty> GetProperties(
        ITypeBase typeBase
    )
    {
        foreach (var property in typeBase.GetDeclaredProperties())
        {
            yield return property;
        }

        foreach (var complexProperty in typeBase.GetDeclaredComplexProperties())
        {
            if (complexProperty.IsCollection)
            {
                continue;
            }

            foreach (var property in GetProperties(complexProperty.ComplexType))
            {
                yield return property;
            }
        }
    }

    private static bool RequiresModelClrNamespace(
        IProperty property
    )
    {
        if (property.GetTypeMapping() is IMySqlProviderOwnedModelTypeMapping)
        {
            return true;
        }

        var modelClrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        return modelClrType == typeof(Guid) && property.GetMySqlGuidFormat() is not null;
    }
}
