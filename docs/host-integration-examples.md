# Host Integration Examples

These examples demonstrate host-level integration around
`Doka.EntityFrameworkCore.MySql` without adding `OpenTelemetry` or `Serilog`
dependencies to the provider packages themselves.

The runnable sample lives in [Doka.EntityFrameworkCore.MySql.HostExamples.csproj](../examples/Doka.EntityFrameworkCore.MySql.HostExamples/Doka.EntityFrameworkCore.MySql.HostExamples.csproj).

## OpenTelemetry

The provider stays compatible with `OpenTelemetry` through the standard .NET diagnostics surface:

- `Microsoft.Extensions.Logging`
- `Activity` and `ActivitySource`
- `System.Diagnostics.Metrics`

The sample wires host-level telemetry in [Program.cs](../examples/Doka.EntityFrameworkCore.MySql.HostExamples/Program.cs) through `AddOpenTelemetry()`, `AddSource(...)`, `AddMeter(...)`, and the console exporter.

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Doka.EntityFrameworkCore.MySql.HostExamples"))
    .WithTracing(tracing => tracing.AddSource(SampleTelemetry.ActivitySourceName).AddConsoleExporter())
    .WithMetrics(metrics => metrics.AddMeter(SampleTelemetry.MeterName).AddConsoleExporter());
```

This is intentionally a host concern. The provider package itself does not take a direct dependency on `OpenTelemetry.*`.

## Serilog

`Serilog` support is provided through the normal `Microsoft.Extensions.Logging` integration path. The sample uses `AddSerilog()` in [Program.cs](../examples/Doka.EntityFrameworkCore.MySql.HostExamples/Program.cs):

```csharp
builder.Services.AddSerilog(
    (_, loggerConfiguration) =>
    {
        loggerConfiguration
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console();
    });
```

This keeps the provider compatible with `Serilog` while avoiding provider-specific sinks, enrichers, or runtime dependencies.

## Legacy `char(36)` GUID Configuration

The supported compatibility path for legacy textual GUID schemas is explicit property configuration, not inference. The sample shows that configuration in [Program.cs](../examples/Doka.EntityFrameworkCore.MySql.HostExamples/Program.cs) inside `LegacyGuidContext.OnModelCreating(...)`:

```csharp
entity.Property(candidate => candidate.LegacyId)
    .HasMySqlGuidFormat(MySqlGuidFormat.Char36)
    .UseMySqlClientGuidValueGeneration();
```

The sample keeps the provider default at `Binary16` for new-schema posture and uses `Char36` only where a legacy schema explicitly needs it.

Generated snapshots preserve `Guid` as the model CLR type for this mapping. If
an existing relationship changes from an application-level `varchar(36)`
converter to provider-native `Char36`, the migration model differ removes the
foreign key before altering either constrained column and restores the same
constraint after the dependent index is available. The generated down path is
symmetric, so populated relationships do not require a hand-edited migration.

## HealthCheck

Hosts that need a readiness or liveness probe for their `MySqlDbContext` use the standard `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` package. The provider exposes no custom health-check surface; the EF Core integration covers both the connection round-trip and a configurable probe query.

Two layered patterns cover the common cases:

```csharp
builder.Services.AddHealthChecks()
    // Liveness: cheap "can we still reach the database?" probe. The default
    // delegates to EF Core's CanConnectAsync without opening a transaction.
    .AddDbContextCheck<MyDbContext>(
        name: "mysql-liveness",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "live" })

    // Readiness: stronger probe that asserts the migration history is
    // present, so an instance does not declare ready before its schema
    // has caught up with the migrator.
    .AddDbContextCheck<MyDbContext>(
        name: "mysql-readiness",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready" },
        customTestQuery: async (context, cancellationToken) =>
        {
            var applied = await context.Database
                .GetAppliedMigrationsAsync(cancellationToken);
            return applied.Any();
        });

// Map probes per tag so Kubernetes / load balancers hit the right one.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});
```

### Notes

- The liveness probe must NOT block on schema state. Migrations may be in flight on a sibling pod; a liveness failure would tear the pod down before the migrator releases the advisory lock. The `AddDbContextCheck<TContext>` default behavior is correct for this.
- The readiness probe SHOULD assert schema state. The `customTestQuery` above is the minimal form (history table exists); production hosts typically extend it to also verify the latest expected migration row is present.
- Neither probe should run a write or hold a lock. Avoid `SELECT FOR UPDATE`, `GET_LOCK`, or any DDL in the probe path -- they conflict with the migration lock and create false-positive liveness failures during migrator startup.
- The probes consume one connection from the pool per call. Under Kubernetes-default probe intervals (10 seconds) this is negligible; under faster intervals (sub-second) consider a dedicated `DbContext` registration to isolate probe traffic from request traffic.

The pattern is intentionally host-side: the provider package does not depend on `Microsoft.Extensions.Diagnostics.HealthChecks.*`.

## Local Validation

Build the sample without adding any host dependencies to the provider packages:

```bash
dotnet build examples/Doka.EntityFrameworkCore.MySql.HostExamples/Doka.EntityFrameworkCore.MySql.HostExamples.csproj --configuration Release
```

The sample is intentionally outside the provider runtime package graph and exists only as a host-level integration reference.

## Primary Sources

Retrieved 2026-08-21:

- [OpenTelemetry .NET tracing](https://opentelemetry.io/docs/languages/dotnet/traces/getting-started-aspnetcore/)
- [OpenTelemetry .NET metrics](https://opentelemetry.io/docs/languages/dotnet/metrics/getting-started-console/)
- [Serilog integration with `Microsoft.Extensions.Logging`](https://github.com/serilog/serilog-extensions-logging)
- [ASP.NET Core health checks and `AddDbContextCheck`](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0)
- [Kubernetes liveness, readiness, and startup probes](https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/)
