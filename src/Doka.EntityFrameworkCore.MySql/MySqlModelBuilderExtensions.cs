namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Adds provider-specific model-level configuration extensions for MySQL-family metadata.
/// </summary>
public static class MySqlModelBuilderExtensions
{
    /// <summary>
    /// Configures the default database character set for the model.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="charSet">The database character set.</param>
    /// <returns>The same <see cref="ModelBuilder"/> instance.</returns>
    public static ModelBuilder HasCharSet(
        this ModelBuilder modelBuilder,
        string charSet
    )
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(charSet);

        modelBuilder.Model.SetMySqlCharSet(charSet);

        return modelBuilder;
    }
}
