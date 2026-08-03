# Spatial Queries

Demonstrates the optional NetTopologySuite package:

- `UseNetTopologySuite` service activation
- `point` mapping with SRID 4326
- spatial-index DDL
- server-side `DistanceSphere` translation

The example inserts Berlin, Potsdam, and Hamburg and verifies the places within
50 kilometers of Berlin.

```bash
dotnet run --project examples/SpatialQueries/SpatialQueries.csproj
```

See [the shared example configuration](../README.md) for prerequisites.
