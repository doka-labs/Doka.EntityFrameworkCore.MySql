namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Enforces a bounded structural-depth contract before spatial values enter
/// NetTopologySuite's recursive WKT and WKB readers.
/// </summary>
internal static class MySqlSpatialInputGuard
{
    /// <summary>
    /// Maximum structural parser frames, including the leaf geometry. Ordinary
    /// OGC geometries require only a handful of frames; this higher bound preserves
    /// legitimate complex values while keeping parser recursion safely below
    /// process-stack exhaustion.
    /// </summary>
    internal const int MaximumNestingDepth = 256;

    /// <summary>
    /// Validates that a WKT value cannot drive the recursive reader beyond the
    /// provider's structural-depth budget.
    /// </summary>
    public static void ValidateWkt(
        string wkt
    )
    {
        ArgumentNullException.ThrowIfNull(wkt);

        var depth = 0;

        foreach (var character in wkt)
        {
            if (character == '(')
            {
                depth++;

                if (depth > MaximumNestingDepth)
                {
                    ThrowNestingLimitExceeded();
                }
            }
            else if (character == ')'
                     && depth > 0)
            {
                depth--;
            }
        }
    }

    /// <summary>
    /// Iteratively walks standard, ISO, and EWKB geometry records so collection
    /// nesting is bounded before the recursive WKB reader sees the value. Incomplete
    /// syntax is left to NetTopologySuite, while unknown geometry types fail closed
    /// with the same parser exception category.
    /// </summary>
    public static void ValidateWkb(
        ReadOnlySpan<byte> wkb
    )
    {
        Span<uint> remainingChildren = stackalloc uint[MaximumNestingDepth];
        var collectionDepth = 0;
        var offset = 0;

        while (true)
        {
            if (!TryReadGeometryHeader(
                    wkb,
                    ref offset,
                    out var geometryType,
                    out var coordinateDimension,
                    out var littleEndian))
            {
                return;
            }

            if (geometryType is < 1 or > 7)
            {
                throw new ParseException("Unsupported WKB geometry type.");
            }

            var completeGeometry = geometryType switch
            {
                1 => TrySkipCoordinates(wkb, ref offset, coordinateCount: 1, coordinateDimension),
                2 => TrySkipCoordinateSequence(wkb, ref offset, coordinateDimension, littleEndian),
                3 => TrySkipPolygon(wkb, ref offset, coordinateDimension, littleEndian),
                4 or 5 or 6 or 7 => true,
                _ => false,
            };

            if (geometryType is 4 or 5 or 6 or 7)
            {
                if (!TryReadUInt32(wkb, ref offset, littleEndian, out var childCount))
                {
                    return;
                }

                if (childCount > 0)
                {
                    // The eventual leaf geometry consumes one additional parser
                    // frame, so the collection stack must retain one frame of headroom.
                    if (collectionDepth == MaximumNestingDepth - 1)
                    {
                        ThrowNestingLimitExceeded();
                    }

                    remainingChildren[collectionDepth] = childCount;
                    collectionDepth++;
                    continue;
                }
            }
            else if (!completeGeometry)
            {
                return;
            }

            while (collectionDepth > 0)
            {
                ref var remaining = ref remainingChildren[collectionDepth - 1];
                remaining--;

                if (remaining > 0)
                {
                    break;
                }

                collectionDepth--;
            }

            if (collectionDepth == 0)
            {
                return;
            }
        }
    }

    private static bool TryReadGeometryHeader(
        ReadOnlySpan<byte> wkb,
        ref int offset,
        out uint geometryType,
        out int coordinateDimension,
        out bool littleEndian
    )
    {
        geometryType = 0;
        coordinateDimension = 0;
        littleEndian = false;

        if ((uint)offset >= (uint)wkb.Length)
        {
            return false;
        }

        var byteOrder = wkb[offset++];

        if (byteOrder is not (0 or 1))
        {
            return false;
        }

        littleEndian = byteOrder == 1;

        if (!TryReadUInt32(wkb, ref offset, littleEndian, out var encodedType))
        {
            return false;
        }

        var hasZ = (encodedType & 0x80000000u) != 0;
        var hasM = (encodedType & 0x40000000u) != 0;
        var hasSrid = (encodedType & 0x20000000u) != 0;
        geometryType = encodedType & 0x1fffffffu;
        coordinateDimension = 2 + (hasZ ? 1 : 0) + (hasM ? 1 : 0);

        if (geometryType >= 3000
            && geometryType < 4000)
        {
            geometryType -= 3000;
            coordinateDimension = 4;
        }
        else if (geometryType >= 2000
                 && geometryType < 3000)
        {
            geometryType -= 2000;
            coordinateDimension = 3;
        }
        else if (geometryType >= 1000
                 && geometryType < 2000)
        {
            geometryType -= 1000;
            coordinateDimension = 3;
        }

        return !hasSrid || TrySkip(wkb, ref offset, sizeof(uint));
    }

    private static bool TrySkipCoordinateSequence(
        ReadOnlySpan<byte> wkb,
        ref int offset,
        int coordinateDimension,
        bool littleEndian
    )
    {
        if (!TryReadUInt32(wkb, ref offset, littleEndian, out var coordinateCount))
        {
            return false;
        }

        return TrySkipCoordinates(wkb, ref offset, coordinateCount, coordinateDimension);
    }

    private static bool TrySkipPolygon(
        ReadOnlySpan<byte> wkb,
        ref int offset,
        int coordinateDimension,
        bool littleEndian
    )
    {
        if (!TryReadUInt32(wkb, ref offset, littleEndian, out var ringCount))
        {
            return false;
        }

        for (uint ringIndex = 0; ringIndex < ringCount; ringIndex++)
        {
            if (!TryReadUInt32(wkb, ref offset, littleEndian, out var coordinateCount)
                || !TrySkipCoordinates(wkb, ref offset, coordinateCount, coordinateDimension))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TrySkipCoordinates(
        ReadOnlySpan<byte> wkb,
        ref int offset,
        uint coordinateCount,
        int coordinateDimension
    )
    {
        var byteCount = (ulong)coordinateCount * (uint)coordinateDimension * sizeof(double);

        return byteCount <= int.MaxValue && TrySkip(wkb, ref offset, (int)byteCount);
    }

    private static bool TryReadUInt32(
        ReadOnlySpan<byte> value,
        ref int offset,
        bool littleEndian,
        out uint result
    )
    {
        result = 0;

        if (offset < 0
            || value.Length - offset < sizeof(uint))
        {
            return false;
        }

        var bytes = value.Slice(offset, sizeof(uint));
        result = littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt32BigEndian(bytes);
        offset += sizeof(uint);

        return true;
    }

    private static bool TrySkip(
        ReadOnlySpan<byte> value,
        ref int offset,
        int byteCount
    )
    {
        if (offset < 0
            || byteCount < 0
            || value.Length - offset < byteCount)
        {
            return false;
        }

        offset += byteCount;
        return true;
    }

    private static void ThrowNestingLimitExceeded()
    {
        throw new InvalidOperationException(
            $"The spatial value exceeds the provider nesting limit of {MaximumNestingDepth.ToString(CultureInfo.InvariantCulture)}.");
    }
}
