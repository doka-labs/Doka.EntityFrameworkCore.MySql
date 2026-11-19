namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Preserves minimal text-default compatibility evidence in runtime and supplied models.</summary>
internal sealed class MySqlTextRuntimeModelConvention : RelationalRuntimeModelConvention
{
    /// <summary>Reuses EF's runtime-model construction while retaining the provider's minimal text receipts.</summary>
    public MySqlTextRuntimeModelConvention(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies
    ) : base(dependencies, relationalDependencies) { }

    /// <inheritdoc />
    protected override void ProcessPropertyAnnotations(
        Dictionary<string, object?> annotations,
        IProperty property,
        RuntimeProperty runtimeProperty,
        bool runtime
    )
    {
        base.ProcessPropertyAnnotations(annotations, property, runtimeProperty, runtime);

        if (!runtime)
        {
            MySqlRuntimeColumnTextFacets.AddRuntimeAnnotations(annotations, property, property);
        }
    }

    /// <inheritdoc />
    protected override void ProcessPropertyOverridesAnnotations(
        Dictionary<string, object?> annotations,
        IRelationalPropertyOverrides propertyOverrides,
        RuntimeRelationalPropertyOverrides runtimePropertyOverrides,
        bool runtime
    )
    {
        base.ProcessPropertyOverridesAnnotations(annotations, propertyOverrides, runtimePropertyOverrides, runtime);

        if (!runtime)
        {
            MySqlRuntimeColumnTextFacets.AddRuntimeAnnotations(annotations,
                propertyOverrides,
                propertyOverrides.Property);
        }
    }

    /// <inheritdoc />
    protected override void ProcessModelAnnotations(
        Dictionary<string, object?> annotations,
        IModel model,
        RuntimeModel runtimeModel,
        bool runtime
    )
    {
        base.ProcessModelAnnotations(annotations, model, runtimeModel, runtime);

        if (!runtime)
        {
            MySqlTextDefaults.AddRuntimeAnnotations(annotations, model);
        }
    }
}
