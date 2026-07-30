using System.Data;
using System.Diagnostics.CodeAnalysis;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Interception;

/// <summary>
/// Shared MySQL implementation of the official command-interception
/// contract. Concrete variants exercise injected and diagnostic listeners.
/// </summary>
public abstract class CommandInterceptionMySqlTestBase
    : CommandInterceptionTestBase
{
    protected CommandInterceptionMySqlTestBase(
        InterceptionMySqlFixtureBase fixture
    ) : base(fixture)
    {
    }

    public override async Task<string> Intercept_query_passively(
        bool async,
        bool inject
    )
    {
        AssertSql(
            """
            SELECT `s`.`Id`, `s`.`Type` FROM `Singularity` AS `s`
            """,
            await base.Intercept_query_passively(async, inject));

        return null!;
    }

    protected override async Task<string> QueryMutationTest<TInterceptor>(
        bool async,
        bool inject
    )
    {
        AssertSql(
            """
            SELECT `s`.`Id`, `s`.`Type` FROM `Brane` AS `s`
            """,
            await base.QueryMutationTest<TInterceptor>(async, inject));

        return null!;
    }

    public override async Task<string> Intercept_query_to_replace_execution(
        bool async,
        bool inject
    )
    {
        AssertSql(
            """
            SELECT `s`.`Id`, `s`.`Type` FROM `Singularity` AS `s`
            """,
            await base.Intercept_query_to_replace_execution(async, inject));

        return null!;
    }

    public abstract class InterceptionMySqlFixtureBase : InterceptionFixtureBase
    {
        protected override string StoreName => "CommandInterception";

        protected override ITestStoreFactory TestStoreFactory =>
            MySqlTestStoreFactory.Instance;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<IInterceptor> injectedInterceptors
        ) => base.InjectInterceptors(
            serviceCollection.AddEntityFrameworkDokaMySql(),
            injectedInterceptors);
    }

    /// <summary>
    /// Exercises command interceptors without diagnostic-listener
    /// subscriptions.
    /// </summary>
    [Trait("Category", "Spec")]
    [Collection(FunctionalDatabaseTestGroup.Name)]
    public sealed class CommandInterceptionMySqlTest
        : CommandInterceptionMySqlTestBase,
        IClassFixture<CommandInterceptionMySqlTest.InterceptionMySqlFixture>
    {
        public CommandInterceptionMySqlTest(
            InterceptionMySqlFixture fixture
        ) : base(fixture)
        {
        }

        public sealed class InterceptionMySqlFixture : InterceptionMySqlFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener => false;
        }
    }

    /// <summary>
    /// Exercises command interceptors with diagnostic-listener subscriptions.
    /// </summary>
    [Trait("Category", "Spec")]
    [Collection(FunctionalDatabaseTestGroup.Name)]
    public sealed class CommandInterceptionWithDiagnosticsMySqlTest
        : CommandInterceptionMySqlTestBase,
        IClassFixture<CommandInterceptionWithDiagnosticsMySqlTest.InterceptionMySqlFixture>
    {
        public CommandInterceptionWithDiagnosticsMySqlTest(
            InterceptionMySqlFixture fixture
        ) : base(fixture)
        {
        }

        public sealed class InterceptionMySqlFixture : InterceptionMySqlFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener => true;
        }
    }
}

/// <summary>
/// Shared MySQL implementation of the official connection-interception
/// contract, including connection creation, replacement, failure, and
/// disposal paths.
/// </summary>
/// <remarks>
/// The upstream contract adds per-test interceptor instances to throwaway
/// context options. Both provider-configuration paths keep those intentionally
/// distinct providers out of EF Core's process-wide service-provider cache.
/// </remarks>
public abstract class ConnectionInterceptionMySqlTestBase
    : ConnectionInterceptionTestBase
{
    private static readonly MySqlServerVersion s_serverVersion =
        MySqlServerVersion.MySql(new Version(8, 4, 0));

    protected ConnectionInterceptionMySqlTestBase(
        InterceptionMySqlFixtureBase fixture
    ) : base(fixture)
    {
    }

    protected override DbContextOptionsBuilder ConfigureProvider(
        DbContextOptionsBuilder optionsBuilder
    ) => optionsBuilder
        .EnableServiceProviderCaching(false)
        .UseMySql(
            "Server=localhost;Database=DokaConnectionInterception;User ID=root;",
            s_serverVersion);

    protected override BadUniverseContext CreateBadUniverse(
        DbContextOptionsBuilder optionsBuilder
    ) => new(
        optionsBuilder
            .EnableServiceProviderCaching(false)
            .UseMySql(new ThrowingDbConnection(), s_serverVersion)
            .Options);

    public abstract class InterceptionMySqlFixtureBase : InterceptionFixtureBase
    {
        protected override string StoreName => "ConnectionInterception";

        protected override ITestStoreFactory TestStoreFactory =>
            MySqlTestStoreFactory.Instance;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<IInterceptor> injectedInterceptors
        ) => base.InjectInterceptors(
            serviceCollection.AddEntityFrameworkDokaMySql(),
            injectedInterceptors);
    }

    private sealed class ThrowingDbConnection : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "Database";

        public override string DataSource => "DataSource";

        public override string ServerVersion =>
            throw new NotImplementedException();

        public override ConnectionState State => ConnectionState.Closed;

        public override void ChangeDatabase(
            string databaseName
        ) => throw new NotImplementedException();

        public override void Close() => throw new NotImplementedException();

        public override void Open() => throw new NotImplementedException();

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel
        ) => throw new NotImplementedException();

        protected override DbCommand CreateDbCommand() =>
            throw new NotImplementedException();
    }

    /// <summary>
    /// Exercises connection interceptors without diagnostic-listener
    /// subscriptions.
    /// </summary>
    [Trait("Category", "Spec")]
    [Collection(FunctionalDatabaseTestGroup.Name)]
    public sealed class ConnectionInterceptionMySqlTest
        : ConnectionInterceptionMySqlTestBase,
        IClassFixture<ConnectionInterceptionMySqlTest.InterceptionMySqlFixture>
    {
        public ConnectionInterceptionMySqlTest(
            InterceptionMySqlFixture fixture
        ) : base(fixture)
        {
        }

        public sealed class InterceptionMySqlFixture : InterceptionMySqlFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener => false;
        }
    }

    /// <summary>
    /// Exercises connection interceptors with diagnostic-listener
    /// subscriptions.
    /// </summary>
    [Trait("Category", "Spec")]
    [Collection(FunctionalDatabaseTestGroup.Name)]
    public sealed class ConnectionInterceptionWithDiagnosticsMySqlTest
        : ConnectionInterceptionMySqlTestBase,
        IClassFixture<ConnectionInterceptionWithDiagnosticsMySqlTest.InterceptionMySqlFixture>
    {
        public ConnectionInterceptionWithDiagnosticsMySqlTest(
            InterceptionMySqlFixture fixture
        ) : base(fixture)
        {
        }

        public sealed class InterceptionMySqlFixture : InterceptionMySqlFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener => true;
        }
    }
}

