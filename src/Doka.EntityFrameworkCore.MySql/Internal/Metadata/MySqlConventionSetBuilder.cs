namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlConventionSetBuilder : RelationalConventionSetBuilder
{
    private readonly IEnumerable<ISingletonOptions> _singletonOptions;

    public MySqlConventionSetBuilder(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies,
        IEnumerable<ISingletonOptions> singletonOptions
    ) : base(dependencies, relationalDependencies)
    {
        _singletonOptions = singletonOptions ?? throw new ArgumentNullException(nameof(singletonOptions));
    }

    public override ConventionSet CreateConventionSet()
    {
        var conventionSet = base.CreateConventionSet();

        conventionSet.ModelFinalizingConventions.Add(new MySqlValueGenerationConvention(_singletonOptions));

        return conventionSet;
    }
}
