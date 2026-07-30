namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Adds the optional NetTopologySuite activation seam to <see cref="MySqlDbContextOptionsBuilder" />.
/// </summary>
public static class MySqlNetTopologySuiteDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Enables the optional NetTopologySuite integration package for the current context configuration.
    /// </summary>
    /// <param name="builder">The provider-specific options builder.</param>
    /// <returns>The same <see cref="MySqlDbContextOptionsBuilder" /> instance.</returns>
    public static MySqlDbContextOptionsBuilder UseNetTopologySuite(
        this MySqlDbContextOptionsBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = builder.InfrastructureOptionsBuilder;
        var extension = optionsBuilder.Options.FindExtension<MySqlNetTopologySuiteOptionsExtension>()
            ?? new MySqlNetTopologySuiteOptionsExtension();

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        return builder;
    }
}
