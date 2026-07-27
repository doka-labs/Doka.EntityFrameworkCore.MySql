namespace Doka.EntityFrameworkCore.MySql.AdrValidator;

internal sealed record AdrDocument(
    string Id,
    string Title,
    string Status,
    DateOnly Date,
    string RelativePath,
    IReadOnlyList<string> Supersedes,
    IReadOnlyList<string> SupersededBy,
    IReadOnlyList<string> Amends,
    IReadOnlyList<string> AmendedBy
);

internal sealed record AdrValidationError(
    string RelativePath,
    int? Line,
    string Message
)
{
    public override string ToString() =>
        Line is null ? $"{RelativePath}: {Message}" : $"{RelativePath}:{Line}: {Message}";
}

internal sealed record AdrParseResult(
    AdrDocument? Document,
    IReadOnlyList<AdrValidationError> Errors
);

internal sealed record AdrValidationReport(
    IReadOnlyList<AdrDocument> Documents,
    IReadOnlyList<AdrValidationError> Errors
)
{
    public bool IsValid => Errors.Count == 0;
}
