namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

internal static class PerformanceJsonSpatialWorkloads
{
    private static readonly Point s_referencePoint = new(13.4050, 52.5200) { SRID = 4326 };

    public static void Register(
        PerformanceWorkloadCatalog catalog
    )
    {
        AddJsonComparerWorkloads(catalog);

        AddJsonMaterializationWorkloads(catalog);

        AddSpatialWorkloads(catalog);
    }

    private static void AddJsonComparerWorkloads(
        PerformanceWorkloadCatalog catalog
    )
    {
        AddJsonElementComparison(catalog, "json.compare.element.equal.bytes-1024", 1024, MismatchKind.None);
        AddJsonElementComparison(catalog, "json.compare.element.equal.bytes-65536", 65536, MismatchKind.None);
        AddJsonElementComparison(catalog, "json.compare.element.early-mismatch.bytes-65536", 65536, MismatchKind.Early);
        AddJsonElementComparison(catalog, "json.compare.element.late-mismatch.bytes-65536", 65536, MismatchKind.Late);
        AddJsonElementComparison(catalog, "json.compare.element.equal.bytes-1048576", 1048576, MismatchKind.None);

        AddJsonNodeComparison(catalog, "json.compare.node.equal.bytes-65536", 65536, MismatchKind.None);
        AddJsonNodeComparison(catalog, "json.compare.node.early-mismatch.bytes-65536", 65536, MismatchKind.Early);
        AddJsonNodeComparison(catalog, "json.compare.node.late-mismatch.bytes-65536", 65536, MismatchKind.Late);
    }

    private static void AddJsonElementComparison(
        PerformanceWorkloadCatalog catalog,
        string id,
        int payloadBytes,
        MismatchKind mismatch
    )
    {
        var (leftJson, rightJson) = BuildPayloadPair(payloadBytes, mismatch);
        var left = catalog.Own(JsonDocument.Parse(leftJson));
        var right = catalog.Own(JsonDocument.Parse(rightJson));
        var comparer = MySqlJsonValueComparers.JsonElementComparer;

        catalog.Add(
            new PerformanceWorkload(
                id,
                _ => ValueTask.FromResult(comparer.Equals(left.RootElement, right.RootElement) ? 1L : 0L)));
    }

    private static void AddJsonNodeComparison(
        PerformanceWorkloadCatalog catalog,
        string id,
        int payloadBytes,
        MismatchKind mismatch
    )
    {
        var (leftJson, rightJson) = BuildPayloadPair(payloadBytes, mismatch);
        var left = JsonNode.Parse(leftJson)
            ?? throw new InvalidDataException("The left JSON workload payload is null.");
        var right = JsonNode.Parse(rightJson)
            ?? throw new InvalidDataException("The right JSON workload payload is null.");
        var comparer = MySqlJsonValueComparers.JsonNodeComparer;

        catalog.Add(
            new PerformanceWorkload(
                id,
                _ => ValueTask.FromResult(comparer.Equals(left, right) ? 1L : 0L)));
    }

    private static (string Left, string Right) BuildPayloadPair(
        int payloadBytes,
        MismatchKind mismatch
    )
    {
        const string prefixA = "{\"early\":\"A\",\"payload\":\"";
        const string prefixB = "{\"early\":\"B\",\"payload\":\"";
        const string suffixA = "\",\"late\":\"A\"}";
        const string suffixB = "\",\"late\":\"B\"}";

        var paddingLength = Math.Max(1, payloadBytes - prefixA.Length - suffixA.Length);
        var padding = new string('x', paddingLength);
        var left = prefixA + padding + suffixA;
        var right = mismatch switch
        {
            MismatchKind.None => left,
            MismatchKind.Early => prefixB + padding + suffixA,
            MismatchKind.Late => prefixA + padding + suffixB,
            _ => throw new UnreachableException(),
        };

        return (left, right);
    }

    private static void AddJsonMaterializationWorkloads(
        PerformanceWorkloadCatalog catalog
    )
    {
        AddJsonMaterialization(catalog, "json.materialize.async.rows-1", 1, useAsync: true);
        AddJsonMaterialization(catalog, "json.materialize.async.rows-100", 100, useAsync: true);
        AddJsonMaterialization(catalog, "json.materialize.async.rows-1000", 1000, useAsync: true);
        AddJsonMaterialization(catalog, "json.materialize.sync.rows-100", 100, useAsync: false);
    }

    private static void AddJsonMaterialization(
        PerformanceWorkloadCatalog catalog,
        string id,
        int rowCount,
        bool useAsync
    )
    {
        catalog.Add(
            new PerformanceWorkload(
                id,
                cancellationToken => MaterializeJsonAsync(rowCount, useAsync, cancellationToken)));
    }

    private static async ValueTask<long> MaterializeJsonAsync(
        int rowCount,
        bool useAsync,
        CancellationToken cancellationToken
    )
    {
        if (!useAsync)
        {
            using var context = BenchmarkEnvironment.CreateContext();
            var payloads = context
                .BasicEntities.AsNoTracking()
                .OrderBy(entity => entity.Id)
                .Select(entity => entity.Payload)
                .Take(rowCount)
                .ToList();

            return payloads.Sum(payload => (long)payload.Length);
        }

        await using var asyncContext = BenchmarkEnvironment.CreateContext();

        var asyncPayloads = await asyncContext
            .BasicEntities.AsNoTracking()
            .OrderBy(entity => entity.Id)
            .Select(entity => entity.Payload)
            .Take(rowCount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return asyncPayloads.Sum(payload => (long)payload.Length);
    }

    private static void AddSpatialWorkloads(
        PerformanceWorkloadCatalog catalog
    )
    {
        AddSpatial(catalog, "spatial.materialize.async.rows-1", 1, useAsync: true);
        AddSpatial(catalog, "spatial.materialize.async.rows-100", 100, useAsync: true);
        AddSpatial(catalog, "spatial.materialize.async.rows-1000", 1000, useAsync: true);
        AddSpatial(catalog, "spatial.materialize.sync.rows-100", 100, useAsync: false);
    }

    private static void AddSpatial(
        PerformanceWorkloadCatalog catalog,
        string id,
        int rowCount,
        bool useAsync
    )
    {
        catalog.Add(
            new PerformanceWorkload(
                id,
                cancellationToken => MaterializeSpatialAsync(rowCount, useAsync, cancellationToken)));
    }

    private static async ValueTask<long> MaterializeSpatialAsync(
        int rowCount,
        bool useAsync,
        CancellationToken cancellationToken
    )
    {
        if (!useAsync)
        {
            using var context = BenchmarkEnvironment.CreateContext();
            var rows = context
                .SpatialEntities.AsNoTracking()
                .Where(entity => EF.Functions.DistanceSphere(entity.Location, s_referencePoint) < 250000d)
                .OrderBy(entity => entity.Id)
                .Take(rowCount)
                .ToList();

            return rows.Sum(entity => (long)entity.Id + entity.Location.SRID);
        }

        await using var asyncContext = BenchmarkEnvironment.CreateContext();

        var asyncRows = await asyncContext
            .SpatialEntities.AsNoTracking()
            .Where(entity => EF.Functions.DistanceSphere(entity.Location, s_referencePoint) < 250000d)
            .OrderBy(entity => entity.Id)
            .Take(rowCount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return asyncRows.Sum(entity => (long)entity.Id + entity.Location.SRID);
    }

    private enum MismatchKind
    {
        None,
        Early,
        Late,
    }
}
