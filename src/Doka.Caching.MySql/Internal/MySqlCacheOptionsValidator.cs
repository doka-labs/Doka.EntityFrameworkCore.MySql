namespace Doka.Caching.MySql;

internal sealed class MySqlCacheOptionsValidator : IValidateOptions<MySqlCacheOptions>
{
    private static readonly TimeSpan s_minimumCleanupInterval = TimeSpan.FromMinutes(5);

    public ValidateOptionsResult Validate(
        string? name,
        MySqlCacheOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.DataSource is not null)
        {
            if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                failures.Add("DataSource and ConnectionString cannot both be supplied.");
            }

            if (new MySqlConnectionStringBuilder(options.DataSource.ConnectionString).AutoEnlist)
            {
                failures.Add("DataSource must be configured with AutoEnlist=false.");
            }
        }
        else if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add("ConnectionString or DataSource is required.");
        }
        else
        {
            try
            {
                _ = new MySqlConnectionStringBuilder(options.ConnectionString);
            }
            catch (ArgumentException)
            {
                failures.Add("ConnectionString is not a valid MySQL connection string.");
            }
        }

        ValidateIdentifier(options.SchemaName, nameof(options.SchemaName), failures);
        ValidateIdentifier(options.TableName, nameof(options.TableName), failures);

        if (options.DefaultSlidingExpiration < TimeSpan.FromMicroseconds(1))
        {
            failures.Add("DefaultSlidingExpiration must be at least one microsecond.");
        }

        if (options.ExpiredItemsDeletionInterval < s_minimumCleanupInterval)
        {
            failures.Add("ExpiredItemsDeletionInterval must be at least five minutes.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateIdentifier(
        string identifier,
        string optionName,
        List<string> failures
    )
    {
        try
        {
            _ = MySqlCacheIdentifier.Quote(identifier, optionName);
        }
        catch (ArgumentException exception)
        {
            failures.Add(exception.Message);
        }
    }
}
