namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the infrastructure responsibility for MySqlPrimitiveCollectionParameterTests.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterTests
{
    private static IQueryable<PrimitiveCollectionEntity> Query(
        PrimitiveCollectionContext context,
        Guid[] values
    ) => context.Items.Where(item => values.Contains(item.BinaryId));

    /// <summary>Creates a context using the existing transient or cache-sensitive provider policy.</summary>
    /// <param name="defaultGuidFormat">The default model collection GUID representation.</param>
    /// <param name="parameterTranslationMode">The optional explicit collection strategy.</param>
    /// <param name="cacheServiceProvider">Whether to verify the shared provider cache.</param>
    /// <param name="mariaDb">Whether to translate for the supported MariaDB profile.</param>
    private static PrimitiveCollectionContext CreateContext(
        MySqlGuidFormat defaultGuidFormat = MySqlGuidFormat.Binary16,
        ParameterTranslationMode? parameterTranslationMode = null,
        bool cacheServiceProvider = false,
        bool mariaDb = false
    )
    {
        var builder = cacheServiceProvider
            ? new DbContextOptionsBuilder<PrimitiveCollectionContext>()
                .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
            : MySqlFunctionalTestOptions.CreateTransientBuilder<PrimitiveCollectionContext>();

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            mariaDb
                ? MySqlServerVersion.MariaDb(new Version(12, 3, 0))
                : MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options =>
            {
                options.DefaultGuidFormat(defaultGuidFormat);

                if (parameterTranslationMode is not null)
                {
                    options.UseParameterizedCollectionMode(parameterTranslationMode.Value);
                }
            });

        return new PrimitiveCollectionContext(builder.Options);
    }
}
