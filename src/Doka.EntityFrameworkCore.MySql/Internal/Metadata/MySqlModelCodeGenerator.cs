namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlModelCodeGenerator : IModelCodeGenerator
{
    private static readonly Regex s_dbSetPropertyRegex = DbSetPropertyRegex();

    private readonly IModelCodeGenerator _innerGenerator;

    public MySqlModelCodeGenerator(
        IModelCodeGenerator innerGenerator
    )
    {
        _innerGenerator = innerGenerator ?? throw new ArgumentNullException(nameof(innerGenerator));
    }

    public string Language => _innerGenerator.Language ?? "C#";

    public ScaffoldedModel GenerateModel(
        IModel model,
        ModelCodeGenerationOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);

        var scaffoldedModel = _innerGenerator.GenerateModel(model, options);

        scaffoldedModel.ContextFile.Code = RewriteDbSetProperties(scaffoldedModel.ContextFile.Code);

        if (options.UseNullableReferenceTypes)
        {
            scaffoldedModel.ContextFile.Code = EnsureNullableEnable(scaffoldedModel.ContextFile.Code);

            foreach (var additionalFile in scaffoldedModel.AdditionalFiles)
            {
                additionalFile.Code = EnsureNullableEnable(additionalFile.Code);
            }
        }

        return scaffoldedModel;
    }

    private static string EnsureNullableEnable(
        string code
    )
    {
        ArgumentNullException.ThrowIfNull(code);

        const string nullableEnableDirective = "#nullable enable";

        if (code.StartsWith(nullableEnableDirective, StringComparison.Ordinal))
        {
            return code;
        }

        return nullableEnableDirective + Environment.NewLine + Environment.NewLine + code;
    }

    private static string RewriteDbSetProperties(
        string code
    )
    {
        ArgumentNullException.ThrowIfNull(code);

        return s_dbSetPropertyRegex.Replace(
            code,
            static match =>
            {
                var indent = match.Groups["indent"].Value;
                var entity = match.Groups["entity"].Value;
                var name = match.Groups["name"].Value;

                return FormattableString.Invariant(
                    $"{indent}public virtual DbSet<{entity}> {name} => Set<{entity}>();");
            });
    }

    [GeneratedRegex(
        @"^(?<indent>\s*)public\s+virtual\s+DbSet<(?<entity>[^>]+)>\s+(?<name>\w+)\s+\{\s*get;\s*set;\s*\}\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex DbSetPropertyRegex();
}
