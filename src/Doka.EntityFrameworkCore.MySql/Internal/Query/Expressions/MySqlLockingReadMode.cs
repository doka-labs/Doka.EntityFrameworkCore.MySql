namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Identifies the one locking policy of an executed direct SELECT block.</summary>
internal enum MySqlLockingReadMode
{
    None,
    Update,
    Share,
    NoWait,
    SkipLocked,
}

/// <summary>Keeps typed query metadata and one-attempt conflict classification independent of SQL comments.</summary>
internal static class MySqlLockingReadMetadata
{
    internal const string AnnotationName = MySqlAnnotationNames.Prefix + "LockingRead";
    private static readonly object s_noWaitConflict = new();

    /// <summary>Marks the original native conflict without replacing its exception type or error number.</summary>
    internal static void MarkNoWaitConflict(
        Exception exception
    ) => exception.Data[s_noWaitConflict] = true;

    /// <summary>Tests an exception-local marker; no connection state survives into a later retry decision.</summary>
    internal static bool IsNoWaitConflict(
        Exception exception
    ) => exception.Data[s_noWaitConflict] is true;

    /// <summary>Reads a complete policy or rejects malformed provider-owned metadata.</summary>
    internal static MySqlLockingReadMode GetMode(
        SelectExpression select
    )
    {
        var result = MySqlLockingReadMode.None;
        var tables = select.Tables;

        // WHY: Metadata inspection also runs for ordinary cached-query parameter processing;
        // index the stable IReadOnlyList without creating an interface enumerator.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < tables.Count; index++)
        {
            var direct = tables[index];

            while (direct is JoinExpressionBase join)
            {
                direct = join.Table;
            }

            // WHY: The carrier belongs to this completed block, not its table alias. Do not
            // discover a nested SELECT's independent policy while inspecting an outer query.
            if (direct is not TableExpression table
                || table.FindAnnotation(AnnotationName) is not { } annotation)
            {
                continue;
            }

            if (annotation.Value is not MySqlLockingReadMode mode
                || mode is not (MySqlLockingReadMode.None
                    or MySqlLockingReadMode.Update
                    or MySqlLockingReadMode.Share
                    or MySqlLockingReadMode.NoWait
                    or MySqlLockingReadMode.SkipLocked)
                || (result != MySqlLockingReadMode.None && result != mode))
            {
                throw new InvalidOperationException("The locking read has invalid provider metadata.");
            }

            result = mode;
        }

        return result;
    }

    /// <summary>Carries one validated block policy without using EF's unsupported Select annotation writer.</summary>
    internal static SelectExpression ApplyMode(
        SelectExpression select,
        MySqlLockingReadMode mode
    )
    {
        var existing = GetMode(select);

        if (existing == mode)
        {
            return select;
        }

        if (existing != MySqlLockingReadMode.None
            || mode is not (MySqlLockingReadMode.Update
                or MySqlLockingReadMode.Share
                or MySqlLockingReadMode.NoWait
                or MySqlLockingReadMode.SkipLocked)
            || select.Tables.Count == 0
            || select.Tables[0] is not TableExpression first)
        {
            throw new InvalidOperationException("The locking read no longer has a direct physical query root.");
        }

        // WHY: SelectExpression.WithAnnotations deliberately throws in EF11. Its supported
        // table-copy and Update seams retain policy without mutating cached trees. The SQL
        // generator still appends one clause to the entire root block, not to this one table.
        var tables = select.Tables.ToArray();
        tables[0] = first.AddAnnotation(AnnotationName, mode);

        return select.Update(
            tables,
            select.Predicate,
            select.GroupBy,
            select.Having,
            select.Projection,
            select.Orderings,
            select.Offset,
            select.Limit);
    }
}
