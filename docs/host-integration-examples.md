# Host Integration Examples

These examples are Phase 4 documentation deliverables. They demonstrate host-level integration around `Doka.EntityFrameworkCore.MySql` without adding `OpenTelemetry` or `Serilog` dependencies to the provider packages themselves.

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

## Local Validation

Build the sample without adding any host dependencies to the provider packages:

```bash
dotnet build examples/Doka.EntityFrameworkCore.MySql.HostExamples/Doka.EntityFrameworkCore.MySql.HostExamples.csproj --configuration Release
```

The sample is intentionally outside the provider runtime package graph and exists only as a host-level integration reference.
