namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlWindowFunctionIntegrationTests
{
    /// <summary>Reads fixed native work counters from the already-open physical data session.</summary>
    private static async Task<WindowSessionCounters> ReadWideSessionCountersAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = new MySqlCommand(
            "SHOW SESSION STATUS WHERE Variable_name IN ('Com_select','Created_tmp_tables','Created_tmp_disk_tables',"
            + "'Sort_rows','Sort_merge_passes','Sort_scan');",
            connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0), long.Parse(reader.GetString(1), CultureInfo.InvariantCulture));
        }

        // WHY: Missing instrumentation must be visible rather than silently reporting zero native work.
        return new WindowSessionCounters(values["Com_select"],
            values["Created_tmp_tables"],
            values["Created_tmp_disk_tables"],
            values["Sort_rows"],
            values["Sort_merge_passes"],
            values["Sort_scan"]);
    }

    /// <summary>Records observable session work; it is neither a fixed plan nor a resource-budget gate.</summary>
    private sealed record WindowSessionCounters(
        long Selects,
        long TempTables,
        long DiskTempTables,
        long SortRows,
        long SortMergePasses,
        long SortScans
    )
    {
        /// <summary>Reports an interval without silently subtracting or clamping engine-specific inspection overhead.</summary>
        public WindowSessionCounters Since(
            WindowSessionCounters before
        ) => new(Selects - before.Selects,
            TempTables - before.TempTables,
            DiskTempTables - before.DiskTempTables,
            SortRows - before.SortRows,
            SortMergePasses - before.SortMergePasses,
            SortScans - before.SortScans);
    }
}
