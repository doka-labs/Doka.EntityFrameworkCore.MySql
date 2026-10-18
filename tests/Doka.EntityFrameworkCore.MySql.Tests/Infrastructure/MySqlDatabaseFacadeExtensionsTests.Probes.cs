namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlDatabaseFacadeExtensionsTests
{
    /// <summary>Constructs one explicit family configuration with fail-fast I/O observers.</summary>
    private static DbContextOptionsBuilder<FamilyContext> Options(
        bool mariaDb,
        NoIoBoundary boundary
    )
        => new DbContextOptionsBuilder<FamilyContext>()
            .UseMySql(ConnectionString, TargetVersion(mariaDb))
            .AddInterceptors(boundary, boundary.Commands);

    /// <summary>Uses existing supported descriptors rather than detecting an actual server.</summary>
    private static MySqlServerVersion TargetVersion(
        bool mariaDb
    )
        => mariaDb ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

    /// <summary>Owns the configured service graph instead of fragmenting EF's global internal provider cache.</summary>
    private static ServiceProvider CreateServices(
        DbContextOptions options
    )
    {
        var services = new ServiceCollection();

        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>Provides ordinary context initialization and EF's public context-pool constructor shape.</summary>
    private sealed class FamilyContext : DbContext
    {
        /// <summary>Provides EF's single public options constructor required for context pooling.</summary>
        /// <param name="options">The test-owned configured provider and internal service graph.</param>
        public FamilyContext(
            DbContextOptions<FamilyContext> options
        ) : base(options) { }

        /// <summary>Gets or sets a deliberately misleading model annotation supplied by the test.</summary>
        internal string? ModelFamilyMarker { get; init; }

        /// <summary>Gets the number of convention model builds invoked for this context instance.</summary>
        internal int ModelBuilds { get; private set; }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            ModelBuilds++;

            if (ModelFamilyMarker is not null)
            {
                modelBuilder.Model.SetAnnotation(FamilyAnnotation, ModelFamilyMarker);
            }
        }
    }

    /// <summary>Exposes only the public options service to isolate negative provider-configuration guards.</summary>
    private sealed class OptionsOnlyFacade(
        DbContext context,
        IServiceProvider services
    )
        : DatabaseFacade(context), IInfrastructure<IServiceProvider>
    {
        /// <inheritdoc />
        IServiceProvider IInfrastructure<IServiceProvider>.Instance => services;
    }

    /// <summary>Rejects synchronous and asynchronous connection attempts at the actual EF boundary.</summary>
    private sealed class NoIoBoundary : DbConnectionInterceptor
    {
        /// <summary>Gets the number of attempted connection starts.</summary>
        internal int ConnectionStarts { get; private set; }

        /// <summary>Gets the command-creation observer used alongside this connection observer.</summary>
        internal NoCommandBoundary Commands { get; } = new();

        /// <inheritdoc />
        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result
        )
        {
            ConnectionStarts++;

            throw new InvalidOperationException("Family inspection attempted database I/O.");
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            ConnectionStarts++;

            throw new InvalidOperationException("Family inspection attempted asynchronous database I/O.");
        }
    }

    /// <summary>Rejects command creation, including attempts on an already open connection.</summary>
    private sealed class NoCommandBoundary : DbCommandInterceptor
    {
        /// <summary>Gets the number of attempted command creations.</summary>
        internal int Created { get; private set; }

        /// <inheritdoc />
        public override InterceptionResult<DbCommand> CommandCreating(
            CommandCorrelatedEventData eventData,
            InterceptionResult<DbCommand> result
        )
        {
            Created++;

            throw new InvalidOperationException("Family inspection attempted to create a database command.");
        }
    }
}
