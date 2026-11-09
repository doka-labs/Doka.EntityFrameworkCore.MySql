using System.Reflection;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using TestLogger = Microsoft.EntityFrameworkCore.TestUtilities.TestLogger;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Infrastructure;

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class LoggingMySqlTest : LoggingRelationalTestBase<MySqlDbContextOptionsBuilder, MySqlOptionsExtension>
{
    protected override string ProviderName => "Doka.EntityFrameworkCore.MySql";

    protected override string ProviderVersion => typeof(MySqlOptionsExtension).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? string.Empty;

    protected override string DefaultOptions
        => $"ParameterizedCollectionTranslationMode=MultipleParameters using Doka MySql ({MySqlTestEnvironment.ServerVersion}) ";

    protected override DbContextOptionsBuilder CreateOptionsBuilder(
        IServiceCollection services,
        Action<RelationalDbContextOptionsBuilder<MySqlDbContextOptionsBuilder, MySqlOptionsExtension>> relationalAction
    ) => new DbContextOptionsBuilder()
        .UseInternalServiceProvider(
            services
                .AddEntityFrameworkDokaMySql()
                .BuildServiceProvider(validateScopes: true))
        .UseMySql(
            MySqlTestEnvironment.ConnectionString,
            MySqlTestEnvironment.ServerVersion,
            options => relationalAction?.Invoke(options));

    protected override TestLogger CreateTestLogger() => new TestLogger<MySqlLoggingDefinitions>();
}
