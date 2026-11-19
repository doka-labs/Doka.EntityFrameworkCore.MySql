namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlUpsertCommandBuilder
{
    /// <summary>Builds the next bounded batch without rereading or reconverting a pending candidate.</summary>
    internal Batch BindNextBatch<TEntity>(
        DbContext context,
        DbCommand parameterFactory,
        IReadOnlyList<TEntity> entities,
        ref int nextIndex,
        ref BoundRow? pending,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        var rows = new List<BoundRow>();
        var bytes = _fixedPayloadBytes;
        var parameterCount = 0;

        while (pending is not null
            || nextIndex < entities.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = pending ?? BindRow(context, parameterFactory, entities[nextIndex++]);
            pending = null;

            if (row.ParameterCount > MaximumParameters
                || _fixedPayloadBytes + row.PayloadBytes > MaximumPayloadBytes)
            {
                throw new InvalidOperationException(
                    "One UPSERT candidate exceeds the bounded provider command budget.");
            }

            if (rows.Count != 0
                && (rows.Count == MaximumRows
                    || parameterCount + row.ParameterCount > MaximumParameters
                    || bytes + row.PayloadBytes > MaximumPayloadBytes))
            {
                // WHY: This candidate already owns converted parameters. Retain it once, then
                // rebase parameter names when the following batch consumes it; never convert twice.
                pending = row;

                break;
            }

            rows.Add(row);
            bytes = checked(bytes + row.PayloadBytes);
            parameterCount += row.ParameterCount;
        }

        return new Batch(rows, parameterCount, bytes, pending is not null || nextIndex < entities.Count);
    }

    /// <summary>Reads each model value once and delegates conversion and parameter facets to its actual column mapping.</summary>
#pragma warning disable EF1001 // This EF11 nonmutating entry/member seam is confined to one binding method; it never creates entries or detects changes.
    private BoundRow BindRow(
        DbContext context,
        DbCommand parameterFactory,
        object entity
    )
    {
        var entry = (_stateManager ??= context.GetService<IStateManager>())
            .TryGetEntry(entity, Target.EntityType, throwOnTypeMismatch: false);

        var parameters = new DbParameter?[_columns.Length];
        var bytes = 4L;
        var count = 0;

        // WHY: Validate the descriptor array directly and stop before binding a row with a forbidden value.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var guard in _readOnlyGuards)
        {
            var value = guard.Getter is not null
                ? guard.Getter.GetClrValueUsingContainingEntity(entity)
                : entry is null
                    ? guard.Sentinel
                    : entry.GetCurrentValue(guard.Property);

            if (!guard.Comparer.Equals(value, guard.Sentinel))
            {
                throw new InvalidOperationException(
                    "UPSERT input assigns a computed property whose before-save behavior forbids a value.");
            }
        }

        // WHY: Actual descriptor arrays are indexed in this per-cell hot path; it neither allocates
        // an interface enumerator nor repeats metadata/getter resolution for each input entity.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _columns.Length; index++)
        {
            var column = _columns[index];
            var property = column.Property;
            var value = column.Getter is not null
                ? column.Getter.GetClrValueUsingContainingEntity(entity)
                : entry is null
                    ? column.Sentinel
                    : entry.GetCurrentValue(property);

            // WHY: The configured model comparer and Sentinel preserve EF's custom sentinel
            // semantics after one getter read; HasSentinelValue would invoke that getter again.
            var sentinel = column.Comparer.Equals(value, column.Sentinel);
            var temporary = entry?.HasTemporaryValue(property) == true;
            var beforeSave = column.BeforeSave;

            if (temporary && !column.ServerDefault)
            {
                throw new InvalidOperationException(
                    "UPSERT requires permanent values for client-generated input properties.");
            }

            if (beforeSave == PropertySaveBehavior.Throw
                && !sentinel
                && !temporary)
            {
                throw new InvalidOperationException(
                    "UPSERT input assigns a property whose before-save behavior forbids a value.");
            }

            if ((sentinel || temporary || beforeSave == PropertySaveBehavior.Ignore)
                && column.ServerDefault)
            {
                bytes = checked(bytes + 9);

                continue;
            }

            if (beforeSave != PropertySaveBehavior.Save
                || (sentinel && column.Generation != ValueGenerated.Never))
            {
                throw new InvalidOperationException(
                    "UPSERT cannot generate a missing permanent input value on the client.");
            }

            if (value is null
                && !column.Store.IsNullable)
            {
                throw new InvalidOperationException("UPSERT requires a nonnull value for a required input column.");
            }

            var parameter = column.Mapping.CreateParameter(parameterFactory, "p", value, column.Store.IsNullable);
            parameters[index] = parameter;
            count++;

            // WHY: Twenty-four bytes bound the final placeholder name, separators, and wire
            // envelope even after a pending row is rebased to the end of a 65,535-parameter batch.
            bytes = checked(bytes + EstimateParameterBytes(parameter.Value) + 24);
        }

        return new BoundRow(parameters, count, bytes);
    }
#pragma warning restore EF1001

    /// <summary>Builds one normal EF relational command from already converted, command-local parameters.</summary>
    internal IRelationalCommand BuildCommand(
        Batch batch,
        IRelationalCommandBuilderFactory factory,
        ISqlGenerationHelper sql
    )
    {
        // WHY: The sealed provider helper uses @pN. Unprefixed native names and a direct
        // SQL prefix avoid measured formatting/normalization allocations; replacement
        // helpers retain their independent parameter-name and placeholder contracts.
        var useNativeParameterNames = sql is MySqlSqlGenerationHelper;
        var builder = factory.Create();
        builder.Append(_header);
        var parameterIndex = 0;

        for (var rowIndex = 0; rowIndex < batch.Rows.Count; rowIndex++)
        {
            if (rowIndex != 0)
            {
                builder.Append(",\n");
            }

            builder.Append("(");
            var parameters = batch.Rows[rowIndex].Parameters;

            for (var columnIndex = 0; columnIndex < parameters.Length; columnIndex++)
            {
                if (columnIndex != 0)
                {
                    builder.Append(", ");
                }

                if (parameters[columnIndex] is not { } parameter)
                {
                    builder.Append("DEFAULT");

                    continue;
                }

                var name = string.Create(CultureInfo.InvariantCulture, $"p{parameterIndex}");
                parameterIndex++;

                if (useNativeParameterNames)
                {
                    parameter.ParameterName = name;
                    builder
                        .Append("@")
                        .Append(name);
                }
                else
                {
                    parameter.ParameterName = sql.GenerateParameterName(name);
                    builder.Append(sql.GenerateParameterNamePlaceholder(name));
                }

                builder.AddRawParameter(name, parameter);
            }

            builder.Append(")");
        }

        builder.Append(_suffix);

        if (builder.Build() is not MySqlRelationalCommand command)
        {
            throw new InvalidOperationException("UPSERT requires Doka's direct-write relational command factory.");
        }

        // WHY: Only explicit direct writes use the cleanup-preserving, nonretry command branch.
        // Ordinary queries and SaveChanges retain their existing execution and ownership paths.
        command.IsDirectWrite = true;

        return command;
    }

    /// <summary>Bounds the existing driver's admitted scalar/binary parameter encodings without allocating encoded arrays.</summary>
    internal static long EstimateParameterBytes(
        object? value
    ) => value switch
    {
        null or DBNull => 4,
        string text => checked(16L + 2L * Encoding.UTF8.GetByteCount(text)),
        char => 32,
        char[] characters => checked(16L + 2L * Encoding.UTF8.GetByteCount(characters.AsSpan())),
        byte[] binary => checked(16L + 2L * binary.LongLength),
        ReadOnlyMemory<byte> binary => checked(16L + 2L * binary.Length),
        Memory<byte> binary => checked(16L + 2L * binary.Length),
        ArraySegment<byte> binary => checked(16L + 2L * binary.Count),
        MySqlGeometry geometry => checked(16L + 2L * geometry.Value.Length),
        bool => 5,
        byte or sbyte or short or ushort or int or uint or long or ulong or Enum => 32,
        float or double or decimal => 64,
        Guid => 96,
        DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan => 128,
        _ => throw new InvalidOperationException(
            "UPSERT does not support this mapped provider parameter representation."),
    };

    /// <summary>Owns only one candidate's already converted parameters.</summary>
    internal sealed record BoundRow(
        DbParameter?[] Parameters,
        int ParameterCount,
        long PayloadBytes
    );

    /// <summary>Owns one bounded statement's candidate rows and its exact continuation state.</summary>
    internal sealed record Batch(
        List<BoundRow> Rows,
        int ParameterCount,
        long PayloadBytes,
        bool HasMore
    );
}
