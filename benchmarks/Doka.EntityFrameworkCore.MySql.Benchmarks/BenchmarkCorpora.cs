namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

internal static class BenchmarkCorpora
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public static TranslationCorpusDto LoadTranslationCorpus() => Load<TranslationCorpusDto>("translation-corpus.json");

    public static MigrationCorpusDto LoadMigrationCorpus() => Load<MigrationCorpusDto>("migration-corpus.json");

    private static TCorpus Load<TCorpus>(
        string fileName
    )
    {
        var path = Path.Combine(AppContext.BaseDirectory, "corpora", fileName);
        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<TCorpus>(json, s_jsonOptions)
            ?? throw new InvalidOperationException($"The benchmark corpus '{fileName}' could not be deserialized.");
    }
}

public sealed record TranslationCorpusDto(
    string CorpusVersion,
    IReadOnlyList<TranslationScenarioDto> Queries
);

public sealed record TranslationScenarioDto(string Id);

public sealed record MigrationCorpusDto(
    string CorpusVersion,
    IReadOnlyList<MigrationScenarioDto> Diffs
);

public sealed record MigrationScenarioDto(string Id);
