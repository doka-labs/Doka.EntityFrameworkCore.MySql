namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Creates command builders without capturing a scoped context or connection.</summary>
internal sealed class MySqlRelationalCommandBuilderFactory : RelationalCommandBuilderFactory
{
    /// <summary>Initializes the immutable dependencies for this operation.</summary>
    public MySqlRelationalCommandBuilderFactory(
        RelationalCommandBuilderDependencies dependencies
    ) : base(dependencies) { }

    /// <inheritdoc />
    public override IRelationalCommandBuilder Create() => new MySqlRelationalCommandBuilder(Dependencies);

    /// <summary>Creates the ordinary migration-only factory over the same framework logging and binding dependencies.</summary>
    internal RelationalCommandBuilderFactory CreateMigrationCommandBuilderFactory() => new(Dependencies);
}
