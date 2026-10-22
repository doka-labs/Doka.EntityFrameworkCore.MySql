namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the infrastructure responsibility for MySqlPrimitiveCollectionParameterTests.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterTests
{
    private static IQueryable<PrimitiveCollectionEntity> Query(
        PrimitiveCollectionContext context,
        Guid[] values
    ) => context.Items.Where(item => values.Contains(item.BinaryId));

    private static PrimitiveCollectionContext CreateContext(
        MySqlGuidFormat defaultGuidFormat = MySqlGuidFormat.Binary16,
        ParameterTranslationMode? parameterTranslationMode = null,
        bool cacheServiceProvider = false
    )
    {
        var builder = cacheServiceProvider
            ? new DbContextOptionsBuilder<PrimitiveCollectionContext>()
                .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
            : MySqlFunctionalTestOptions.CreateTransientBuilder<PrimitiveCollectionContext>();

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
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
