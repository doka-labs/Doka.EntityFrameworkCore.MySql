namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlConventionSetBuilder : RelationalConventionSetBuilder
{
    /// <summary>
    /// Maximum identifier length accepted by both supported database families. EF Core
    /// applies deterministic truncation and collision suffixes before sending DDL.
    /// </summary>
    internal const int MaxIdentifierLength = 64;

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

        conventionSet.Add(
            new RelationalMaxIdentifierLengthConvention(
                MaxIdentifierLength,
                Dependencies,
                RelationalDependencies));
        conventionSet.ModelFinalizingConventions.Add(new MySqlValueGenerationConvention(_singletonOptions));

        return conventionSet;
    }
}
