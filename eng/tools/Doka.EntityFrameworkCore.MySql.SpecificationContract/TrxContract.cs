namespace Doka.EntityFrameworkCore.MySql.SpecificationContract;

/// <summary>
/// Reconciles exact specification discovery IDs with TRX outcomes and D-021 dispositions.
/// Aggregate pass/skip counts cannot conceal a missing, duplicate, or undeclared result.
/// </summary>
internal static class TrxContract
{
    private const int DispositionSchemaVersion = 2;

    /// <summary>
    /// Validates one target's TRX result set against its version-bound discovery contract.
    /// </summary>
    internal static TrxContractReport Validate(
        DiscoveryContractDocument discovery,
        string target,
        IEnumerable<string> trxPaths,
        string dispositionPath
    )
    {
        var errors = new List<string>();
        var expectation = discovery.Targets.FirstOrDefault(item => item.Target == target);
        if (expectation is null)
        {
            return new TrxContractReport(0, 0, 0, 0, [$"Unknown discovery target '{target}'."]);
        }

        var results = trxPaths
            .SelectMany(path => ParseResults(path, expectation.TestIds))
            .Where(result => DiscoveryContract.IsSpecificationTestId(result.TestId))
            .ToArray();

        var duplicates = results
            .GroupBy(result => result.TestId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToArray();

        // WHY: Emit one diagnostic per offending group with its exact outcome count.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var duplicate in duplicates)
        {
            errors.Add($"TRX contains {duplicate.Count()} outcomes for '{duplicate.Key}'.");
        }

        var uniqueResults = results
            .GroupBy(result => result.TestId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);

        var expectedIds = expectation.TestIds.ToHashSet(StringComparer.Ordinal);

        // WHY: Keep each inventory mismatch and its diagnostic in the same control-flow step.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var missing in expectedIds.Except(uniqueResults.Keys, StringComparer.Ordinal))
        {
            errors.Add($"TRX is missing expected test ID '{missing}'.");
        }

        // WHY: Keep unexpected results visible beside the complementary missing-ID check.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var unexpected in uniqueResults.Keys.Except(expectedIds, StringComparer.Ordinal))
        {
            errors.Add($"TRX contains unexpected specification test ID '{unexpected}'.");
        }

        var permittedNotExecuted = LoadPermittedNotExecuted(dispositionPath, discovery, target, errors);
        var passed = 0;
        var notExecuted = 0;
        var failed = 0;

        foreach (var (testId, result) in uniqueResults)
        {
            switch (result.Outcome)
            {
                case "Passed":
                    passed++;
                    if (permittedNotExecuted.Contains(testId))
                    {
                        errors.Add($"Dispositioned test '{testId}' passed and must be re-evaluated.");
                    }

                    break;
                case "NotExecuted":
                case "Skipped":
                    notExecuted++;
                    if (!permittedNotExecuted.Contains(testId))
                    {
                        errors.Add($"TRX contains undeclared NotExecuted test '{testId}'.");
                    }

                    break;
                default:
                    failed++;
                    errors.Add(
                        $"TRX outcome for '{testId}' is '{result.Outcome}', expected Passed "
                        + "or a declared NotExecuted disposition.");

                    break;
            }
        }

        // WHY: Report each missing skip disposition explicitly after the outcome counters are finalized.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var dispositionedId in permittedNotExecuted.Except(
            uniqueResults
                .Where(pair => pair.Value.Outcome is "NotExecuted" or "Skipped")
                .Select(pair => pair.Key),
            StringComparer.Ordinal))
        {
            errors.Add($"Dispositioned test '{dispositionedId}' was not reported as NotExecuted.");
        }

