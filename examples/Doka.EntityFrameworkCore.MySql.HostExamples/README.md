# Host Integration

Demonstrates provider integration with the .NET Generic Host, OpenTelemetry,
Serilog, and explicit legacy `char(36)` GUID mapping.

```bash
dotnet run --project \
  examples/Doka.EntityFrameworkCore.MySql.HostExamples/Doka.EntityFrameworkCore.MySql.HostExamples.csproj
```

The sample configures host services and telemetry without adding OpenTelemetry
or Serilog dependencies to either provider package. See the complete
[host-integration guide](../../docs/host-integration-examples.md).
