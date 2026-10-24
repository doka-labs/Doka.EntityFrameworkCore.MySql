namespace Doka.EntityFrameworkCore.MySql.RepositoryContract;

internal static partial class EngineeringSurfaceContract
{
    /// <summary>
    /// Creates a disposable linter view of exact self references without modifying repository sources.
    /// </summary>
    /// <remarks>
    /// The pinned actionlint predates GitHub's self-repository syntax. A full
    /// metadata mirror keeps reusable-workflow and nested-action analysis intact;
    /// ignoring the unsupported reference diagnostics would lose those checks.
    /// </remarks>
    public static IReadOnlyList<string> PrepareActionlintSnapshot(
        string repositoryRoot,
        string destination
    )
    {
        var root = Path.GetFullPath(repositoryRoot);
        var target = Path.GetFullPath(destination);
        var errors = new List<string>();
        if (target.Equals(root, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()
            || Directory.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
        {
            errors.Add("Actionlint snapshot requires an empty, nonsymbolic destination outside the repository.");

            return errors;
        }

        if ((File.GetAttributes(Path.Combine(root, ".github")) & FileAttributes.ReparsePoint) != 0)
        {
            errors.Add(".github: symbolic links are not permitted in the actionlint snapshot.");

            return errors;
        }

        errors.AddRange(ValidateWorkflowConcurrencyFiles(root));
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var directory in new[] { ".github/workflows", ".github/actions" })
        {
            var path = Path.Combine(root, directory);
            if (!Directory.Exists(path))
            {
                continue;
            }

            CollectActionlintMetadata(root, path, contents, errors);
        }

        foreach (var relative in new[] { ".github/actionlint.yaml", ".github/actionlint.yml" })
        {
            var path = Path.Combine(root, relative);
            if (!File.Exists(path))
            {
                continue;
            }

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                errors.Add($"{relative}: symbolic links are not permitted in the actionlint snapshot.");
                continue;
            }

            contents.Add(relative, File.ReadAllText(path));
        }

        foreach (var (relative, content) in contents.ToArray())
        {
            var normalized = NormalizeSelfReferences(root, relative, content, errors);
            contents[relative] = normalized;
            if (relative.StartsWith(".github/actions/", StringComparison.Ordinal)
                && Path.GetFileName(relative) is "action.yml" or "action.yaml")
            {
                AddCompositeLintWorkflow(relative, normalized, contents, errors);
            }
        }

        if (errors.Count != 0)
        {
            return errors;
        }

        // WHY: Emit only after validation, so a failing check cannot leave a
        // partial view that a caller could mistake for the complete workflow graph.
        foreach (var (relative, content) in contents)
        {
            var path = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        // WHY: actionlint uses a .git marker to discover its project root and
        // resolve local callees. This empty temporary directory contains no Git
        // objects, configuration or history and never touches the real checkout.
        Directory.CreateDirectory(Path.Combine(target, ".git"));

        return errors;
    }

    /// <summary>
    /// Collects every YAML metadata file while rejecting links rather than following them.
    /// </summary>
    private static void CollectActionlintMetadata(
        string root,
        string path,
        Dictionary<string, string> contents,
        List<string> errors
    )
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            errors.Add($"{Relative(root, path)}: symbolic links are not permitted in the actionlint snapshot.");

            return;
        }

