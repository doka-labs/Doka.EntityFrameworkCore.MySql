using System.Text;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlGeneratedValueSqlTests
{
    /// <summary>
    /// Preserves complete SQL and readback contracts with EF-created narrow and wide command lists.
    /// </summary>
    /// <param name="columnCount">The physical column count, including the key and any generated column.</param>
    /// <param name="rowCount">The number of tracked insert commands.</param>
    /// <param name="generated">Whether the server returns a computed column.</param>
    /// <param name="mariaDb">Whether native MariaDB RETURNING is available.</param>
    [Theory]
    [MemberData(nameof(TrackedEnumerationCases))]
    public void TrackedNarrowAndWideCommandsPreserveCompleteSql(
        int columnCount,
        int rowCount,
        bool generated,
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateEnumerationContext(mariaDb);
        var tableName = EnumerationTableName(columnCount, generated);
        var commands = CreateEnumerationCommands(context, tableName, columnCount, rowCount, generated);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();
        var expectedSql = ExpectedEnumerationSql(tableName, columnCount, rowCount, generated, mariaDb);
        var expectedMapping = ExpectedEnumerationMapping(rowCount, generated, mariaDb);

        // Act
        var mapping = generator.AppendBulkInsertOperation(sql, commands, commandPosition: 17, out var transaction);

        // Assert
        Assert.Equal(expectedSql, sql.ToString(), ignoreLineEndingDifferences: true);
        Assert.Equal(expectedMapping, mapping);
        Assert.Equal(generated && !mariaDb, transaction);
        Assert.All(commands, command => Assert.Equal(columnCount, command.ColumnModifications.Count));
        Assert.All(commands, command => Assert.Single(command.Entries));
    }

    /// <summary>
    /// Keeps the full packet-cost calculation independent of the number of ordinary written columns.
    /// </summary>
    /// <param name="columnCount">The complete physical column count.</param>
    /// <param name="generated">Whether the command reads a computed column.</param>
    /// <param name="mariaDb">Whether native MariaDB RETURNING replaces keyed readback.</param>
    [Theory]
    [InlineData(3, false, false)]
    [InlineData(50, false, false)]
    [InlineData(3, true, false)]
    [InlineData(50, true, false)]
    [InlineData(3, true, true)]
    [InlineData(50, true, true)]
    public void TrackedColumnListsPreserveExactKeyedReadbackCost(
        int columnCount,
        bool generated,
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateEnumerationContext(mariaDb);
        var tableName = EnumerationTableName(columnCount, generated);
        var command = CreateEnumerationCommands(context, tableName, columnCount, 1, generated)[0];
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var expectedEligible = generated && !mariaDb;
        var expectedBytes = expectedEligible
            ? 256L + (4L * (96 + (2 * tableName.Length) + 6 + (2 * "Generated".Length) + 16 + (2 * "Id".Length) + 3))
            : 0;

        // Act
        var eligible = generator.TryGetKeyedReadbackCost(command, out var packetBytes);

        // Assert
        Assert.Equal(expectedEligible, eligible);
        Assert.Equal(expectedBytes, packetBytes);
    }

    /// <summary>
    /// Rejects a second descriptor for the same physical key instead of choosing an arbitrary readback predicate.
    /// </summary>
    [Fact]
    public void DuplicatePhysicalKeyDescriptorsRetainPerRowReadback()
    {
        // Arrange
        using var context = CreateContext();
        var commands = CreateCommands(context, "Rows", 2);
        var modifications = commands[1].ColumnModifications.ToList();
        modifications.Add(CreateModification(commands[1].Table!.PrimaryKey!.Columns[0], 2, "duplicate", false, true));
        commands[1] = new TestCommand(commands[1].Table, modifications);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        // Act
        var mapping = generator.AppendBulkInsertOperation(sql, commands, commandPosition: 0, out var transaction);

        // Assert
        Assert.Equal(ResultSetMapping.LastInResultSet, mapping);
        Assert.True(transaction);
        Assert.Equal(2, Regex.Count(sql.ToString(), "INSERT INTO"));
        Assert.DoesNotContain("UNION ALL", sql.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A duplicate physical key is not eligible for a keyed-readback packet reservation.
    /// </summary>
    [Fact]
    public void DuplicatePhysicalKeyDescriptorsDoNotReserveKeyedReadbackCost()
    {
        // Arrange
        using var context = CreateContext();
        var original = CreateCommands(context, "Rows", 1)[0];
        var modifications = original.ColumnModifications.ToList();
        modifications.Add(CreateModification(original.Table!.PrimaryKey!.Columns[0], 1, "duplicate", false, true));
        var command = new TestCommand(original.Table, modifications);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();

        // Act
        var eligible = generator.TryGetKeyedReadbackCost(command, out var packetBytes);

        // Assert
        Assert.False(eligible);
        Assert.Equal(0, packetBytes);
    }

    /// <summary>Supplies independent real-command width, row-count, generation, and engine cases.</summary>
    public static IEnumerable<object[]> TrackedEnumerationCases()
    {
        foreach (var columnCount in new[] { 3, 50 })
        {
            foreach (var rowCount in new[] { 1, 3 })
            {
                yield return [columnCount, rowCount, false, false];
                yield return [columnCount, rowCount, false, true];
                yield return [columnCount, rowCount, true, false];
                yield return [columnCount, rowCount, true, true];
            }
        }
    }

    private static EnumerationContext CreateEnumerationContext(
        bool mariaDb
    ) => new(new DbContextOptionsBuilder<EnumerationContext>()
        .UseMySql(
            "Server=localhost;Database=enumeration_unit;User ID=test;Password=unused;",
            mariaDb
                ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
                : MySqlServerVersion.MySql(new Version(8, 4, 0)))
        .Options);

    /// <summary>
    /// Retains EF's real command and column-list construction rather than a synthetic array implementation.
    /// </summary>
    private static IReadOnlyModificationCommand[] CreateEnumerationCommands(
        EnumerationContext context,
        string tableName,
        int columnCount,
        int rowCount,
        bool generated
    )
    {
        var rows = context.Set<Dictionary<string, object>>(tableName);
        var payloadCount = columnCount - (generated ? 2 : 1);

        // WHY: Row ordinals define deterministic keys and expected parameter values for every tracked command.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var row = 0; row < rowCount; row++)
        {
            var values = new Dictionary<string, object> { ["Id"] = row + 1 };

            // WHY: Payload ordinals keep generated property names aligned with independently expected SQL values.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var column = 0; column < payloadCount; column++)
            {
                values[$"Payload{column:D2}"] = (row + 1) * 100 + column;
            }

            rows.Add(values);
        }

        var table = context.Model.GetRelationalModel().Tables.Single(candidate => candidate.Name == tableName);
        var entries = context.GetService<IUpdateAdapterFactory>().Create().Entries
            .OrderBy(entry => (int)entry.GetCurrentValue(entry.EntityType.FindProperty("Id")!)!)
            .ToArray();

        var factory = context.GetService<IModificationCommandFactory>();
        var commands = new IReadOnlyModificationCommand[rowCount];
        var parameter = 0;

        // WHY: Match each tracked entry to its fixed command-array position and deterministic parameter order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var row = 0; row < rowCount; row++)
        {
            var command = factory.CreateModificationCommand(new ModificationCommandParameters(
                table,
                sensitiveLoggingEnabled: false,
                detailedErrorsEnabled: false,
                comparer: null,
                generateParameterName: () => "p" + parameter++,
                logger: null));

            command.AddEntry(entries[row], mainEntry: true);

            // WHY: Freeze lazy names in command/column order so the expected SQL is
            // independent of which eligibility scan first touches a parameter.
            foreach (var modification in command.ColumnModifications)
            {
                if (modification.UseCurrentValueParameter)
                {
                    _ = modification.ParameterName;
                }
            }

            commands[row] = command;
        }

        return commands;
    }

    /// <summary>
    /// Builds the expected protocol from the fixture schema, not from the generated command descriptors.
    /// </summary>
    private static string ExpectedEnumerationSql(
        string tableName,
        int columnCount,
        int rowCount,
        bool generated,
        bool mariaDb
    )
    {
        var payloadCount = columnCount - (generated ? 2 : 1);
        var writeCount = payloadCount + 1;
        var columns = Enumerable.Range(0, payloadCount)
            .Select(index => $"Payload{index:D2}")
            .Prepend("Id");

        var sql = new StringBuilder()
            .Append("INSERT INTO `").Append(tableName).Append("` (")
            .AppendJoin(", ", columns.Select(column => "`" + column + "`"))
            .AppendLine(")")
            .Append("VALUES ");

        // WHY: Row ordinals select the exact parameter range and separator position in the independent SQL oracle.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var row = 0; row < rowCount; row++)
        {
            if (row != 0)
            {
                sql.AppendLine(",");
            }

            sql.Append('(')
                .AppendJoin(", ", Enumerable.Range(row * writeCount, writeCount).Select(index => "@p" + index))
                .Append(')');
        }

        if (generated && mariaDb)
        {
            return sql.AppendLine().AppendLine("RETURNING `Generated`;").ToString();
        }

        sql.AppendLine(";");

        if (generated)
        {
            // WHY: Expected readback SQL includes the explicit correlation ordinal and row-specific key predicate.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var row = 0; row < rowCount; row++)
            {
                if (row != 0)
                {
                    sql.AppendLine().AppendLine("UNION ALL");
                }

                sql.Append("SELECT `Generated`");

                if (rowCount > 1)
                {
                    sql.Append(", ").Append(row);
                }

                sql.AppendLine()
                    .Append("FROM `").Append(tableName).AppendLine("`")
                    .Append("WHERE ROW_COUNT() = ").Append(rowCount)
                    .Append(" AND `Id` = @p").Append(row * writeCount);
            }

            sql.AppendLine(";");

            if (rowCount == 1)
            {
                // WHY: EF's single-row fallback retains its trailing statement
                // separator; the provider's consolidated readback does not.
                sql.AppendLine();
            }
        }

        return sql.ToString();
    }

    private static string EnumerationTableName(
        int columnCount,
        bool generated
    ) => (generated ? "EnumerationRead" : "EnumerationWrite") + columnCount;

    private static ResultSetMapping ExpectedEnumerationMapping(
        int rowCount,
        bool generated,
        bool mariaDb
    )
    {
        if (!generated)
        {
            return ResultSetMapping.NoResults;
        }

        if (rowCount == 1)
        {
            return ResultSetMapping.LastInResultSet;
        }

        if (mariaDb)
        {
            return ResultSetMapping.NotLastInResultSet;
        }

        return ResultSetMapping.NotLastInResultSet | ResultSetMapping.IsPositionalResultMappingEnabled;
    }

    /// <summary>Owns the fixed set of narrow and wide schemas used by the real-command cases.</summary>
    private sealed class EnumerationContext(DbContextOptions<EnumerationContext> options) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            // WHY: All widths share one fixed model, so EF's context-type model cache
            // cannot substitute a schema from a previously executed test case.
            foreach (var columnCount in new[] { 3, 50 })
            {
                foreach (var generated in new[] { false, true })
                {
                    var tableName = EnumerationTableName(columnCount, generated);
                    var entity = modelBuilder.SharedTypeEntity<Dictionary<string, object>>(tableName);
                    entity.ToTable(tableName);
                    entity.Property<int>("Id").ValueGeneratedNever();
                    entity.HasKey("Id");
                    var payloadCount = columnCount - (generated ? 2 : 1);

                    // WHY: Construct the requested model width with the same ordinal naming contract as the fixture.
                    // ReSharper disable once ForCanBeConvertedToForeach
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    for (var column = 0; column < payloadCount; column++)
                    {
                        entity.Property<int>($"Payload{column:D2}");
                    }

                    if (generated)
                    {
                        entity.Property<int>("Generated").HasComputedColumnSql("`Payload00` + 1", stored: true);
                    }
                }
            }
        }
    }
}
