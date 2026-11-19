namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlDefaultTextOptionsIntegrationTests
{
    private const string ElementConverterTable = "DefaultTextElementConverters";

    /// <summary>Reports matched and unmatched membership and its negation for each actual provider representation.</summary>
    public static TheoryData<string, string, bool, bool> ElementConverterCases()
    {
        var data = new TheoryData<string, string, bool, bool>();

        foreach (var target in SelectedTargets)
        {
            foreach (var representation in new[] { "string-int", "bool-int", "bool-string" })
            {
                data.Add(target, representation, true, false);
                data.Add(target, representation, false, false);
                data.Add(target, representation, true, true);
                data.Add(target, representation, false, true);
            }
        }

        return data;
    }

    /// <summary>Primitive collection membership and materialization honor the converter's persisted scalar representation.</summary>
    /// <param name="target">The selected engine profile.</param>
    /// <param name="representation">The actual element conversion configured in the EF model.</param>
    /// <param name="matches">Whether the requested item exists in the persisted collection.</param>
    /// <param name="negated">Whether membership is negated in LINQ.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(ElementConverterCases))]
    public async Task Enabled_defaults_preserve_converted_element_membership_and_readback(
        string target,
        string representation,
        bool matches,
        bool negated
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await using var context = new ElementConverterContext(CreateSpecialOptions<ElementConverterContext>(database));
        await CreateSpecialSchemaAsync(context, CancellationToken.None);
        context.Add(new ElementConverterRow
        {
            Id = 1,
            NumericStrings = ["1", "2"],
            NumericFlags = [true],
            TextFlags = [true],
        });

        await context.SaveChangesAsync(CancellationToken.None);
        var rows = context.Set<ElementConverterRow>();
        var stringItem = matches
            ? "2"
            : "3";

        var boolItem = matches;
        var query = representation switch
        {
            "string-int" => negated
                ? rows.Where(row => !row.NumericStrings.Contains(stringItem))
                : rows.Where(row => row.NumericStrings.Contains(stringItem)),
            "bool-int" => negated
                ? rows.Where(row => !row.NumericFlags.Contains(boolItem))
                : rows.Where(row => row.NumericFlags.Contains(boolItem)),
            "bool-string" => negated
                ? rows.Where(row => !row.TextFlags.Contains(boolItem))
                : rows.Where(row => row.TextFlags.Contains(boolItem)),
            _ => throw new ArgumentOutOfRangeException(nameof(representation)),
        };

        var column = representation switch
        {
            "string-int" => nameof(ElementConverterRow.NumericStrings),
            "bool-int" => nameof(ElementConverterRow.NumericFlags),
            "bool-string" => nameof(ElementConverterRow.TextFlags),
            _ => throw new ArgumentOutOfRangeException(nameof(representation)),
        };

        // Act
        var ids = await query
            .Select(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        var stored = await rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        var nativeKind = await ReadScalarAsync(database.Connection,
            $"SELECT JSON_TYPE(JSON_EXTRACT(`{column}`, '$[0]')) FROM `{ElementConverterTable}` WHERE `Id` = 1;",
            CancellationToken.None);

        // Assert
        Assert.Equal(matches != negated
                ? 1
                : 0,
            ids.Length);

        Assert.All(ids, static id => Assert.Equal(1, id));
        Assert.Equal(["1", "2"], stored.NumericStrings);
        Assert.Equal([true], stored.NumericFlags);
        Assert.Equal([true], stored.TextFlags);
        Assert.Equal(representation == "bool-string"
                ? "STRING"
                : "INTEGER",
            nativeKind);
    }

    private sealed class ElementConverterContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public ElementConverterContext(
            DbContextOptions<ElementConverterContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<ElementConverterRow>(entity =>
        {
            entity.ToTable(ElementConverterTable);
            entity.HasKey(row => row.Id);
            entity
                .Property(row => row.Id)
                .ValueGeneratedNever();

            // WHY: Dispatch must follow these real provider scalars rather than the CLR element;
            // manually attaching a mismatched mapping would bypass EF's converter configuration.
            entity
                .PrimitiveCollection(row => row.NumericStrings)
                .ElementType(element => element.HasConversion(
                    new ValueConverter<string, int>(value => int.Parse(value, CultureInfo.InvariantCulture),
                        value => value.ToString(CultureInfo.InvariantCulture))));

            entity
                .PrimitiveCollection(row => row.NumericFlags)
                .ElementType(element => element.HasConversion(new BoolToZeroOneConverter<int>()));

            entity
                .PrimitiveCollection(row => row.TextFlags)
                .ElementType(element => element.HasConversion(new BoolToStringConverter("false", "true")));
        });
    }

    private sealed class ElementConverterRow
    {
        public int Id { get; set; }

        public List<string> NumericStrings { get; set; } = [];

        public List<bool> NumericFlags { get; set; } = [];

        public List<bool> TextFlags { get; set; } = [];
    }
}