        foreach (var child in Directory.EnumerateFileSystemEntries(path))
        {
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
            {
                errors.Add($"{Relative(root, child)}: symbolic links are not permitted in the actionlint snapshot.");
                continue;
            }

            if (Directory.Exists(child))
            {
                CollectActionlintMetadata(root, child, contents, errors);
                continue;
            }

            if (Path.GetExtension(child) is ".yml" or ".yaml")
            {
                contents.Add(Relative(root, child), File.ReadAllText(child));
            }
        }
    }

    /// <summary>
    /// Rewrites only validated uses scalars; literal scripts and comments retain their bytes.
    /// </summary>
    private static string NormalizeSelfReferences(
        string root,
        string relative,
        string content,
        List<string> errors
    )
    {
        var lines = content.Split('\n');
        var scalarIndent = -1;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var indent = line.Length - line.TrimStart(' ').Length;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (scalarIndent >= 0 && indent > scalarIndent)
            {
                continue;
            }

            scalarIndent = BlockScalarHeader().IsMatch(line) ? indent : -1;
            var field = SelfReferenceUses().Match(line);
            if (!field.Success)
            {
                continue;
            }

            var raw = field.Groups["value"].Value;
            var reference = UnquoteYamlScalar(TrimYamlComment(raw));
            if (!reference.StartsWith("$/", StringComparison.Ordinal))
            {
                continue;
            }

            if (!ValidateSelfReferenceTarget(root, reference))
            {
                errors.Add(
                    $"{relative}:{index + 1}: self-repository reference requires an exact existing regular target.");
                continue;
            }

            // WHY: Replacing one prefix character preserves line and column
            // positions, while every callee is resolved from the same mirror.
            var offset = field.Groups["value"].Index + (raw.StartsWith('"') || raw.StartsWith('\'') ? 1 : 0);
            lines[index] = line[..offset] + "." + line[(offset + 1)..];
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Resolves only canonical repository metadata paths, without aliases or filesystem links.
    /// </summary>
    private static bool ValidateSelfReferenceTarget(
        string root,
        string reference
    )
    {
        if (!SelfReferencePath().IsMatch(reference)
            || reference.EndsWith('/')
            || reference[2..].Split('/').Any(static segment => segment is "" or "." or ".."))
        {
            return false;
        }

        var current = root;
        foreach (var segment in reference[2..].Split('/'))
        {
            if (!Directory.Exists(current))
            {
                return false;
            }

            var next = Directory.EnumerateFileSystemEntries(current)
                .FirstOrDefault(path => Path.GetFileName(path).Equals(segment, StringComparison.Ordinal));

            if (next is null || (File.GetAttributes(next) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            current = next;
        }

        if (reference.StartsWith("$/.github/workflows/", StringComparison.Ordinal))
        {
            return File.Exists(current);
        }

        return Directory.Exists(current) && Directory.EnumerateFiles(current).Any(path =>
            Path.GetFileName(path) is "action.yml" or "action.yaml"
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0);
    }

    /// <summary>
    /// Exposes composite steps to actionlint's workflow and shell analysis, not just its metadata reader.
    /// </summary>
    private static void AddCompositeLintWorkflow(
        string relative,
        string content,
        Dictionary<string, string> contents,
        List<string> errors
    )
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var runs = Array.IndexOf(lines, "runs:");
        if (runs < 0)
        {
            errors.Add($"{relative}: action metadata requires an explicit block-style runs mapping.");

            return;
        }

        var usingField = lines.Skip(runs + 1)
            .Select(static line => WorkflowScalarField().Match(line))
            .FirstOrDefault(static field => field.Success && UnquoteYamlScalar(field.Groups["key"].Value) == "using");

        var kind = usingField is null ? string.Empty : UnquoteYamlScalar(
            TrimYamlComment(usingField.Groups["value"].Value));

        if (kind is "node12" or "node16" or "node20" or "node24" or "docker")
        {
            return;
        }

        if (kind != "composite")
        {
            errors.Add($"{relative}: action metadata requires one literal recognized runs.using identity.");

            return;
        }

        var steps = Array.FindIndex(lines, runs + 1, static line => line == "  steps:");
        if (steps < 0)
        {
            errors.Add($"{relative}: composite actions require an explicit block-style runs.steps mapping.");

            return;
        }

        var inputNames = new List<string>();
        var inputs = Array.IndexOf(lines, "inputs:");
        for (var index = inputs + 1; inputs >= 0 && index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length != 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith('#'))
            {
                break;
            }

            var input = CompositeInputName().Match(line);
            if (input.Success)
            {
                inputNames.Add(input.Groups["name"].Value);
            }
        }

        var wrapper = new System.Text.StringBuilder();
        wrapper.Append("name: ").Append(JsonSerializer.Serialize($"Lint-only {relative}"))
            .Append("\non:\n  workflow_call:\n");

        if (inputNames.Count != 0)
        {
            wrapper.Append("    inputs:\n");
            foreach (var input in inputNames)
            {
                wrapper.Append("      ").Append(input).Append(":\n        type: string\n");
            }
        }

        // WHY: This synthetic job is only a linter input and never selects a
        // hosted runner. Keep a label the pinned linter knows in isolated fixtures.
        wrapper.Append("jobs:\n  composite:\n    runs-on: ubuntu-24.04\n    steps:\n");
        for (var index = steps + 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (!string.IsNullOrWhiteSpace(line) && line.Length - line.TrimStart(' ').Length <= 2)
            {
                break;
            }

            wrapper.Append("  ").Append(line).Append('\n');
        }

        var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(relative))).ToLowerInvariant();

        var path = $".github/workflows/__actionlint_composite_{identity}.yml";
        if (!contents.TryAdd(path, wrapper.ToString()))
        {
            errors.Add($"{relative}: synthetic composite linter identity collides with repository metadata.");
        }
    }

    [GeneratedRegex("^ *(?:- +)?(?:uses|'uses'|\"uses\"):[ \\t]*(?<value>.*)$")]
    private static partial Regex SelfReferenceUses();

    [GeneratedRegex("^\\$/[.]github/(?:workflows/[A-Za-z0-9_.-]+[.]ya?ml|actions/[A-Za-z0-9_./-]+)$")]
    private static partial Regex SelfReferencePath();

    [GeneratedRegex("^  (?<name>[A-Za-z_][A-Za-z0-9_-]*):[ \\t]*(?:#.*)?$")]
    private static partial Regex CompositeInputName();
}
