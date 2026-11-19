namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectIntegrationTests
{
    /// <summary>Inserts 14,000 complete fifty-column rows with one command and verifies every persisted field.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Fourteen_thousand_fifty_column_rows_preserve_every_value_without_a_source_reader(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);

        // WHY: The source is generated in SQL, avoiding an artificial managed 14,000-entity preload in the measurement fixture.
        await ExecuteNativeAsync(store.Observer,
            """
            CREATE TABLE InsertDigits (`Digit` INT PRIMARY KEY) ENGINE=InnoDB;
            INSERT INTO InsertDigits VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9);
            INSERT INTO InsertWideSource (
            `Id`, `C01`, `C02`, `C03`, `C04`, `C05`, `C06`, `C07`, `C08`,
            `C09`, `C10`, `C11`, `C12`, `C13`, `C14`, `C15`, `C16`, `C17`,
            `C18`, `C19`, `C20`, `C21`, `C22`, `C23`, `C24`, `C25`, `C26`,
            `C27`, `C28`, `C29`, `C30`, `C31`, `C32`, `C33`, `C34`, `C35`,
            `C36`, `C37`, `C38`, `C39`, `C40`, `C41`, `C42`, `C43`, `C44`,
            `C45`, `C46`, `C47`, `C48`, `C49`
            )
            SELECT
            number, number * 100 + 1, number * 100 + 2, number * 100 + 3, number * 100 + 4,
            number * 100 + 5, number * 100 + 6, number * 100 + 7, number * 100 + 8, number * 100 + 9,
            number * 100 + 10, number * 100 + 11, number * 100 + 12, number * 100 + 13, number * 100 + 14,
            number * 100 + 15, number * 100 + 16, number * 100 + 17, number * 100 + 18, number * 100 + 19,
            number * 100 + 20, number * 100 + 21, number * 100 + 22, number * 100 + 23, number * 100 + 24,
            number * 100 + 25, number * 100 + 26, number * 100 + 27, number * 100 + 28, number * 100 + 29,
            number * 100 + 30, number * 100 + 31, number * 100 + 32, number * 100 + 33, number * 100 + 34,
            number * 100 + 35, number * 100 + 36, number * 100 + 37, number * 100 + 38, number * 100 + 39,
            number * 100 + 40, number * 100 + 41, number * 100 + 42, number * 100 + 43, number * 100 + 44,
            number * 100 + 45, number * 100 + 46, number * 100 + 47, number * 100 + 48, number * 100 + 49
            FROM (
                SELECT 1 + a.Digit + 10*b.Digit + 100*c.Digit + 1000*d.Digit + 10000*e.Digit AS number
                FROM InsertDigits a CROSS JOIN InsertDigits b CROSS JOIN InsertDigits c
                    CROSS JOIN InsertDigits d CROSS JOIN InsertDigits e
            ) numbers
            WHERE number <= 14000;
            """,
            safety.Token);

        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        var before = GC.GetTotalAllocatedBytes(precise: true);

        // Act
        var count = await context
            .Set<WideSource>()
            .ExecuteInsertFromQueryAsync(context.Set<WideTarget>(),
                setters => setters
                    .SetProperty(row => row.Id, row => row.Id)
                    .SetProperty(row => row.C01, row => row.C01)
                    .SetProperty(row => row.C02, row => row.C02)
                    .SetProperty(row => row.C03, row => row.C03)
                    .SetProperty(row => row.C04, row => row.C04)
                    .SetProperty(row => row.C05, row => row.C05)
                    .SetProperty(row => row.C06, row => row.C06)
                    .SetProperty(row => row.C07, row => row.C07)
                    .SetProperty(row => row.C08, row => row.C08)
                    .SetProperty(row => row.C09, row => row.C09)
                    .SetProperty(row => row.C10, row => row.C10)
                    .SetProperty(row => row.C11, row => row.C11)
                    .SetProperty(row => row.C12, row => row.C12)
                    .SetProperty(row => row.C13, row => row.C13)
                    .SetProperty(row => row.C14, row => row.C14)
                    .SetProperty(row => row.C15, row => row.C15)
                    .SetProperty(row => row.C16, row => row.C16)
                    .SetProperty(row => row.C17, row => row.C17)
                    .SetProperty(row => row.C18, row => row.C18)
                    .SetProperty(row => row.C19, row => row.C19)
                    .SetProperty(row => row.C20, row => row.C20)
                    .SetProperty(row => row.C21, row => row.C21)
                    .SetProperty(row => row.C22, row => row.C22)
                    .SetProperty(row => row.C23, row => row.C23)
                    .SetProperty(row => row.C24, row => row.C24)
                    .SetProperty(row => row.C25, row => row.C25)
                    .SetProperty(row => row.C26, row => row.C26)
                    .SetProperty(row => row.C27, row => row.C27)
                    .SetProperty(row => row.C28, row => row.C28)
                    .SetProperty(row => row.C29, row => row.C29)
                    .SetProperty(row => row.C30, row => row.C30)
                    .SetProperty(row => row.C31, row => row.C31)
                    .SetProperty(row => row.C32, row => row.C32)
                    .SetProperty(row => row.C33, row => row.C33)
                    .SetProperty(row => row.C34, row => row.C34)
                    .SetProperty(row => row.C35, row => row.C35)
                    .SetProperty(row => row.C36, row => row.C36)
                    .SetProperty(row => row.C37, row => row.C37)
                    .SetProperty(row => row.C38, row => row.C38)
                    .SetProperty(row => row.C39, row => row.C39)
                    .SetProperty(row => row.C40, row => row.C40)
                    .SetProperty(row => row.C41, row => row.C41)
                    .SetProperty(row => row.C42, row => row.C42)
                    .SetProperty(row => row.C43, row => row.C43)
                    .SetProperty(row => row.C44, row => row.C44)
                    .SetProperty(row => row.C45, row => row.C45)
                    .SetProperty(row => row.C46, row => row.C46)
                    .SetProperty(row => row.C47, row => row.C47)
                    .SetProperty(row => row.C48, row => row.C48)
                    .SetProperty(row => row.C49, row => row.C49),
                safety.Token);

        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        await using var command = new MySqlCommand(
            """
            SELECT
            `Id`, `C01`, `C02`, `C03`, `C04`, `C05`, `C06`, `C07`, `C08`,
            `C09`, `C10`, `C11`, `C12`, `C13`, `C14`, `C15`, `C16`, `C17`,
            `C18`, `C19`, `C20`, `C21`, `C22`, `C23`, `C24`, `C25`, `C26`,
            `C27`, `C28`, `C29`, `C30`, `C31`, `C32`, `C33`, `C34`, `C35`,
            `C36`, `C37`, `C38`, `C39`, `C40`, `C41`, `C42`, `C43`, `C44`,
            `C45`, `C46`, `C47`, `C48`, `C49`
            FROM InsertWideTarget ORDER BY `Id`;
            """,
            store.Observer);

        var rows = 0;
        var mismatch = false;
        int fields;

        await using (var reader = await command.ExecuteReaderAsync(safety.Token))
        {
            fields = reader.FieldCount;

            while (await reader.ReadAsync(safety.Token))
            {
                rows++;
                mismatch |= reader.GetInt32(0) != rows;

                for (var column = 1; column < 50; column++)
                {
                    mismatch |= reader.GetInt32(column) != rows * 100 + column;
                }
            }
        }

        var statement = commands.Statements[0];

        // WHY: Explain the exact parameter-free captured INSERT after its reader is closed.
        // FORMAT=JSON observes optimization only; ANALYZE would execute the write again.
        await using var explain = new MySqlCommand("EXPLAIN FORMAT=JSON " + statement, store.Observer);
        var nativePlan =
            Convert.ToString(await explain.ExecuteScalarAsync(safety.Token), CultureInfo.InvariantCulture)!;

        using var plan = JsonDocument.Parse(nativePlan);

        // WHY: This process-wide delta is diagnostic, includes async/runtime activity, and is never an allocation or time gate.
        _output.WriteLine(
            $"INSERT SELECT 14,000 rows x 50 columns: {store.Version}; native count={count}; process allocation delta={allocated}; provider nonqueries={commands.Statements.Count}; readers={commands.Readers}.");

        _output.WriteLine($"INSERT SELECT native JSON plan: {store.Version}; SQL={statement}; plan={nativePlan}");

        // Assert
        Assert.Equal(14000, count);
        Assert.Equal(14000, rows);
        Assert.False(mismatch);
        Assert.Equal(50, fields);
        Assert.Single(commands.Statements);
        Assert.Empty(Assert.Single(commands.Parameters));
        Assert.Equal(0, commands.Readers);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.StartsWith("INSERT INTO `InsertWideTarget`", statement, StringComparison.Ordinal);
        Assert.Contains("FROM `InsertWideSource`", statement, StringComparison.Ordinal);
        string? physicalSource = null;

        if (!plan.RootElement.TryGetProperty("query_block", out var queryBlock))
        {
            // WHY: MySQL JSON v2 uses query_plan with an explicit version marker; JSON v1 and MariaDB use query_block.
            // Source: https://dev.mysql.com/doc/refman/9.7/en/explain.html
            Assert.Equal("2.0",
                plan
                    .RootElement
                    .GetProperty("json_schema_version")
                    .GetString());

            Assert.Equal("insert",
                plan
                    .RootElement
                    .GetProperty("query_type")
                    .GetString());

            Assert.True(plan.RootElement.TryGetProperty("query_plan", out queryBlock));
            physicalSource = "InsertWideSource";
        }

        Assert.Equal(JsonValueKind.Object, queryBlock.ValueKind);

        // WHY: The exact SQL binds the physical source; optimizer aliases, access methods, costs and row estimates are not stable contracts.
        Assert.True(HasPlannedSourceTable(queryBlock, physicalSource));
    }

    /// <summary>Requires a source-read node rather than the INSERT target, without fixing optimizer aliases or access paths.</summary>
    /// <param name="element">The current node of the recognized native JSON plan.</param>
    /// <param name="physicalSource">The physical source name for JSON v2, or null for JSON v1's alias-based table nodes.</param>
    private static bool HasPlannedSourceTable(
        JsonElement element,
        string? physicalSource
    )
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("table_name")
                    && property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString() is { Length: > 0 } name
                    && (physicalSource is null
                        ? name != "InsertWideTarget"
                        : name == physicalSource))
                {
                    return true;
                }

                if (HasPlannedSourceTable(property.Value, physicalSource))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (HasPlannedSourceTable(item, physicalSource))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Keeps the fifty-column fixture concrete so all assignments are regular compile-time typed expressions.</summary>
    private static void ConfigureWideModel(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder
            .Entity<WideSource>()
            .ToTable("InsertWideSource")
            .UseStorageEngine("InnoDB");

        modelBuilder
            .Entity<WideSource>()
            .Property(row => row.Id)
            .ValueGeneratedNever();

        modelBuilder
            .Entity<WideTarget>()
            .ToTable("InsertWideTarget")
            .UseStorageEngine("InnoDB");

        modelBuilder
            .Entity<WideTarget>()
            .Property(row => row.Id)
            .ValueGeneratedNever();
    }

    private sealed class WideSource
    {
        public int Id { get; set; }

        public int C01 { get; set; }

        public int C02 { get; set; }

        public int C03 { get; set; }

        public int C04 { get; set; }

        public int C05 { get; set; }

        public int C06 { get; set; }

        public int C07 { get; set; }

        public int C08 { get; set; }

        public int C09 { get; set; }

        public int C10 { get; set; }

        public int C11 { get; set; }

        public int C12 { get; set; }

        public int C13 { get; set; }

        public int C14 { get; set; }

        public int C15 { get; set; }

        public int C16 { get; set; }

        public int C17 { get; set; }

        public int C18 { get; set; }

        public int C19 { get; set; }

        public int C20 { get; set; }

        public int C21 { get; set; }

        public int C22 { get; set; }

        public int C23 { get; set; }

        public int C24 { get; set; }

        public int C25 { get; set; }

        public int C26 { get; set; }

        public int C27 { get; set; }

        public int C28 { get; set; }

        public int C29 { get; set; }

        public int C30 { get; set; }

        public int C31 { get; set; }

        public int C32 { get; set; }

        public int C33 { get; set; }

        public int C34 { get; set; }

        public int C35 { get; set; }

        public int C36 { get; set; }

        public int C37 { get; set; }

        public int C38 { get; set; }

        public int C39 { get; set; }

        public int C40 { get; set; }

        public int C41 { get; set; }

        public int C42 { get; set; }

        public int C43 { get; set; }

        public int C44 { get; set; }

        public int C45 { get; set; }

        public int C46 { get; set; }

        public int C47 { get; set; }

        public int C48 { get; set; }

        public int C49 { get; set; }
    }

    private sealed class WideTarget
    {
        public int Id { get; set; }

        public int C01 { get; set; }

        public int C02 { get; set; }

        public int C03 { get; set; }

        public int C04 { get; set; }

        public int C05 { get; set; }

        public int C06 { get; set; }

        public int C07 { get; set; }

        public int C08 { get; set; }

        public int C09 { get; set; }

        public int C10 { get; set; }

        public int C11 { get; set; }

        public int C12 { get; set; }

        public int C13 { get; set; }

        public int C14 { get; set; }

        public int C15 { get; set; }

        public int C16 { get; set; }

        public int C17 { get; set; }

        public int C18 { get; set; }

        public int C19 { get; set; }

        public int C20 { get; set; }

        public int C21 { get; set; }

        public int C22 { get; set; }

        public int C23 { get; set; }

        public int C24 { get; set; }

        public int C25 { get; set; }

        public int C26 { get; set; }

        public int C27 { get; set; }

        public int C28 { get; set; }

        public int C29 { get; set; }

        public int C30 { get; set; }

        public int C31 { get; set; }

        public int C32 { get; set; }

        public int C33 { get; set; }

        public int C34 { get; set; }

        public int C35 { get; set; }

        public int C36 { get; set; }

        public int C37 { get; set; }

        public int C38 { get; set; }

        public int C39 { get; set; }

        public int C40 { get; set; }

        public int C41 { get; set; }

        public int C42 { get; set; }

        public int C43 { get; set; }

        public int C44 { get; set; }

        public int C45 { get; set; }

        public int C46 { get; set; }

        public int C47 { get; set; }

        public int C48 { get; set; }

        public int C49 { get; set; }
    }
}
