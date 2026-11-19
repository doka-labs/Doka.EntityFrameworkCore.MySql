namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteIntegrationTests
{
    /// <summary>All references contribute column demand, while unused fields of a real wide source stay unselected.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Wide_shared_source_preserves_both_reference_demands_for_all_fourteen_thousand_rows(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await CteStore.CreateAsync(target, safety.Token);
        await store.SeedWideRowsAsync(safety.Token);
        await using var sourceContext = store.CreateContext();
        var commands = new WideCteCommands();
        var options =
            new DbContextOptionsBuilder<CteContext>(
                    (DbContextOptions<CteContext>)sourceContext.GetService<IDbContextOptions>())
                .AddInterceptors(commands)
                .Options;

        await using var context = new CteContext(options);
        var query = context
            .Rows
            .WithCte(shared => shared.Join(shared,
                left => left.Id,
                right => right.Id,
                (left, right) => new
                {
                    left.Id,
                    Sum = EF.Property<int>(left, "Payload0") + EF.Property<int>(right, "Payload47"),
                }))
            .OrderBy(row => row.Id);

        var before = GC.GetTotalAllocatedBytes(precise: true);

        // Act
        var rows = await query.ToArrayAsync(safety.Token);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        var dataCommand = commands.Statements.TryPeek(out var observed)
            ? observed
            : throw new InvalidOperationException(
                "The native-wide fixture must capture its actual data command before requesting its plan.");

        var plan = await ExplainWideCteAsync(context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The native-wide fixture requires its owned connection identity."),
            dataCommand.Sql,
            safety.Token);

        // Assert
        Assert.Equal(14000, rows.Length);
        Assert.Equal(Enumerable
                .Range(1, 14000)
                .Select(id => (id, id * 5)),
            rows.Select(row => (row.Id, row.Sum)));

        Assert.Empty(context.ChangeTracker.Entries());
        var sql = dataCommand.Sql;
        Assert.Contains("`Payload0`", sql, StringComparison.Ordinal);
        Assert.Contains("`Payload47`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`Payload1`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`Label`", sql, StringComparison.Ordinal);
        Assert.Single(commands.Statements);
        Assert.Equal(safety.Token, dataCommand.Token);
        Assert.Equal(0, dataCommand.ParameterCount);
        Assert.Equal(50,
            context.Model.FindEntityType(typeof(CteRow))!
                .GetProperties()
                .Count());

        using var document = JsonDocument.Parse(plan);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);

        // WHY: Complete async reading/compilation allocations include other concurrent test work.
        // Record full results, the actual native plan and separate command counts; never gate
        // elapsed time, a runner-specific allocation ratio or an optimizer's chosen plan shape.
        _output.WriteLine(
            "CTE native-wide diagnostic: source-rows=14000; source-columns=50; result-rows={0}; result-columns=2; data-commands={1}; plan-commands=1; allocated-process-bytes={2}; sql-chars={3}.",
            rows.Length,
            commands.Statements.Count,
            allocated,
            sql.Length);

        _output.WriteLine("CTE native-wide EXPLAIN FORMAT=JSON: {0}", plan);
    }

    /// <summary>Explains this fixed unparameterized provider statement independently of its measured materialization.</summary>
    private static async Task<string> ExplainWideCteAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken
    )
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            AutoEnlist = false,
        };

        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // WHY: This is an explicit fixture-only EXPLAIN of the actual generated read command,
        // not an extra command in provider execution or a parse/rewrite of user-supplied SQL.
        await using var command = new MySqlCommand("EXPLAIN FORMAT=JSON " + sql, connection);

        return await command.ExecuteScalarAsync(cancellationToken) as string
            ?? throw new InvalidOperationException("The native server did not return its JSON query plan.");
    }

    /// <summary>Observes actual EF reader boundaries; fixture-only plan commands bypass this data-command capture.</summary>
    private sealed class WideCteCommands : DbCommandInterceptor
    {
        /// <summary>Gets SQL, caller token and parameter count for each actual data command.</summary>
        public ConcurrentQueue<(string Sql, CancellationToken Token, int ParameterCount)> Statements { get; } = new();

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Statements.Enqueue((command.CommandText, cancellationToken, command.Parameters.Count));

            return ValueTask.FromResult(result);
        }
    }
}