/// <summary>
/// Shared MySQL implementation of the official SaveChanges-interception
/// contract.
/// </summary>
public abstract class SaveChangesInterceptionMySqlTestBase
    : SaveChangesInterceptionTestBase
{
    protected SaveChangesInterceptionMySqlTestBase(
        InterceptionMySqlFixtureBase fixture
    ) : base(fixture)
    {
    }

    public abstract class InterceptionMySqlFixtureBase : InterceptionFixtureBase
    {
        protected override string StoreName => "SaveChangesInterception";

        protected override ITestStoreFactory TestStoreFactory =>
            MySqlTestStoreFactory.Instance;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<IInterceptor> injectedInterceptors
        ) => base.InjectInterceptors(
            serviceCollection.AddEntityFrameworkDokaMySql(),
            injectedInterceptors);
    }

    /// <summary>
    /// Exercises SaveChanges interceptors without diagnostic listeners.
    /// </summary>
    [Trait("Category", "Spec")]
    [Collection(FunctionalDatabaseTestGroup.Name)]
    public sealed class SaveChangesInterceptionMySqlTest
        : SaveChangesInterceptionMySqlTestBase,
        IClassFixture<SaveChangesInterceptionMySqlTest.InterceptionMySqlFixture>
    {
        public SaveChangesInterceptionMySqlTest(
            InterceptionMySqlFixture fixture
        ) : base(fixture)
        {
        }

        public sealed class InterceptionMySqlFixture : InterceptionMySqlFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener => false;
        }
    }

    /// <summary>
    /// Exercises SaveChanges interceptors with diagnostic listeners.
    /// </summary>
    [Trait("Category", "Spec")]
    [Collection(FunctionalDatabaseTestGroup.Name)]
    public sealed class SaveChangesInterceptionWithDiagnosticsMySqlTest
        : SaveChangesInterceptionMySqlTestBase,
        IClassFixture<SaveChangesInterceptionWithDiagnosticsMySqlTest.InterceptionMySqlFixture>
    {
        public SaveChangesInterceptionWithDiagnosticsMySqlTest(
            InterceptionMySqlFixture fixture
        ) : base(fixture)
        {
        }

        public sealed class InterceptionMySqlFixture : InterceptionMySqlFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener => true;
        }
    }
}

/// <summary>
/// Shared MySQL implementation of the official transaction-interception
/// contract.
/// </summary>
public abstract class TransactionInterceptionMySqlTestBase
    : TransactionInterceptionTestBase
{
    protected TransactionInterceptionMySqlTestBase(
        InterceptionMySqlFixtureBase fixture
    ) : base(fixture)
    {
    }

    public abstract class InterceptionMySqlFixtureBase : InterceptionFixtureBase
    {
        protected override string StoreName => "TransactionInterception";

        protected override ITestStoreFactory TestStoreFactory =>
            MySqlTestStoreFactory.Instance;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<IInterceptor> injectedInterceptors
        ) => base.InjectInterceptors(
            serviceCollection.AddEntityFrameworkDokaMySql(),
            injectedInterceptors);
    }

    /// <summary>
    /// Exercises transaction interceptors without diagnostic listeners.
    /// </summary>
    [Trait("Category", "Spec")]
    [Collection(FunctionalDatabaseTestGroup.Name)]
    public sealed class TransactionInterceptionMySqlTest
        : TransactionInterceptionMySqlTestBase,
        IClassFixture<TransactionInterceptionMySqlTest.InterceptionMySqlFixture>
    {
        public TransactionInterceptionMySqlTest(
            InterceptionMySqlFixture fixture
        ) : base(fixture)
        {
        }

        public sealed class InterceptionMySqlFixture : InterceptionMySqlFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener => false;
        }
    }

    /// <summary>
    /// Exercises transaction interceptors with diagnostic listeners.
    /// </summary>
    [Trait("Category", "Spec")]
    [Collection(FunctionalDatabaseTestGroup.Name)]
    public sealed class TransactionInterceptionWithDiagnosticsMySqlTest
        : TransactionInterceptionMySqlTestBase,
        IClassFixture<TransactionInterceptionWithDiagnosticsMySqlTest.InterceptionMySqlFixture>
    {
        public TransactionInterceptionWithDiagnosticsMySqlTest(
            InterceptionMySqlFixture fixture
        ) : base(fixture)
        {
        }

        public sealed class InterceptionMySqlFixture : InterceptionMySqlFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener => true;
        }
    }
}
