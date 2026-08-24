namespace Doka.EntityFrameworkCore.MySql.RepositoryContract;

internal sealed record ContractError(
    string RelativePath,
    int? Line,
    string Message
)
{
    public override string ToString() =>
        Line is null ? $"{RelativePath}: {Message}" : $"{RelativePath}:{Line}: {Message}";
}

internal sealed record ContractReport(
    int MarkdownDocumentCount,
    int LocalLinkCount,
    int ExampleCount,
    IReadOnlyList<ContractError> Errors
)
{
    public bool IsValid => Errors.Count == 0;
}