        return new TrxContractReport(uniqueResults.Count, passed, notExecuted, failed, errors);
    }

    private static IReadOnlyList<TrxTestResult> ParseResults(
        string path,
        IReadOnlyList<string> expectedIds
    )
    {
        var document = XDocument.Load(path, LoadOptions.None);
        var definitions = document
            .Descendants()
            .Where(element => element.Name.LocalName == "UnitTest")
            .ToArray();

        var runId = (string?)document.Root?.Attribute("id");
        Dictionary<string, (string Name, bool Native)>? linkedDefinitions = null;
        Dictionary<string, string>? nativeIds = null;
        if (definitions.Any(definition => UsesNativeXunitEncoding(definition, runId)))
        {
            linkedDefinitions = new Dictionary<string, (string Name, bool Native)>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                var id = RequiredAttribute(definition, "id", path);
                var name = RequiredAttribute(definition, "name", path);
                if (!linkedDefinitions.TryAdd(id, (name, UsesNativeXunitEncoding(definition, runId))))
                {
                    throw new InvalidDataException($"TRX contains duplicate definition ID '{id}'.");
                }
            }

            // WHY: Match forward-encoded committed IDs, never guess which escape
            // sequences in arbitrary reporter text should become another test ID.
            nativeIds = new Dictionary<string, string>(StringComparer.Ordinal);

            // WHY: Keep dictionary insertion and collision rejection adjacent, outside a stateful LINQ predicate.
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var expectedId in expectedIds)
            {
                if (!nativeIds.TryAdd(EncodeNativeXunitDisplayName(expectedId), expectedId))
                {
                    throw new InvalidDataException("Native TRX encoding collides for two expected test IDs.");
                }
            }
        }

        return
        [
            .. document
                .Descendants()
                .Where(element => element.Name.LocalName == "UnitTestResult")
                .Select(element =>
                {
                    var name = RequiredAttribute(element, "testName", path);
                    var testId = NormalizeTestId(name);
                    if (linkedDefinitions is not null
                        && nativeIds is not null)
                    {
                        var definitionId = RequiredAttribute(element, "testId", path);
                        if (!linkedDefinitions.TryGetValue(definitionId, out var definition)
                            || !string.Equals(definition.Name, name, StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(
                                "Native TRX result does not match its exact test definition.");
                        }

                        if (definition.Native)
                        {
                            // Non-specification live results remain outside this exact
                            // specification inventory, but their linkage is still checked.
                            testId = name;
                            if (DiscoveryContract.IsSpecificationTestId(name))
                            {
                                if (!nativeIds.TryGetValue(name, out var canonicalId))
                                {
                                    throw new InvalidDataException(
                                        $"Native TRX identity is not an exact encoded discovery ID: '{name}'.");
                                }

                                testId = canonicalId;
                            }
                        }
                    }

                    return new TrxTestResult(testId, RequiredAttribute(element, "outcome", path));
                }),
        ];
    }

    /// <summary>Recognizes the native xUnit reporter through its run-bound adapter identity.</summary>
    private static bool UsesNativeXunitEncoding(
        XElement definition,
        string? runId
    )
    {
        var adapter = (string?)definition
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "TestMethod")
            ?.Attribute("adapterTypeName");

        if (adapter?.Contains("/xunit.v3/", StringComparison.Ordinal) != true)
        {
            return false;
        }

        if (!Uri.TryCreate(adapter, UriKind.Absolute, out var identity)
            || identity.Scheme != "executor"
            || !string.Equals(identity.Host, runId, StringComparison.OrdinalIgnoreCase)
            || !identity.AbsolutePath.StartsWith("/xunit.v3/", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Native xUnit TRX adapter is not bound to its test run.");
        }

        return true;
    }

    /// <summary>Requires one explicit XML identity component rather than inferring missing metadata.</summary>
    private static string RequiredAttribute(
        XElement element,
        string name,
        string path
    ) => (string?)element.Attribute(name)
        ?? throw new InvalidDataException($"TRX result in '{path}' has no {name}.");

    /// <summary>Reproduces the native reporter's exact display-name transport.</summary>
    /// <remarks>
    /// Matches xUnit XmlUtility.Escape at commit 384aa7f0b49f7c41e41df57516afd79de83359db
    /// (MIT source). That internal utility is not a reusable public API.
    /// </remarks>
    private static string EncodeNativeXunitDisplayName(
        string value
    )
    {
        var encoded = new StringBuilder(value.Length + 20);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsSurrogatePair(value, index))
            {
                encoded
                    .Append(character)
                    .Append(value[++index]);

                continue;
            }

            var escaped = character switch
            {
                '\0' => @"\0",
                '\a' => @"\a",
                '\b' => @"\b",
                '\f' => @"\f",
                '\n' => @"\n",
                '\r' => @"\r",
                '\t' => @"\t",
                '\v' => @"\v",
                '"' => "\\\"",
                '\\' => @"\\",
                _ => null,
            };

            if (escaped is not null)
            {
                encoded.Append(escaped);
            }
            else if (character < 32)
            {
                encoded
                    .Append(@"\x")
                    .Append(((int)character).ToString("x2", CultureInfo.InvariantCulture));
            }
            else if (char.IsSurrogate(character)
                || character is '\uFFFE' or '\uFFFF')
            {
                encoded
                    .Append(@"\x")
                    .Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                encoded.Append(character);
            }
        }

        return encoded.ToString();
    }

    /// <summary>
    /// Normalizes supplementary Unicode characters that VSTest serializes as adjacent
    /// UTF-16 surrogate escapes in TRX display names.
    /// </summary>
    /// <remarks>
    /// BMP escapes remain untouched because test display names can legitimately contain
    /// JSON text such as <c>\u2764</c>. Decoding only complete surrogate pairs makes the
    /// TRX representation comparable with discovery without changing test identity.
    /// </remarks>
    private static string NormalizeTestId(
        string testId
    )
    {
        StringBuilder? normalized = null;
        var copiedUntil = 0;

        for (var index = 0; index <= testId.Length - 12; index++)
        {
            if (!TryReadEscapedSurrogate(testId, index, out var highSurrogate)
                || !char.IsHighSurrogate(highSurrogate)
                || !TryReadEscapedSurrogate(testId, index + 6, out var lowSurrogate)
                || !char.IsLowSurrogate(lowSurrogate))
            {
                continue;
            }

            normalized ??= new StringBuilder(testId.Length);
            normalized.Append(testId, copiedUntil, index - copiedUntil);
            normalized.Append(highSurrogate);
            normalized.Append(lowSurrogate);

            index += 11;
            copiedUntil = index + 1;
        }

        if (normalized is null)
        {
            return testId;
        }

        normalized.Append(testId, copiedUntil, testId.Length - copiedUntil);
        return normalized.ToString();
    }

    private static bool TryReadEscapedSurrogate(
        string value,
        int index,
        out char surrogate
    )
    {
        surrogate = '\0';

        if (index > value.Length - 6
            || value[index] != '\\'
            || value[index + 1] != 'u')
        {
            return false;
        }

        if (!ushort.TryParse(
            value.AsSpan(index + 2, 4),
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture,
            out var codeUnit))
        {
            return false;
        }

        surrogate = (char)codeUnit;
        return true;
    }

    private static HashSet<string> LoadPermittedNotExecuted(
        string dispositionPath,
        DiscoveryContractDocument discovery,
        string target,
        List<string> errors
    )
    {
        using var document = JsonDocument.Parse(File.ReadAllText(dispositionPath));
        var permitted = new HashSet<string>(StringComparer.Ordinal);
        var root = document.RootElement;
        if (!root.TryGetProperty("schemaVersion", out var schemaVersion)
            || schemaVersion.GetInt32() != DispositionSchemaVersion)
        {
            errors.Add($"Disposition schema must be {DispositionSchemaVersion}.");
        }

        foreach (var disposition in root
            .GetProperty("activeDispositions")
            .EnumerateArray())
        {
            var targets = disposition
                .GetProperty("targets")
                .EnumerateArray()
                .Select(element => element.GetString()!)
                .ToArray();

            var dispositionId = disposition
                .GetProperty("id")
                .GetString()!;

            var suite = disposition
                .GetProperty("suite")
                .GetString();

            var fixture = disposition
                .GetProperty("fixture")
                .GetString();

            if (string.IsNullOrWhiteSpace(suite)
                || string.IsNullOrWhiteSpace(fixture))
            {
                errors.Add($"Disposition '{dispositionId}' requires a suite and fixture.");
                continue;
            }

            if (!disposition.TryGetProperty("discoveredTestIds", out var discoveredIds))
            {
                errors.Add($"Disposition '{dispositionId}' has no discoveredTestIds contract.");
                continue;
            }

            var ids = discoveredIds
                .EnumerateArray()
                .Select(element => element.GetString()!)
                .ToArray();

            if (ids.Length == 0)
            {
                errors.Add($"Disposition '{dispositionId}' has no exact discovered test IDs.");
            }

            // WHY: Keep each duplicate's group key attached to its disposition diagnostic.
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var duplicate in ids
                .GroupBy(id => id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key))
            {
                errors.Add($"Disposition '{dispositionId}' repeats discovery ID '{duplicate}'.");
            }

            var methodNames = disposition
                .GetProperty("testMethods")
                .EnumerateArray()
                .Select(element => element.GetString()!)
                .Select(method => method[(method.IndexOf('.', StringComparison.Ordinal) + 1)..])
                .ToArray();

            foreach (var id in ids)
            {
                if (!id.StartsWith($"{fixture}.", StringComparison.Ordinal))
                {
                    errors.Add(
                        $"Disposition '{dispositionId}' discovery ID '{id}' does not belong "
                        + $"to fixture '{fixture}'.");
                }

                if (!methodNames.Any(method => IsMethodDisplayId(id, method)))
                {
                    errors.Add(
                        $"Disposition '{dispositionId}' discovery ID '{id}' has no matching " + "testMethods entry.");
                }
            }

            // WHY: Preserve one explicit diagnostic for each declared method without an exact display ID.
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var methodName in methodNames)
            {
                if (!ids.Any(id => IsMethodDisplayId(id, methodName)))
                {
                    errors.Add(
                        $"Disposition '{dispositionId}' method '{methodName}' has no exact " + "discovered test ID.");
                }
            }

            foreach (var dispositionTarget in targets)
            {
                var targetExpectation = discovery.Targets.FirstOrDefault(item => item.Target == dispositionTarget);
                if (targetExpectation is null)
                {
                    errors.Add($"Disposition '{dispositionId}' references unknown target " + $"'{dispositionTarget}'.");
                    continue;
                }

                // WHY: Retain the selected target in each individual missing-ID diagnostic.
                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var id in ids)
                {
                    if (!targetExpectation.TestIds.Contains(id, StringComparer.Ordinal))
                    {
                        errors.Add(
                            $"Disposition '{dispositionId}' references discovery ID '{id}' "
                            + $"which is absent for target '{dispositionTarget}'.");
                    }
                }
            }

            if (targets.Contains(target, StringComparer.Ordinal))
            {
                // WHY: Keep set insertion and conflicting-disposition rejection outside a stateful LINQ predicate.
                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var id in ids)
                {
                    if (!permitted.Add(id))
                    {
                        errors.Add($"Discovery ID '{id}' is claimed by multiple active dispositions.");
                    }
                }
            }
        }

        return permitted;
    }

    private static bool IsMethodDisplayId(
        string testId,
        string methodName
    )
    {
        var marker = $".{methodName}";
        var methodStart = testId.LastIndexOf(marker, StringComparison.Ordinal);
        if (methodStart < 0)
        {
            return false;
        }

        var suffix = methodStart + marker.Length;
        return suffix == testId.Length || testId[suffix] == '(';
    }
}

internal sealed record TrxTestResult(
    string TestId,
    string Outcome
);

internal sealed record TrxContractReport(
    int Total,
    int Passed,
    int NotExecuted,
    int Failed,
    IReadOnlyList<string> Errors
)
{
    internal bool IsValid => Errors.Count == 0;
}
