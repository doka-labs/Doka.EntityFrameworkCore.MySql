using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationOperationHandlerTests
{
    /// <summary>
    /// Creates an isolated handler service graph with explicit provider registration order.
    /// </summary>
    private static ServiceProvider CreateServiceProvider(
        IReadOnlyList<Type> handlerTypes,
        bool registerBeforeProvider
    )
    {
        var services = new ServiceCollection();

        if (registerBeforeProvider)
        {
            RegisterHandlers(services, handlerTypes);
        }

        services.AddEntityFrameworkDokaMySql();

        if (!registerBeforeProvider)
        {
            RegisterHandlers(services, handlerTypes);
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static void RegisterHandlers(
        IServiceCollection services,
        IEnumerable<Type> handlerTypes
    )
    {
        foreach (var handlerType in handlerTypes)
        {
            services.TryAddEnumerable(
                new ServiceDescriptor(typeof(IMySqlMigrationOperationHandler), handlerType, ServiceLifetime.Scoped));
        }
    }

    private static void AddPlugin<THandler>(
        DbContextOptionsBuilder builder
    )
        where THandler : class, IMySqlMigrationOperationHandler =>
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new HandlerOptionsExtension<THandler>());

    /// <summary>
    /// Creates the context with a caller-owned internal service provider.
    /// </summary>
    private static HandlerContext CreateContext(
        IServiceProvider serviceProvider,
        MySqlServerVersion? serverVersion = null
    )
    {
        var builder = new DbContextOptionsBuilder<HandlerContext>();
        builder
            .UseInternalServiceProvider(serviceProvider)
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=password;",
                serverVersion ?? MySqlServerVersion.MySql(new Version(8, 4, 11)));

        return new HandlerContext(builder.Options);
    }

    private sealed class HandlerContext : DbContext
    {
        /// <summary>
        /// Initializes the handler context fixture.
        /// </summary>
        public HandlerContext(
            DbContextOptions<HandlerContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<HandlerRenameEntity>(entity =>
            {
                entity.ToTable("HandlerRenameEntries");
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.RenamedValue)
                    .HasColumnType("varchar(64)")
                    .HasComment("path\\segment");
            });
        }
    }

    private sealed class HandlerRenameEntity
    {
        /// <summary>
        /// Gets or sets the entity key.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the renamed value.
        /// </summary>
        public string RenamedValue { get; set; } = string.Empty;
    }

    private sealed class HandlerOptionsExtension<THandler> : IDbContextOptionsExtension
        where THandler : class, IMySqlMigrationOperationHandler
    {
        private DbContextOptionsExtensionInfo? _info;

        /// <inheritdoc />
        public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

        /// <inheritdoc />
        public void ApplyServices(
            IServiceCollection services
        ) => services.TryAddEnumerable(ServiceDescriptor.Scoped<IMySqlMigrationOperationHandler, THandler>());

        /// <inheritdoc />
        public void Validate(
            IDbContextOptions options
        )
        { }

        private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
        {
            /// <summary>
            /// Initializes the extension info fixture.
            /// </summary>
            public ExtensionInfo(
                IDbContextOptionsExtension extension
            ) : base(extension) { }

            /// <inheritdoc />
            public override bool IsDatabaseProvider => false;

            /// <inheritdoc />
            public override string LogFragment => $"migration-handler={typeof(THandler).FullName} ";

            /// <inheritdoc />
            public override int GetServiceProviderHashCode() => typeof(THandler).GetHashCode();

            /// <inheritdoc />
            public override void PopulateDebugInfo(
                IDictionary<string, string> debugInfo
            ) => debugInfo[$"DokaMySqlMigrationHandler:{typeof(THandler).FullName}"] = "1";

            /// <inheritdoc />
            public override bool ShouldUseSameServiceProvider(
                DbContextOptionsExtensionInfo other
            ) => other is ExtensionInfo;
        }
    }
}
