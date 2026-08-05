using System.Diagnostics.CodeAnalysis;

namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Centralizes deterministic identifiers, period metadata, and the reverse-engineering
/// marker shared by temporal migrations and scaffolding.
/// </summary>
/// <remarks>
/// MySQL emulation has no native catalog flag. Its marker must therefore be deterministic,
/// delimiter-safe, and strictly decoded so reverse engineering never mistakes a user trigger
/// or malformed payload for provider-owned temporal infrastructure.
/// </remarks>
internal static class MySqlTemporalMetadata
{
    private const string EmulationMarkerPrefix = "doka-temporal-v1:";

    private static readonly UTF8Encoding s_strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public const string DefaultPeriodStartPropertyName = "PeriodStart";

    public const string DefaultPeriodEndPropertyName = "PeriodEnd";

    public static MySqlTemporalTableMetadata? FindTableMetadata(
        ITable table
    )
    {
        ArgumentNullException.ThrowIfNull(table);

        var temporalEntityType = table
            .EntityTypeMappings.Select(mapping => mapping.TypeBase)
            .OfType<IReadOnlyEntityType>()
            .FirstOrDefault(entityType => entityType.IsMySqlTemporal());

        if (temporalEntityType is null)
        {
            return null;
        }

        var storeObject = StoreObjectIdentifier.Table(table.Name, table.Schema);

        return new MySqlTemporalTableMetadata(
            temporalEntityType.GetMySqlTemporalHistoryTableName(),
            temporalEntityType.GetMySqlTemporalHistoryTableSchema(),
            FindPeriodColumnName(
                temporalEntityType,
                temporalEntityType.GetMySqlTemporalPeriodStartPropertyName(),
                storeObject),
            FindPeriodColumnName(
                temporalEntityType,
                temporalEntityType.GetMySqlTemporalPeriodEndPropertyName(),
                storeObject));
    }

    public static void ValidatePeriodPropertyType(
        IReadOnlyEntityType entityType,
        string propertyName
    )
    {
        var property = entityType.FindProperty(propertyName);

        if (property is not null
            && (property.ClrType != typeof(DateTime) || property.IsNullable))
        {
            throw new InvalidOperationException(
                $"Temporal period property '{entityType.DisplayName()}.{propertyName}' "
                + "must be a non-nullable DateTime property.");
        }
    }

    public static string CreateDefaultHistoryTableName(
        string tableName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var candidate = tableName + "History";

        if (candidate.Length <= MySqlConventionSetBuilder.MaxIdentifierLength)
        {
            return candidate;
        }

        // The stable suffix prevents two long table names with the same prefix
        // from collapsing to one history-table identifier after truncation.
        var hash = XxHash64
            .HashToUInt64(Encoding.UTF8.GetBytes(candidate))
            .ToString("x16", CultureInfo.InvariantCulture);

        var prefixLength = MySqlConventionSetBuilder.MaxIdentifierLength - hash.Length - 1;

        return candidate[..prefixLength] + "_" + hash;
    }

    public static string CreateTriggerName(
        string? schema,
        string tableName,
        string eventName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        var candidate = "__doka_temporal_" + tableName + "_" + eventName;

        if (candidate.Length <= MySqlConventionSetBuilder.MaxIdentifierLength)
        {
            return candidate;
        }

        // Trigger identifiers are schema-wide on both target engines. Include the
        // qualified table identity in the suffix so truncation cannot create a collision.
        var qualifiedName = (schema ?? string.Empty) + "." + tableName + "." + eventName;
        var hash = XxHash64
            .HashToUInt64(Encoding.UTF8.GetBytes(qualifiedName))
            .ToString("x16", CultureInfo.InvariantCulture);
        var prefixLength = MySqlConventionSetBuilder.MaxIdentifierLength - hash.Length - 1;

        return candidate[..prefixLength] + "_" + hash;
    }

