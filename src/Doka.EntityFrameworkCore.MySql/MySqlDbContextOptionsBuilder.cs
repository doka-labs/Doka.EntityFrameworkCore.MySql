namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides access to provider-specific configuration options for <c>UseMySql(...)</c>.
/// </summary>
public sealed class MySqlDbContextOptionsBuilder
{
    private readonly DbContextOptionsBuilder _optionsBuilder;

    /// <summary>
    /// The current options extension snapshot. This follows the standard EF Core mutable-extension
    /// pattern: the initial constructor value is a snapshot that is replaced on the first fluent
    /// call via <see cref="UpdateExtension"/>. Each fluent method clones the extension, mutates
    /// the clone, and re-registers it on the options builder.
    /// </summary>
    private MySqlOptionsExtension _extension;

    internal DbContextOptionsBuilder OptionsBuilder => _optionsBuilder;

    internal MySqlDbContextOptionsBuilder(
        DbContextOptionsBuilder optionsBuilder,
        MySqlOptionsExtension extension
    )
    {
        _optionsBuilder = optionsBuilder ?? throw new ArgumentNullException(nameof(optionsBuilder));
        _extension = extension ?? throw new ArgumentNullException(nameof(extension));
    }

    /// <summary>
    /// Enables the opt-in retry configuration surface for the provider.
    /// </summary>
    /// <param name="maxRetryCount">The maximum number of retry attempts.</param>
    /// <param name="maxRetryDelay">The maximum delay between retry attempts.</param>
    /// <returns>The current builder instance.</returns>
    public MySqlDbContextOptionsBuilder EnableRetryOnFailure(
        int maxRetryCount = MySqlRetryOptions.DefaultMaxRetryCount,
        TimeSpan? maxRetryDelay = null
    ) => UpdateExtension(currentExtension =>
        currentExtension.WithRetryOptions(MySqlRetryOptions.Create(maxRetryCount, maxRetryDelay)));

    /// <summary>
    /// Configures the default provider-level GUID storage format.
    /// </summary>
    /// <param name="format">The default GUID storage format.</param>
    /// <returns>The current builder instance.</returns>
    public MySqlDbContextOptionsBuilder DefaultGuidFormat(
        MySqlGuidFormat format
    ) => Enum.IsDefined(format)
        ? UpdateExtension(currentExtension => currentExtension.WithDefaultGuidFormat(format))
        : throw new ArgumentOutOfRangeException(nameof(format));

    private MySqlDbContextOptionsBuilder UpdateExtension(
        Func<MySqlOptionsExtension, MySqlOptionsExtension> update
    )
    {
        _extension = update(_extension);
        ((IDbContextOptionsBuilderInfrastructure)_optionsBuilder).AddOrUpdateExtension(_extension);

        return this;
    }
}
