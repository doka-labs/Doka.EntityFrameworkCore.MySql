namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlDefaultTextOptionsIntegrationTests
{
    private const string CandidateEncodingTable = "DefaultTextCandidateEncoding";

    /// <summary>Reports exact string membership across physical encodings and its negation for each engine.</summary>
    public static TheoryData<string, bool, bool> CandidateEncodingCases()
    {
        var data = new TheoryData<string, bool, bool>();

        foreach (var target in SelectedTargets)
        {
            data.Add(target, true, false);
            data.Add(target, false, false);
            data.Add(target, true, true);
            data.Add(target, false, true);
        }

        return data;
    }

    /// <summary>A real Latin1 candidate column compares decoded Unicode values rather than incompatible binary encodings.</summary>
    /// <param name="target">The selected engine profile.</param>
    /// <param name="matches">Whether the candidate has the exact accent present in the JSON element.</param>
    /// <param name="negated">Whether LINQ negates string membership.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CandidateEncodingCases))]
    public async Task Enabled_defaults_preserve_json_membership_for_a_latin1_candidate_column(
        string target,
        bool matches,
        bool negated
    )
    {
        // Arrange
        const string member = "Caf\u00e9";
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await using var context =
            new CandidateEncodingContext(CreateSpecialOptions<CandidateEncodingContext>(database));

        await CreateSpecialSchemaAsync(context, CancellationToken.None);
        context.Add(new CandidateEncodingRow
        {
            Id = 1,
            Candidate = matches
                ? member
                : "Cafe",
            Tags = [member],
        });

        await context.SaveChangesAsync(CancellationToken.None);

        // WHY: An ordinary SQL comparison under latin1_swedish_ci may ignore the accent;
        // JSON string membership must instead preserve exact Unicode equality across encodings.
        var query = negated
            ? context
                .Set<CandidateEncodingRow>()
                .Where(row => !row.Tags.Contains(row.Candidate))
            : context
                .Set<CandidateEncodingRow>()
                .Where(row => row.Tags.Contains(row.Candidate));

        // Act
        var ids = await query
            .Select(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        var stored = await context
            .Set<CandidateEncodingRow>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        var candidateCharSet = await ReadColumnFactAsync(database.Connection,
            CandidateEncodingTable,
            nameof(CandidateEncodingRow.Candidate),
            true,
            CancellationToken.None);

        var candidateBytes = await ReadScalarAsync(database.Connection,
            $"SELECT HEX(`Candidate`) FROM `{CandidateEncodingTable}` WHERE `Id` = 1;",
            CancellationToken.None);

        // Assert
        Assert.Equal(matches != negated
                ? 1
                : 0,
            ids.Length);

        Assert.All(ids, static id => Assert.Equal(1, id));
        Assert.Equal(matches
                ? member
                : "Cafe",
            stored.Candidate);

        Assert.Equal([member], stored.Tags);
        Assert.Equal("latin1", candidateCharSet);
        Assert.Equal(matches
                ? "436166E9"
                : "43616665",
            candidateBytes);
    }

    private sealed class CandidateEncodingContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public CandidateEncodingContext(
            DbContextOptions<CandidateEncodingContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<CandidateEncodingRow>(entity =>
        {
            entity.ToTable(CandidateEncodingTable);
            entity.HasKey(row => row.Id);
            entity
                .Property(row => row.Id)
                .ValueGeneratedNever();

            entity
                .Property(row => row.Candidate)
                .HasMaxLength(64)
                .UseCollation("latin1_swedish_ci");

            entity.PrimitiveCollection(row => row.Tags);
        });
    }

    private sealed class CandidateEncodingRow
    {
        public int Id { get; set; }

        public string Candidate { get; set; } = string.Empty;

        public List<string> Tags { get; set; } = [];
    }
}
