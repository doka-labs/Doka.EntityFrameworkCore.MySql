namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Qualifies configured-family inspection independently of connections, models and context leases.</summary>
public sealed partial class MySqlDatabaseFacadeExtensionsTests
{
    private const string ConnectionString =
        "Server=127.0.0.1;Port=1;Database=family_options;User ID=unused;GuidFormat=Binary16;Pooling=false";

    private const string FamilyAnnotation = "MySql:ServerVersion";

    /// <summary>All supported connection inputs expose the configured family without opening or querying.</summary>
    /// <param name="mariaDb">Whether the configured target is MariaDB.</param>
    /// <param name="input">The provider's connection configuration path.</param>
    [Theory]
    [InlineData(false, "ConnectionString")]
    [InlineData(false, "Connection")]
    [InlineData(false, "DataSource")]
    [InlineData(true, "ConnectionString")]
    [InlineData(true, "Connection")]
    [InlineData(true, "DataSource")]
    public async Task Configured_family_is_read_without_connection_or_command_io(
        bool mariaDb,
        string input
    )
    {
        // Arrange
        var boundary = new NoIoBoundary();
        await using var connection = input == "Connection" ? new MySqlConnection(ConnectionString) : null;
        await using var dataSource = input == "DataSource"
            ? new MySqlDataSourceBuilder(ConnectionString).Build() : null;

        var options = new DbContextOptionsBuilder<FamilyContext>();
        var version = TargetVersion(mariaDb);
        switch (input)
        {
            case "ConnectionString":
                options.UseMySql(ConnectionString, version);
                break;
            case "Connection":
                options.UseMySql(connection!, version);
                break;
            case "DataSource":
                options.UseMySql(dataSource!, version);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input));
        }

        options.AddInterceptors(boundary, boundary.Commands);
        await using var services = CreateServices(options.Options);
        await using var context = new FamilyContext(options.UseInternalServiceProvider(services).Options);

        // Act
        var actual = context.Database.IsMariaDb();

        // Assert
        Assert.Equal(mariaDb, actual);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        if (connection is not null)
        {
            Assert.Equal(ConnectionState.Closed, connection.State);
            Assert.Same(connection, context.Database.GetDbConnection());
        }
    }

    /// <summary>Supplied finalized and optimized runtime models cannot override the configured server family.</summary>
    /// <param name="mariaDb">Whether the configured target is MariaDB.</param>
    /// <param name="runtime">Whether the supplied model is EF's optimized runtime representation.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Supplied_models_do_not_determine_the_configured_family(
        bool mariaDb,
        bool runtime
    )
    {
        // Arrange
        var boundary = new NoIoBoundary();
        var marker = mariaDb ? "MySQL 8.4.0" : "MariaDB 11.8.0";
        var sourceOptions = Options(mariaDb, boundary);
        await using var services = CreateServices(sourceOptions.Options);
        await using var source = new FamilyContext(sourceOptions.UseInternalServiceProvider(services).Options)
        {
            ModelFamilyMarker = marker,
        };

        IModel supplied;
        if (runtime)
        {
            // WHY: Obtain the runtime representation through EF's public model lifecycle rather than
            // constructing RuntimeModel with its explicitly internal constructor or silencing EF1001.
            supplied = source.Model;
        }
        else
        {
            var model = new ModelBuilder();
            model.Model.SetAnnotation(FamilyAnnotation, marker);
            supplied = model.FinalizeModel();
        }

        var options = Options(mariaDb, boundary).UseModel(supplied).UseInternalServiceProvider(services);
        await using var context = new FamilyContext(options.Options);

        // Act
        var actual = context.Database.IsMariaDb();

        // Assert
        Assert.Equal(mariaDb, actual);
        Assert.Same(supplied, options.Options.FindExtension<CoreOptionsExtension>()!.Model);
        Assert.Equal(marker, supplied.FindAnnotation(FamilyAnnotation)?.Value);
        Assert.Equal(0, context.ModelBuilds);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        if (runtime)
        {
            Assert.IsAssignableFrom<RuntimeModel>(supplied);
        }
    }

    /// <summary>A pooled context retains its fixed family when the same instance is leased again.</summary>
    /// <param name="mariaDb">Whether the pool is configured for MariaDB.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pooled_context_leases_retain_their_configured_family(
        bool mariaDb
    )
    {
        // Arrange
        var boundary = new NoIoBoundary();
        var options = Options(mariaDb, boundary);
        await using var internalServices = CreateServices(options.Options);
        var registrations = new ServiceCollection();
        registrations.AddDbContextPool<FamilyContext>(builder => builder
            .UseMySql(ConnectionString, TargetVersion(mariaDb))
            .AddInterceptors(boundary, boundary.Commands)
            .UseInternalServiceProvider(internalServices), poolSize: 1);
        await using var application = registrations.BuildServiceProvider();
        FamilyContext first;
        FamilyContext second;
        bool firstFamily;
        bool secondFamily;
        await using (var lease = application.CreateAsyncScope())
        {
            first = lease.ServiceProvider.GetRequiredService<FamilyContext>();
            firstFamily = first.Database.IsMariaDb();
        }

        // Act
        await using (var lease = application.CreateAsyncScope())
        {
            second = lease.ServiceProvider.GetRequiredService<FamilyContext>();
            secondFamily = second.Database.IsMariaDb();
        }

        // Assert
        Assert.Same(first, second);
        Assert.Equal(mariaDb, firstFamily);
        Assert.Equal(mariaDb, secondFamily);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>One internal provider cannot silently change its fixed family after initialization.</summary>
    /// <param name="firstMariaDb">Whether the first configuration initializing the shared services is MariaDB.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conflicting_shared_services_reject_family_reconfiguration(
        bool firstMariaDb
    )
    {
        // Arrange
        var boundary = new NoIoBoundary();
        var firstOptions = Options(firstMariaDb, boundary);
        await using var services = CreateServices(firstOptions.Options);
        await using var first = new FamilyContext(firstOptions.UseInternalServiceProvider(services).Options);
        _ = first.Database.IsMariaDb();
        var conflictingOptions = Options(!firstMariaDb, boundary).UseInternalServiceProvider(services);
        await using var conflicting = new FamilyContext(conflictingOptions.Options);

        // Act
        var error = Record.Exception(() => conflicting.Database.IsMariaDb());

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("server version changed", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A missing facade fails before any EF infrastructure access.</summary>
    [Fact]
    public void Null_database_is_rejected()
    {
        // Arrange
        DatabaseFacade database = null!;
        Action act = () => database.IsMariaDb();

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var error = Assert.IsType<ArgumentNullException>(recordedException);
        Assert.Equal("database", error.ParamName);
    }

    /// <summary>Missing provider options or server-version metadata produce an explicit configuration error.</summary>
    /// <param name="dokaOptions">Whether the options include an unconfigured Doka extension.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_configuration_is_rejected_by_the_options_guard(
        bool dokaOptions
    )
    {
        // Arrange
        var options = new DbContextOptionsBuilder();
        if (dokaOptions)
        {
            ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(new MySqlOptionsExtension());
        }

        var registrations = new ServiceCollection().AddEntityFrameworkDokaMySql();
        registrations.AddSingleton<IDbContextOptions>(options.Options);
        await using var services = registrations.BuildServiceProvider();
        await using var context = new DbContext(new DbContextOptionsBuilder()
            .UseInternalServiceProvider(services).Options);
        // WHY: This public infrastructure seam isolates the option guard from EF's earlier provider validation;
        // no additional foreign database-provider dependency is needed to represent options without Doka.
        var database = new OptionsOnlyFacade(context, services);
        Action act = () => database.IsMariaDb();

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var error = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Equal(dokaOptions
            ? "A MySQL server version must be configured."
            : "Database.IsMariaDb requires the Doka MySQL provider configured with UseMySql.", error.Message);
    }

    /// <summary>A genuinely unconfigured context preserves EF's clear configuration failure.</summary>
    [Fact]
    public void Unconfigured_context_is_rejected_before_database_io()
    {
        // Arrange
        using var context = new DbContext(new DbContextOptionsBuilder().Options);

        // Act
        var error = Record.Exception(() => context.Database.IsMariaDb());

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("No database provider", error.Message, StringComparison.Ordinal);
    }
}
