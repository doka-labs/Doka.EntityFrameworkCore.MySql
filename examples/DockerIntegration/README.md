# Docker Integration

Demonstrates a complete connection round-trip against one service from the
repository Docker Compose stack. It verifies connectivity, creates an isolated
database, persists a probe row, and reads the server version from the live
connection.

```bash
docker compose -f docker/compose.yml up -d mysql84
dotnet run --project examples/DockerIntegration/DockerIntegration.csproj
```

Select MariaDB or a custom endpoint through the variables documented in the
[shared example configuration](../README.md).