    public static string CreateEmulationMarker(
        string? historySchema,
        string historyTable,
        string periodStartColumn,
        string periodEndColumn
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyTable);
        ArgumentException.ThrowIfNullOrWhiteSpace(periodStartColumn);
        ArgumentException.ThrowIfNullOrWhiteSpace(periodEndColumn);

        // Hex encoding keeps arbitrary identifiers delimiter-safe without a second escaping
        // grammar. The reverse-engineering path can then reject malformed UTF-8 instead of
        // guessing how a damaged marker was intended to be decoded.
        return EmulationMarkerPrefix
            + EncodeMarkerValue(historySchema)
            + ":"
            + EncodeMarkerValue(historyTable)
            + ":"
            + EncodeMarkerValue(periodStartColumn)
            + ":"
            + EncodeMarkerValue(periodEndColumn);
    }

    public static bool TryParseEmulationMarker(
        string actionStatement,
        [NotNullWhen(true)] out MySqlTemporalEmulationMarker? marker
    )
    {
        ArgumentNullException.ThrowIfNull(actionStatement);

        const string commentPrefix = "/* " + EmulationMarkerPrefix;
        const string commentSuffix = " */";

        marker = null;

        var markerStart = actionStatement.IndexOf(commentPrefix, StringComparison.Ordinal);

        if (markerStart < 0
            || actionStatement.IndexOf(commentPrefix, markerStart + commentPrefix.Length, StringComparison.Ordinal) >= 0)
        {
            return false;
        }

        var payloadStart = markerStart + commentPrefix.Length;
        var markerEnd = actionStatement.IndexOf(commentSuffix, payloadStart, StringComparison.Ordinal);

        if (markerEnd < 0)
        {
            return false;
        }

        var encodedValues = actionStatement[payloadStart..markerEnd]
            .Split(':');

        if (encodedValues.Length != 4
            || !TryDecodeMarkerValue(encodedValues[0], out var historySchema)
            || !TryDecodeMarkerValue(encodedValues[1], out var historyTable)
            || !TryDecodeMarkerValue(encodedValues[2], out var periodStartColumn)
            || !TryDecodeMarkerValue(encodedValues[3], out var periodEndColumn)
            || string.IsNullOrWhiteSpace(historyTable)
            || string.IsNullOrWhiteSpace(periodStartColumn)
            || string.IsNullOrWhiteSpace(periodEndColumn)
            || string.Equals(periodStartColumn, periodEndColumn, StringComparison.Ordinal))
        {
            return false;
        }

        marker = new MySqlTemporalEmulationMarker(
            string.IsNullOrEmpty(historySchema) ? null : historySchema,
            historyTable,
            periodStartColumn,
            periodEndColumn);

        return true;
    }

    private static string EncodeMarkerValue(
        string? value
    ) => Convert.ToHexString(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static bool TryDecodeMarkerValue(
        string encodedValue,
        [NotNullWhen(true)] out string? value
    )
    {
        try
        {
            value = s_strictUtf8.GetString(Convert.FromHexString(encodedValue));
            return true;
        }
        catch (FormatException)
        {
            value = null;
            return false;
        }
        catch (DecoderFallbackException)
        {
            value = null;
            return false;
        }
    }

    private static string FindPeriodColumnName(
        IReadOnlyEntityType entityType,
        string? propertyName,
        StoreObjectIdentifier storeObject
    )
    {
        if (string.IsNullOrWhiteSpace(propertyName)
            || entityType.FindProperty(propertyName) is not { } property
            || property.GetColumnName(storeObject) is not { } columnName)
        {
            throw new InvalidOperationException(
                $"Temporal entity type '{entityType.DisplayName()}' does not map period property "
                + $"'{propertyName}' to table '{storeObject.Name}'.");
        }

        return columnName;
    }
}

internal sealed record MySqlTemporalTableMetadata(
    string? HistoryTable,
    string? HistorySchema,
    string PeriodStartColumn,
    string PeriodEndColumn
);

internal sealed record MySqlTemporalEmulationMarker(
    string? HistorySchema,
    string HistoryTable,
    string PeriodStartColumn,
    string PeriodEndColumn
);
