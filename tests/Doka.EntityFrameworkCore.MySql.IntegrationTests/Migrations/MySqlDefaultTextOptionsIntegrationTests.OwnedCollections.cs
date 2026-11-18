namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlDefaultTextOptionsIntegrationTests
{
    private const string OwnedCollectionTable = "DefaultTextOwnedCollection";

    /// <summary>Reports selected and absent projected members and their negation separately on each engine.</summary>
    public static IEnumerable<TheoryDataRow<string, bool, bool>> OwnedCollectionCases()
    {
        foreach (var target in SelectedTargets)
        {
            yield return new TheoryDataRow<string, bool, bool>(target.Data, true, false);
            yield return new TheoryDataRow<string, bool, bool>(target.Data, false, false);
            yield return new TheoryDataRow<string, bool, bool>(target.Data, true, true);
            yield return new TheoryDataRow<string, bool, bool>(target.Data, false, true);
        }
    }

    /// <summary>Membership over owned JSON objects uses the configured member path and preserves escaped values.</summary>
    /// <param name="target">The selected engine profile.</param>
    /// <param name="matches">Whether the requested member exists in the value projection.</param>
    /// <param name="negated">Whether LINQ negates projected-member membership.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(OwnedCollectionCases))]
    public async Task Enabled_defaults_preserve_owned_json_projected_member_membership_and_readback(
        string target,
        bool matches,
        bool negated
    )
    {
        // Arrange
        const string present = "Caf\u00e9 'value' \U0001F642";
        const string otherOnly = "Caf\u00e9 'other' \U0001F642";
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await using var context = new OwnedCollectionContext(CreateSpecialOptions<OwnedCollectionContext>(database));
        await CreateSpecialSchemaAsync(context, CancellationToken.None);
        context.Add(new OwnedCollectionRow
        {
            Id = 1,
            Items =
            [
                new OwnedCollectionValue
                {
                    Value = present,
                    Other = "different",
                },
                new OwnedCollectionValue
                {
                    Value = "different",
                    Other = otherOnly,
                },
            ],
        });

        await context.SaveChangesAsync(CancellationToken.None);
        var item = matches
            ? present
            : otherOnly;

        // WHY: The negative needle exists in another member of the same JSON array;
        // searching whole objects or dropping the configured lower-case path is incorrect.
        var query = negated
            ? context
                .Set<OwnedCollectionRow>()
                .Where(row => !row
                    .Items
                    .Select(value => value.Value)
                    .Contains(item))
            : context
                .Set<OwnedCollectionRow>()
                .Where(row => row
                    .Items
                    .Select(value => value.Value)
                    .Contains(item));

        // Act
        var ids = await query
            .Select(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        var stored = await context
            .Set<OwnedCollectionRow>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        var nativeValue = await ReadScalarAsync(database.Connection,
            $"SELECT JSON_UNQUOTE(JSON_EXTRACT(`Items`, '$[0].value')) FROM `{OwnedCollectionTable}` WHERE `Id` = 1;",
            CancellationToken.None);

        // Assert
        Assert.Equal(matches != negated
                ? 1
                : 0,
            ids.Length);

        Assert.All(ids, static id => Assert.Equal(1, id));
        Assert.Equal(2, stored.Items.Count);
        Assert.Equal(present, stored.Items[0].Value);
        Assert.Equal("different", stored.Items[0].Other);
        Assert.Equal("different", stored.Items[1].Value);
        Assert.Equal(otherOnly, stored.Items[1].Other);
        Assert.Equal(present, nativeValue);
    }

    private sealed class OwnedCollectionContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public OwnedCollectionContext(
            DbContextOptions<OwnedCollectionContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<OwnedCollectionRow>(entity =>
        {
            entity.ToTable(OwnedCollectionTable);
            entity.HasKey(row => row.Id);
            entity
                .Property(row => row.Id)
                .ValueGeneratedNever();

            entity.OwnsMany(row => row.Items,
                owned =>
                {
                    owned.ToJson();
                    owned
                        .Property(value => value.Value)
                        .HasJsonPropertyName("value");
                });
        });
    }

    private sealed class OwnedCollectionRow
    {
        public int Id { get; set; }

        public List<OwnedCollectionValue> Items { get; set; } = [];
    }

    private sealed class OwnedCollectionValue
    {
        public string Value { get; set; } = string.Empty;

        public string Other { get; set; } = string.Empty;
    }
}
