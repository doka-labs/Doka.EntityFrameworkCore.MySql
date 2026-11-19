namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies root ownership and reporter binding across independent design-time scopes.</summary>
public sealed class MySqlDesignTimeLoggingLifetimeTests
{
    private static readonly Action<ILogger, string, Exception?> s_warning = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(0),
        "{Message}");

    /// <summary>A completed scaffold scope cannot dispose the factory needed by the next operation.</summary>
    /// <param name="preRegisterEf">Whether tooling has already registered EF design-time services.</param>
    /// <param name="asynchronous">Whether the first scope is disposed asynchronously.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Sequential_scopes_keep_the_shared_logger_factory_alive(
        bool preRegisterEf,
        bool asynchronous
    )
    {
        // Arrange
        await using var services = CreateServices(preRegisterEf)
            .BuildServiceProvider(validateScopes: true);

        var firstScope = services.CreateAsyncScope();
        var firstLogger = firstScope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var firstModelFactory = firstScope.ServiceProvider.GetRequiredService<IScaffoldingModelFactory>();

        if (asynchronous)
        {
            await firstScope.DisposeAsync();
        }
        else
        {
            // WHY: This theory case verifies the synchronous scope-disposal contract.
            // ReSharper disable once MethodHasAsyncOverload
            firstScope.Dispose();
        }

        using var secondScope = services.CreateScope();

        // Act
        var secondModelFactory = secondScope.ServiceProvider.GetRequiredService<IScaffoldingModelFactory>();
        var secondLogger = secondScope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var categoryLogger = secondLogger.CreateLogger(MySqlLoggerCategory.Scaffolding);

        // Assert
        Assert.NotSame(firstModelFactory, secondModelFactory);
        Assert.Same(firstLogger, secondLogger);
        Assert.NotNull(categoryLogger);
    }

    /// <summary>Disposing one overlapping operation cannot poison another scope's not-yet-created model factory.</summary>
    /// <param name="preRegisterEf">Whether tooling has already registered EF design-time services.</param>
    /// <param name="asynchronous">Whether the completed scope is disposed asynchronously.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Interleaved_scopes_do_not_transfer_logger_ownership_to_the_first_completed_operation(
        bool preRegisterEf,
        bool asynchronous
    )
    {
        // Arrange
        await using var services = CreateServices(preRegisterEf)
            .BuildServiceProvider(validateScopes: true);

        var completedScope = services.CreateAsyncScope();
        using var pendingScope = services.CreateScope();
        _ = completedScope.ServiceProvider.GetRequiredService<IScaffoldingModelFactory>();
        var logger = pendingScope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        // Act
        if (asynchronous)
        {
            await completedScope.DisposeAsync();
        }
        else
        {
            // WHY: This theory case verifies synchronous disposal while another scope remains active.
            // ReSharper disable once MethodHasAsyncOverload
            completedScope.Dispose();
        }

        var modelFactory = pendingScope.ServiceProvider.GetRequiredService<IScaffoldingModelFactory>();
        var categoryLogger = logger.CreateLogger(MySqlLoggerCategory.Scaffolding);

        // Assert
        Assert.NotNull(modelFactory);
        Assert.NotNull(categoryLogger);
        Assert.Same(logger, pendingScope.ServiceProvider.GetRequiredService<ILoggerFactory>());
    }

    /// <summary>The root releases its owned logger and provider exactly once after all operations finish.</summary>
    /// <param name="asynchronous">Whether root disposal is asynchronous.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Root_disposal_releases_the_owned_factory_once(
        bool asynchronous
    )
    {
        // Arrange
        await using var services = CreateServices(preRegisterEf: true)
            .BuildServiceProvider(validateScopes: true);

        ILoggerFactory logger;
        var observer = new DisposalObserverProvider();

        using (var scope = services.CreateScope())
        {
            logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
            logger.AddProvider(observer);
            _ = scope.ServiceProvider.GetRequiredService<IScaffoldingModelFactory>();
        }

        // Act
        if (asynchronous)
        {
            await services.DisposeAsync();
        }
        else
        {
            // WHY: This theory case verifies the root's synchronous ownership cleanup.
            // ReSharper disable once MethodHasAsyncOverload
            services.Dispose();
        }

        // Assert
        Assert.Equal(1, observer.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => logger.CreateLogger(MySqlLoggerCategory.Scaffolding));
    }

    /// <summary>A caller's later registration retains standard instance-versus-factory DI ownership.</summary>
    /// <param name="registerInstance">Whether the caller supplies an externally owned instance.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Caller_logger_override_retains_its_original_ownership_contract(
        bool registerInstance
    )
    {
        // Arrange
        var descriptors = CreateServices(preRegisterEf: true);
        var caller = new CallerLoggerFactory();
        using var callerOwner = registerInstance
            ? caller
            : null;

        if (registerInstance)
        {
            descriptors.AddSingleton<ILoggerFactory>(caller);
        }
        else
        {
            descriptors.AddSingleton<ILoggerFactory>(_ => caller);
        }

        using var services = descriptors.BuildServiceProvider(validateScopes: true);
        ILoggerFactory selected;

        using (var scope = services.CreateScope())
        {
            selected = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
            _ = scope.ServiceProvider.GetRequiredService<IScaffoldingModelFactory>();
        }

        // Act
        services.Dispose();

        // Assert
        Assert.Same(caller, selected);
        Assert.Equal(registerInstance
                ? 0
                : 1,
            caller.DisposeCount);

        if (registerInstance)
        {
            Assert.NotNull(caller.CreateLogger(MySqlLoggerCategory.Scaffolding));
        }
        else
        {
            Assert.Throws<ObjectDisposedException>(() => caller.CreateLogger(MySqlLoggerCategory.Scaffolding));
        }
    }

    /// <summary>The official tooling reporter remains bound to design-time logging after provider registration.</summary>
    /// <param name="preRegisterEf">Whether the reporter came from EF's design-time helper.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Existing_reporter_instance_receives_the_scaffolding_warning_once(
        bool preRegisterEf
    )
    {
        // Arrange
        var warnings = new List<string>();
        var descriptors = new ServiceCollection();
#pragma warning disable EF1001 // EF11 tooling's reporter instance is the versioned D-001 registration boundary under test.
        var reporter = new OperationReporter(new OperationReportHandler(_ => { }, warnings.Add, _ => { }, _ => { }));

        if (preRegisterEf)
        {
            descriptors.AddEntityFrameworkDesignTimeServices(reporter);
        }
        else
        {
            descriptors.AddSingleton<IOperationReporter>(reporter);
        }
#pragma warning restore EF1001

        descriptors.AddEntityFrameworkDokaMySqlDesignTime();
        using var services = descriptors.BuildServiceProvider(validateScopes: true);
        using var scope = services.CreateScope();
        var logger = scope
            .ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(MySqlLoggerCategory.Scaffolding);

        // Act
        s_warning(logger, "scaffolding-reporter-canary", null);

        // Assert
        Assert.Equal("scaffolding-reporter-canary", Assert.Single(warnings));
#pragma warning disable EF1001 // Verify the exact official reporter instance, rather than a second logger-only reporter.
        Assert.Same(reporter, scope.ServiceProvider.GetRequiredService<IOperationReporter>());
#pragma warning restore EF1001
    }

    /// <summary>An unrelated keyed reporter must not bind ordinary scaffold logging.</summary>
    [Fact]
    public void Keyed_reporter_does_not_replace_the_unkeyed_tooling_reporter()
    {
        // Arrange
        var warnings = new List<string>();
        var keyedWarnings = new List<string>();
        var descriptors = new ServiceCollection();
#pragma warning disable EF1001 // Keyed-versus-unkeyed selection is verified at EF11's official reporter registration seam.
        var reporter = new OperationReporter(new OperationReportHandler(_ => { }, warnings.Add, _ => { }, _ => { }));
        var keyedReporter =
            new OperationReporter(new OperationReportHandler(_ => { }, keyedWarnings.Add, _ => { }, _ => { }));

        descriptors.AddEntityFrameworkDesignTimeServices(reporter);
        descriptors.AddKeyedSingleton<IOperationReporter>("unrelated", keyedReporter);
#pragma warning restore EF1001
        descriptors.AddEntityFrameworkDokaMySqlDesignTime();
        using var services = descriptors.BuildServiceProvider(validateScopes: true);
        using var scope = services.CreateScope();
        var logger = scope
            .ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(MySqlLoggerCategory.Scaffolding);

        // Act
        s_warning(logger, "unkeyed-scaffolding-canary", null);

        // Assert
        Assert.Equal("unkeyed-scaffolding-canary", Assert.Single(warnings));
        Assert.Empty(keyedWarnings);
#pragma warning disable EF1001 // The keyed reporter is deliberately not the ordinary design-time service.
        Assert.Same(reporter, scope.ServiceProvider.GetRequiredService<IOperationReporter>());
#pragma warning restore EF1001
    }

    private static ServiceCollection CreateServices(
        bool preRegisterEf
    )
    {
        var services = new ServiceCollection();

        if (preRegisterEf)
        {
            services.AddEntityFrameworkDesignTimeServices();
        }

        services.AddEntityFrameworkDokaMySqlDesignTime();

        return services;
    }

    private sealed class DisposalObserverProvider : ILoggerProvider
    {
        internal int DisposeCount { get; private set; }

        public ILogger CreateLogger(
            string categoryName
        ) => NullLogger.Instance;

        public void Dispose() => DisposeCount++;
    }

    private sealed class CallerLoggerFactory : ILoggerFactory
    {
        private readonly LoggerFactory _inner = new();
        internal int DisposeCount { get; private set; }

        public ILogger CreateLogger(
            string categoryName
        ) => _inner.CreateLogger(categoryName);

        public void AddProvider(
            ILoggerProvider provider
        ) => _inner.AddProvider(provider);

        public void Dispose()
        {
            DisposeCount++;
            _inner.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
