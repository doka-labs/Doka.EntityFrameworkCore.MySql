namespace Doka.EntityFrameworkCore.MySql;

#pragma warning disable EF1001
internal sealed class MySqlCSharpRuntimeAnnotationCodeGenerator : RelationalCSharpRuntimeAnnotationCodeGenerator
{
    public MySqlCSharpRuntimeAnnotationCodeGenerator(
        CSharpRuntimeAnnotationCodeGeneratorDependencies dependencies,
        RelationalCSharpRuntimeAnnotationCodeGeneratorDependencies relationalDependencies
    ) : base(dependencies, relationalDependencies) { }

    /// <inheritdoc />
    public override void Generate(
        IProperty property,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters
    )
    {
        if (!parameters.IsRuntime)
        {
            MySqlRuntimeColumnTextFacets.AddRuntimeAnnotations(parameters.Annotations, property, property);
        }

        base.Generate(property, parameters);
    }

    /// <inheritdoc />
    public override void Generate(
        IRelationalPropertyOverrides propertyOverrides,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters
    )
    {
        if (!parameters.IsRuntime)
        {
            MySqlRuntimeColumnTextFacets.AddRuntimeAnnotations(parameters.Annotations,
                propertyOverrides,
                propertyOverrides.Property);
        }

        base.Generate(propertyOverrides, parameters);
    }

    /// <inheritdoc />
    public override void Generate(
        IModel model,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters
    )
    {
        // WHY: Compiled-model generation starts from design metadata rather than executing
        // runtime conventions; emit the same primitive compatibility receipt on this path.
        if (!parameters.IsRuntime)
        {
            MySqlTextDefaults.AddRuntimeAnnotations(parameters.Annotations, model);
        }

        base.Generate(model, parameters);
    }
}
#pragma warning restore EF1001
