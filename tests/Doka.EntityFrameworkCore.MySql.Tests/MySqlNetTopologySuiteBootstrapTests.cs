namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers the optional NetTopologySuite bootstrap contract.
/// </summary>
public sealed class MySqlNetTopologySuiteBootstrapTests
{
    /// <summary>
    /// Verifies that the approved spatial seam adds its own options extension.
    /// </summary>
    [Fact]
    public void UseNetTopologySuite_adds_the_optional_spatial_extension()
    {
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => options.UseNetTopologySuite());

        var extension = builder.Options.FindExtension<MySqlNetTopologySuiteOptionsExtension>();

        Assert.NotNull(extension);
    }

    /// <summary>
    /// Verifies that the spatial seam preserves the same builder instance for chaining.
    /// </summary>
    [Fact]
    public void UseNetTopologySuite_returns_the_same_builder_instance()
    {
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        MySqlDbContextOptionsBuilder? returnedBuilder = null;

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => returnedBuilder = options.UseNetTopologySuite());

        Assert.NotNull(returnedBuilder);
    }

    /// <summary>
    /// Verifies that the main provider assembly stays free of direct NetTopologySuite references.
    /// </summary>
    [Fact]
    public void Main_provider_assembly_does_not_reference_nettopologysuite()
    {
        var referencedAssemblies = typeof(MySqlDbContextOptionsBuilder).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            referencedAssemblies,
            assemblyName => string.Equals(assemblyName.Name, "NetTopologySuite", StringComparison.Ordinal));
    }
}
