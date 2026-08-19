namespace Doka.EntityFrameworkCore.MySql.HostExamples;

internal static class Program
{
    private static readonly Action<Microsoft.Extensions.Logging.ILogger, string, Exception?> s_hostConfigured =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(0, nameof(Program)),
            "Configured host-level OpenTelemetry and Serilog integration for {Provider}.");

    public static async Task Main(
        string[] args
    )
    {
        using var startupActivity = SampleTelemetry.ActivitySource.StartActivity("host-example-bootstrap");

        var builder = Host.CreateApplicationBuilder(args);
        var connectionString = builder.Configuration["ConnectionStrings:MySql"]
            ?? "Server=localhost;Database=doka_host_examples;User ID=root;Password=password;";

        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        builder.Services.AddSerilog((
            _,
            loggerConfiguration
        ) =>
        {
            loggerConfiguration
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture);
        });

        builder
            .Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("Doka.EntityFrameworkCore.MySql.HostExamples"))
            .WithTracing(tracing => tracing
                .AddSource(SampleTelemetry.ActivitySourceName)
                .AddConsoleExporter())
            .WithMetrics(metrics => metrics
                .AddMeter(SampleTelemetry.MeterName)
                .AddConsoleExporter());

        builder.Services.AddDbContext<LegacyGuidContext>(options =>
        {
            options.UseMySql(
                connectionString,
                serverVersion,
                mySqlOptions => mySqlOptions.DefaultGuidFormat(MySqlGuidFormat.Binary16));
        });

        using var host = builder.Build();

        var logger = host
            .Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("HostExamples");

        s_hostConfigured(logger, "Doka.EntityFrameworkCore.MySql", null);

        SampleTelemetry.HostBuildCounter.Add(1, new KeyValuePair<string, object?>("provider", "mysql"));

        await host
            .StartAsync()
            .ConfigureAwait(false);
        await host
            .StopAsync()
            .ConfigureAwait(false);
    }
}

internal static class SampleTelemetry
{
    public const string ActivitySourceName = "Doka.EntityFrameworkCore.MySql.HostExamples";
    public const string MeterName = "Doka.EntityFrameworkCore.MySql.HostExamples";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> HostBuildCounter = Meter.CreateCounter<long>("host_examples.builds");
}

internal sealed class LegacyGuidContext : DbContext
{
    public LegacyGuidContext(
        DbContextOptions<LegacyGuidContext> options
    ) : base(options) { }

    public DbSet<LegacyCustomer> LegacyCustomers => Set<LegacyCustomer>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<LegacyCustomer>(entity =>
        {
            entity.ToTable("LegacyCustomers");
            entity.HasKey(candidate => candidate.LegacyId);
            entity
                .Property(candidate => candidate.LegacyId)
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36)
                .UseMySqlClientGuidValueGeneration();
            entity
                .Property(candidate => candidate.DisplayName)
                .HasMaxLength(128);
        });
    }
}

internal sealed class LegacyCustomer
{
    public Guid LegacyId { get; set; }

    public string DisplayName { get; set; } = string.Empty;
}
