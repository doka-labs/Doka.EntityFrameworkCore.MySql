namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlDefaultTextOptionsIntegrationTests
{
    /// <summary>Reports ordinary and inverse metadata transitions separately for each supported engine.</summary>
    public static TheoryData<string, bool> SchemaTransitionCases
    {
        get
        {
            var data = new TheoryData<string, bool>();

            foreach (var target in SelectedTargets)
            {
                data.Add(target, false);
                data.Add(target, true);
            }

            return data;
        }
    }

    /// <summary>Native text ordering preserves collation semantics and the equality predicate remains index eligible.</summary>
    /// <param name="target">The selected engine profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Text_ordering_uses_native_collation_and_equality_keeps_its_eligible_index(
        string target
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        await using var setup = CreateContext(database);
        await ApplyModelAsync(setup, null, CancellationToken.None);
        setup
            .Set<TextRow>()
            .AddRange(new TextRow
                {
                    Id = 1,
                    Name = "Zebra",
                },
                new TextRow
                {
                    Id = 2,
                    Name = "alpha",
                },
                new TextRow
                {
                    Id = 3,
                    Name = "Alpha",
                },
                new TextRow
                {
                    Id = 4,
                    Name = "bravo",
                },
                new TextRow
                {
                    Id = 5,
                    Name = "\u00e9clair",
                },
                new TextRow
                {
                    Id = 6,
                    Name = "eclair",
                });

        await setup.SaveChangesAsync(CancellationToken.None);
        var recorder = new QueryRecorder();
        await using var context = CreateContext(database, interceptor: recorder);
        var ordering = context
            .Set<TextRow>()
            .AsNoTracking()
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Id)
            .Select(row => row.Id);

        var equalitySql = context
            .Set<TextRow>()
            .Where(row => row.Name == "alpha")
            .Select(row => row.Id)
            .ToQueryString();

        // Act
        var ids = await ordering.ToListAsync(CancellationToken.None);
        var eligibleIndexes = await ReadEligibleIndexesAsync(database.Connection, equalitySql, CancellationToken.None);

        // Assert
        Assert.Equal([2, 3, 4, 5, 6, 1], ids);
        Assert.Contains("IX_DefaultText_Name", eligibleIndexes, StringComparison.Ordinal);
        var sql = Assert.Single(recorder.Statements);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COLLATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SET NAMES", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A real forward or inverse collation migration preserves complete native keys and their enforcement.</summary>
    /// <param name="target">The selected engine profile.</param>
    /// <param name="inverse">Whether the operation restores the original explicit column binding.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SchemaTransitionCases))]
    public async Task Regular_collation_transition_preserves_foreign_keys_indexes_values_and_enforcement(
        string target,
        bool inverse
    )
    {
        // Arrange
        await using var database = await TextDatabase.CreateAsync(ParseTarget(target), CancellationToken.None);
        var schema = new TextSchema
        {
            TableCollation = BinaryCollation,
            ExplicitColumnCollation = true,
        };

        await using var explicitColumn = CreateContext(database, schema);
        await using var inherited = CreateContext(database, schema with { ExplicitColumnCollation = false });
        await ApplyModelAsync(explicitColumn, null, CancellationToken.None);
        explicitColumn.Add(new TextRow
        {
            Id = 1,
            Name = "Caf\u00e9",
            ExternalId = s_externalId,
        });

        await explicitColumn.SaveChangesAsync(CancellationToken.None);
        await ExecuteAsync(database.Connection,
            "CREATE TABLE `DefaultTextChildren` (`Id` int NOT NULL PRIMARY KEY, `ParentId` int NOT NULL, "
            + "KEY `IX_DefaultTextChildren_ParentId` (`ParentId`), "
            + "CONSTRAINT `FK_DefaultTextChildren_Records` FOREIGN KEY (`ParentId`) REFERENCES `DefaultTextRecords` (`Id`) "
            + "ON DELETE RESTRICT ON UPDATE CASCADE) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;",
            CancellationToken.None);

        await ExecuteAsync(database.Connection,
            "INSERT INTO `DefaultTextChildren` (`Id`, `ParentId`) VALUES (1, 1);",
            CancellationToken.None);

        var originalKeys = await ReadNativeKeyDefinitionsAsync(database.Connection, CancellationToken.None);

        if (inverse)
        {
            await ApplyModelAsync(inherited, explicitColumn, CancellationToken.None);
        }

        var targetContext = inverse
            ? explicitColumn
            : inherited;

        var sourceContext = inverse
            ? inherited
            : explicitColumn;

        // Act
        await ApplyModelAsync(targetContext, sourceContext, CancellationToken.None);
        var actualKeys = await ReadNativeKeyDefinitionsAsync(database.Connection, CancellationToken.None);
        var row = await targetContext
            .Set<TextRow>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        var references = await ReadScalarAsync(database.Connection,
            "SELECT COUNT(*) FROM `DefaultTextChildren` AS c JOIN `DefaultTextRecords` AS p ON c.`ParentId` = p.`Id`;",
            CancellationToken.None);

        var violation = await Record.ExceptionAsync(() => ExecuteAsync(database.Connection,
            "INSERT INTO `DefaultTextChildren` (`Id`, `ParentId`) VALUES (2, 999);",
            CancellationToken.None));

        var columnCollation =
            await ReadColumnFactAsync(database.Connection, MainTable, "Name", false, CancellationToken.None);

        // Assert
        Assert.Equal(5, originalKeys.Length);
        Assert.Equal(originalKeys, actualKeys);
        Assert.Equal("Caf\u00e9", row.Name);
        Assert.Equal(s_externalId, row.ExternalId);
        Assert.Equal("1", references);
        Assert.Equal(1452,
            Assert.IsType<MySqlException>(violation)
                .Number);

        Assert.Equal(inverse
                ? UnicodeCollation
                : BinaryCollation,
            columnCollation);
    }

    /// <summary>Reports candidate index names without asserting hardware-sensitive optimizer costs or plan selection.</summary>
    private static async Task<string> ReadEligibleIndexesAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        // WHY: MySQL 9.7 defaults to TREE output; explicitly select the common tabular
        // contract instead of changing server/session settings or parsing engine-specific trees.
        await using var command = new MySqlCommand("EXPLAIN FORMAT=TRADITIONAL " + sql, connection);
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var candidates = new List<string>();

        while (await reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            var ordinal = reader.GetOrdinal("possible_keys");

            if (!reader.IsDBNull(ordinal))
            {
                candidates.Add(reader.GetString(ordinal));
            }
        }

        // WHY: Eligibility proves the native predicate still exposes its backing index;
        // actual chosen plans and estimated costs may legitimately vary with statistics.
        return string.Join(",", candidates);
    }

    /// <summary>Captures complete native FK and index declarations while excluding optional column charset spelling.</summary>
    private static async Task<string[]> ReadNativeKeyDefinitionsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        var definitions = new List<string>();

        foreach (var table in new[] { MainTable, "DefaultTextChildren" })
        {
            var sql = await ReadCreateTableAsync(connection, table, cancellationToken)
                .ConfigureAwait(false);

            foreach (var line in sql.Split('\n', StringSplitOptions.TrimEntries))
            {
                if (line.StartsWith("PRIMARY KEY", StringComparison.Ordinal)
                    || line.StartsWith("KEY ", StringComparison.Ordinal)
                    || line.StartsWith("UNIQUE KEY ", StringComparison.Ordinal)
                    || line.StartsWith("CONSTRAINT ", StringComparison.Ordinal))
                {
                    definitions.Add(table + ": " + line.TrimEnd(','));
                }
            }
        }

        definitions.Sort(StringComparer.Ordinal);

        return definitions.ToArray();
    }
}
