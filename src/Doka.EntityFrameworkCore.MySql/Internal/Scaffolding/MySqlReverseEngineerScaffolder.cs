namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Defines the lifetime boundary for provider state shared across EF Core's reverse-engineering
/// services. Cleanup runs for successful, failed, and cancelled operations.
/// </summary>
internal sealed class MySqlReverseEngineerScaffolder : IReverseEngineerScaffolder
{
    private readonly IReverseEngineerScaffolder _inner;
    private readonly MySqlScaffoldingContext _scaffoldingContext;

    public MySqlReverseEngineerScaffolder(
        IReverseEngineerScaffolder inner,
        MySqlScaffoldingContext scaffoldingContext
    )
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _scaffoldingContext = scaffoldingContext ?? throw new ArgumentNullException(nameof(scaffoldingContext));
    }

    public ScaffoldedModel ScaffoldModel(
        string connectionString,
        DatabaseModelFactoryOptions databaseOptions,
        ModelReverseEngineerOptions modelOptions,
        ModelCodeGenerationOptions codeOptions
    )
    {
        _scaffoldingContext.Begin();

        try
        {
            return _inner.ScaffoldModel(connectionString, databaseOptions, modelOptions, codeOptions);
        }
        finally
        {
            _scaffoldingContext.Abort();
        }
    }

    public SavedModelFiles Save(
        ScaffoldedModel scaffoldedModel,
        string outputDir,
        bool overwriteFiles
    ) => _inner.Save(scaffoldedModel, outputDir, overwriteFiles);
}
