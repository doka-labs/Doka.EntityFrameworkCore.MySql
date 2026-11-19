namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Stores immutable primitive execution requirements for a guarded recursive SELECT.</summary>
internal sealed class MySqlRecursiveQueryPolicy
{
    private readonly string[] _depthParameterNames;

    /// <summary>Captures the generated control shape and independently owned runtime depth parameter names.</summary>
    internal MySqlRecursiveQueryPolicy(
        IReadOnlyList<string> depthParameterNames,
        string presenceColumnName,
        string invalidLimitColumnName,
        int expectedPayloadColumnCount
    )
    {
        ArgumentNullException.ThrowIfNull(depthParameterNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(presenceColumnName);
        ArgumentException.ThrowIfNullOrWhiteSpace(invalidLimitColumnName);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedPayloadColumnCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(expectedPayloadColumnCount, int.MaxValue - 2);

        if (string.Equals(presenceColumnName, invalidLimitColumnName, StringComparison.Ordinal))
        {
            throw new ArgumentException("Recursive query control column names must differ.");
        }

        _depthParameterNames = new string[depthParameterNames.Count];

        for (var index = 0; index < depthParameterNames.Count; index++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(depthParameterNames[index]);
            _depthParameterNames[index] = depthParameterNames[index];
        }

        PresenceColumnName = presenceColumnName;
        InvalidLimitColumnName = invalidLimitColumnName;
        ExpectedPayloadColumnCount = expectedPayloadColumnCount;
    }

    /// <summary>Gets the trailing column distinguishing real payload from the synthetic empty row.</summary>
    internal string PresenceColumnName { get; }

    /// <summary>Gets the final native guard code for recursion-depth and per-string packet-capacity budgets.</summary>
    internal string InvalidLimitColumnName { get; }

    /// <summary>Gets the number of visible payload columns emitted by the actual SELECT.</summary>
    internal int ExpectedPayloadColumnCount { get; }

    /// <summary>Validates current parameter values on every execution, including cached command reuse.</summary>
    internal void Validate(
        IReadOnlyDictionary<string, object?>? parameterValues
    ) => ValidateDepthParameters(_depthParameterNames.AsSpan(), parameterValues);

    /// <summary>Validates the same public depth contract for SELECT and explicit direct-write commands.</summary>
    internal static void ValidateDepthParameters(
        ReadOnlySpan<string> names,
        IReadOnlyDictionary<string, object?>? values
    )
    {
        // WHY: Cached SQL does not rerun translation-time argument checks. Inspect
        // the current dictionary without retaining values or allocating on success.
        foreach (var name in names)
        {
            if (values is null
                || !values.TryGetValue(name, out var value)
                || value is not int depth
                || depth < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(values),
                    "Recursive depth parameters must contain non-negative Int32 values.");
            }
        }
    }
}
