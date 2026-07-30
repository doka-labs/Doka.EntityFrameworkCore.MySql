using System.Reflection;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Diagnostics.Internal;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Infrastructure;

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class LoggingMySqlTest : LoggingRelationalTestBase<MySqlDbContextOptionsBuilder, MySqlOptionsExtension>
{
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

    protected override Microsoft.EntityFrameworkCore.TestUtilities.TestLogger CreateTestLogger() =>
        new Microsoft.EntityFrameworkCore.TestUtilities.TestLogger<MySqlLoggingDefinitions>();

    protected override string ProviderName => "Doka.EntityFrameworkCore.MySql";

    protected override string ProviderVersion =>
        typeof(MySqlOptionsExtension).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? string.Empty;

    protected override string DefaultOptions => $"using Doka MySql ({MySqlTestEnvironment.ServerVersion}) ";
}
