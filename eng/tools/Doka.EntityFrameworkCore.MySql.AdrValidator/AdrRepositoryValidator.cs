namespace Doka.EntityFrameworkCore.MySql.AdrValidator;

internal sealed class AdrRepositoryValidator
{
    private const string DecisionsRelativePath = "docs/decisions";

    /// <summary>
    /// Validates the complete repository ADR corpus and its generated indexes.
    /// </summary>
    /// <param name="repositoryRoot">The absolute repository root.</param>
    /// <param name="validateGeneratedArtifacts">
    /// Whether deterministic index files must already match the validated corpus.
    /// </param>
    /// <returns>All usable decisions and every validation diagnostic.</returns>
    public static AdrValidationReport Validate(
        string repositoryRoot,
        bool validateGeneratedArtifacts = true
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var errors = new List<AdrValidationError>();
        var documents = new List<AdrDocument>();
        var decisionsDirectory = Path.Combine(repositoryRoot, DecisionsRelativePath);
        if (!Directory.Exists(decisionsDirectory))
        {
            errors.Add(new AdrValidationError(DecisionsRelativePath, null, "Decision directory does not exist."));
            return new AdrValidationReport(documents, errors);
        }

        var files = Directory
            .GetFiles(decisionsDirectory, "D-*.md")
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            errors.Add(new AdrValidationError(DecisionsRelativePath, null, "No ADR files were found."));
            return new AdrValidationReport(documents, errors);
        }

        foreach (var file in files)
        {
            var relativePath = Path
                .GetRelativePath(repositoryRoot, file)
                .Replace('\\', '/');

            var result = AdrParser.Parse(file, relativePath);
            errors.AddRange(result.Errors);

            if (result.Document is not null)
            {
                documents.Add(result.Document);
            }
        }

        documents.Sort(static (left, right) => string.CompareOrdinal(left.Id, right.Id));

        if (ValidateIdentifiers(documents, errors))
        {
            ValidateRelationships(documents, errors);
        }

        if (validateGeneratedArtifacts && errors.Count == 0)
        {
            ValidateGeneratedArtifact(
                repositoryRoot,
                AdrIndexRenderer.ReadmeRelativePath,
                AdrIndexRenderer.RenderReadme(documents),
                errors);
            ValidateGeneratedArtifact(
                repositoryRoot,
                AdrIndexRenderer.JsonRelativePath,
                AdrIndexRenderer.RenderJson(documents),
                errors);
        }

        return new AdrValidationReport(documents, errors);
    }

    private static bool ValidateIdentifiers(
        List<AdrDocument> documents,
        List<AdrValidationError> errors
    )
    {
        var duplicate = documents
            .GroupBy(static document => document.Id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicate is not null)
        {
            errors.Add(
                new AdrValidationError(
                    duplicate.First()
                        .RelativePath,
                    null,
                    $"Duplicate ADR identifier '{duplicate.Key}'."));

            return false;
        }

        for (var index = 0; index < documents.Count; index++)
        {
            var expected = $"D-{index + 1:000}";
            if (documents[index].Id != expected)
            {
                errors.Add(
                    new AdrValidationError(
                        documents[index].RelativePath,
                        null,
                        $"ADR identifiers must be contiguous; expected '{expected}'."));
            }
        }

        return true;
    }

    private static void ValidateRelationships(
        List<AdrDocument> documents,
        List<AdrValidationError> errors
    )
    {
        var byId = documents.ToDictionary(static document => document.Id, StringComparer.Ordinal);
        foreach (var document in documents)
        {
            ValidateRelationshipSet(
                document,
                document.Supersedes,
                "supersedes",
                static target => target.SupersededBy,
                byId,
                errors);
            ValidateRelationshipSet(
                document,
                document.SupersededBy,
                "superseded-by",
                static target => target.Supersedes,
                byId,
                errors);
            ValidateRelationshipSet(
                document,
                document.Amends,
                "amends",
                static target => target.AmendedBy,
                byId,
                errors);
            ValidateRelationshipSet(
                document,
                document.AmendedBy,
                "amended-by",
                static target => target.Amends,
                byId,
                errors);

            if (document is { Status: "superseded", SupersededBy.Count: 0 })
            {
                errors.Add(
                    new AdrValidationError(
                        document.RelativePath,
                        null,
                        "A superseded ADR must identify its successor."));
            }

            if (document.Status != "superseded"
                && document.SupersededBy.Count > 0)
            {
                errors.Add(
                    new AdrValidationError(
                        document.RelativePath,
                        null,
                        "An ADR with superseded-by metadata must use status 'superseded'."));
            }
        }
    }

    private static void ValidateRelationshipSet(
        AdrDocument source,
        IReadOnlyList<string> targetIds,
        string relationshipName,
        Func<AdrDocument, IReadOnlyList<string>> inverseSelector,
        Dictionary<string, AdrDocument> byId,
        List<AdrValidationError> errors
    )
    {
        foreach (var targetId in targetIds)
        {
            if (targetId == source.Id)
            {
                errors.Add(
                    new AdrValidationError(
                        source.RelativePath,
                        null,
                        $"Relationship '{relationshipName}' cannot reference the same ADR."));
                continue;
            }

            if (!byId.TryGetValue(targetId, out var target))
            {
                errors.Add(
                    new AdrValidationError(
                        source.RelativePath,
                        null,
                        $"Relationship '{relationshipName}' references missing ADR '{targetId}'."));
                continue;
            }

            if (!inverseSelector(target)
                    .Contains(source.Id, StringComparer.Ordinal))
            {
                errors.Add(
                    new AdrValidationError(
                        source.RelativePath,
                        null,
                        $"Relationship '{relationshipName}' to '{targetId}' is not bidirectional."));
            }
        }
    }

    private static void ValidateGeneratedArtifact(
        string repositoryRoot,
        string relativePath,
        string expected,
        List<AdrValidationError> errors
    )
    {
        var path = Path.Combine(repositoryRoot, relativePath);
        if (!File.Exists(path))
        {
            errors.Add(new AdrValidationError(relativePath, null, "Generated decision artifact is missing."));
            return;
        }

        var actual = File.ReadAllText(path);
        if (actual != expected)
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    null,
                    "Generated decision artifact is stale; run 'eng/validate-adrs.sh --write-index'."));
        }
    }
}
