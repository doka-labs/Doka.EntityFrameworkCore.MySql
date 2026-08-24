namespace Doka.EntityFrameworkCore.MySql.RepositoryContract;

internal static class RepositoryContractValidator
{
    public static ContractReport Validate(
        string repositoryRoot
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var root = Path.GetFullPath(repositoryRoot);
        var documentation = DocumentationContract.Validate(root);
        var examples = ExampleContractValidator.Validate(root);
        var errors = documentation
            .Errors
            .Concat(examples.Errors)
            .Concat(ImagePinContract.Validate(root))
            .Concat(
                EngineeringSurfaceContract
                    .Validate(root)
                    .Select(static message => new ContractError("engineering", null, message)))
            .ToArray();

        return new ContractReport(documentation.DocumentCount, documentation.LinkCount, examples.ExampleCount, errors);
    }
}
