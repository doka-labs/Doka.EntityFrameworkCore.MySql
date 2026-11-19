namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectIntegrationTests
{
    private static readonly string[] s_priorRecursiveRows = ["5000|prior"];
    private static readonly string[] s_warmedRecursiveRows = ["1|recursive-budget", "5000|prior"];

    /// <summary>Reports invalid recursion on normal, empty, constant-false and Take-zero payloads, direct and compiled.</summary>
    public static IEnumerable<TheoryDataRow<string, string, bool>> RecursiveBudgetCases
        => SelectedTargets.SelectMany(target => new[]
        {
            new TheoryDataRow<string, string, bool>(target.Data, "normal", false),
            new(target.Data, "normal", true),
            new(target.Data, "empty", false),
            new(target.Data, "empty", true),
            new(target.Data, "false", false),
            new(target.Data, "false", true),
            new(target.Data, "take-zero", false),
            new(target.Data, "take-zero", true),
        });

    /// <summary>An invalid native recursion budget remains an error even when EF can remove every payload row.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(RecursiveBudgetCases))]
    public async Task Invalid_recursive_budget_survives_empty_payload_optimization_and_compiled_parameter_reuse(
        string target,
        string payload,
        bool compiled
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var limits = await ReadNativeAsync(store.Observer,
            store.Version.IsMariaDb
                ? "SELECT @@max_recursive_iterations;"
                : "SELECT @@cte_max_recursion_depth;",
            safety.Token);

        var invalidBudget = int.Parse(limits.Single(), CultureInfo.InvariantCulture);

        if (invalidBudget <= 0)
        {
            throw new InvalidOperationException("The pinned fixture must expose a positive native recursion ceiling.");
        }

        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        var worker = (MySqlConnection)context.Database.GetDbConnection();
        var nativeTransaction = (MySqlTransaction)transaction.GetDbTransaction();
        await ExecuteNativeAsync(worker,
            $"INSERT INTO `{TargetTable}` (`Id`,`Value`,`order`) VALUES (5000,5000,'prior');",
            safety.Token,
            nativeTransaction);

        var operation = compiled
            ? CompileRecursiveInsert(payload)
            : null;

        var warmCount = 0;

        // Act
        if (operation is not null)
        {
            // WHY: Reusing the same delegate with depth zero first detects a stale cached budget, not merely unsupported compilation.
            warmCount = await operation(context, 0, safety.Token);
        }

        var error = await Record.ExceptionAsync(() => operation is not null
            ? operation(context, invalidBudget, safety.Token)
            : BuildRecursiveSource(context, payload, invalidBudget)
                .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                    setters => setters
                        .SetProperty(row => row.Value, value => value)
                        .SetProperty(row => row.Code, value => "recursive-budget"),
                    safety.Token));

        var priorCount = await CountPriorWorkAsync(worker, nativeTransaction, safety.Token);
        await transaction.CommitAsync(safety.Token);
        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT `Value`,`order` FROM `{TargetTable}` ORDER BY `Value`;",
            safety.Token);

        // Assert
        Assert.Equal(1242,
            Assert.IsType<MySqlException>(error)
                .Number);

        Assert.Equal(compiled && payload == "normal"
                ? 1
                : 0,
            warmCount);

        Assert.Equal(1, priorCount);
        Assert.Equal(compiled && payload == "normal"
                ? s_warmedRecursiveRows
                : s_priorRecursiveRows,
            actual);

        Assert.Equal(compiled
                ? 2
                : 1,
            commands.Statements.Count);

        Assert.Equal(0, commands.Readers);
        Assert.All(commands.Statements,
            statement => Assert.StartsWith("INSERT INTO", statement.TrimStart(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Keeps each empty-producing form explicit so neither a helper call nor a client predicate becomes its false rejection oracle.</summary>
    private static IQueryable<int> BuildRecursiveSource(
        InsertContext context,
        string payload,
        int maximumDepth
    )
    {
        var source = context.Set<InsertSource>();
        var anchor = source
            .Where(row => row.Id
                == (payload == "empty"
                    ? -1
                    : 1))
            .Select(row => row.Id);

        return payload switch
        {
            "false" => anchor.WithRecursiveCte(frontier => frontier.SelectMany(id => source
                    .Where(row => row.Id == id + 1)
                    .Select(row => row.Id)),
                rows => rows.Where(value => false),
                maximumDepth),
            "take-zero" => anchor.WithRecursiveCte(frontier => frontier.SelectMany(id => source
                    .Where(row => row.Id == id + 1)
                    .Select(row => row.Id)),
                rows => rows.Take(0),
                maximumDepth),
            "normal" or "empty" => anchor.WithRecursiveCte(frontier => frontier.SelectMany(id => source
                    .Where(row => row.Id == id + 1)
                    .Select(row => row.Id)),
                rows => rows,
                maximumDepth),
            _ => throw new ArgumentOutOfRangeException(nameof(payload)),
        };
    }

    /// <summary>Leaves the supported CTE and setter operators directly inside each compiled expression.</summary>
    private static Func<InsertContext, int, CancellationToken, Task<int>> CompileRecursiveInsert(
        string payload
    ) => payload switch
    {
        "normal" => EF.CompileQuery((InsertContext context, int maximumDepth, CancellationToken token) => context
            .Set<InsertSource>()
            .Where(row => row.Id == 1)
            .Select(row => row.Id)
            .WithRecursiveCte(frontier => frontier.SelectMany(id => context
                    .Set<InsertSource>()
                    .Where(row => row.Id == id + 1)
                    .Select(row => row.Id)),
                rows => rows,
                maximumDepth)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, value => value)
                    .SetProperty(row => row.Code, value => "recursive-budget"),
                token)),
        "empty" => EF.CompileQuery((InsertContext context, int maximumDepth, CancellationToken token) => context
            .Set<InsertSource>()
            .Where(row => row.Id == -1)
            .Select(row => row.Id)
            .WithRecursiveCte(frontier => frontier.SelectMany(id => context
                    .Set<InsertSource>()
                    .Where(row => row.Id == id + 1)
                    .Select(row => row.Id)),
                rows => rows,
                maximumDepth)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, value => value)
                    .SetProperty(row => row.Code, value => "recursive-budget"),
                token)),
        "false" => EF.CompileQuery((InsertContext context, int maximumDepth, CancellationToken token) => context
            .Set<InsertSource>()
            .Where(row => row.Id == 1)
            .Select(row => row.Id)
            .WithRecursiveCte(frontier => frontier.SelectMany(id => context
                    .Set<InsertSource>()
                    .Where(row => row.Id == id + 1)
                    .Select(row => row.Id)),
                rows => rows.Where(value => false),
                maximumDepth)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, value => value)
                    .SetProperty(row => row.Code, value => "recursive-budget"),
                token)),
        "take-zero" => EF.CompileQuery((InsertContext context, int maximumDepth, CancellationToken token) => context
            .Set<InsertSource>()
            .Where(row => row.Id == 1)
            .Select(row => row.Id)
            .WithRecursiveCte(frontier => frontier.SelectMany(id => context
                    .Set<InsertSource>()
                    .Where(row => row.Id == id + 1)
                    .Select(row => row.Id)),
                rows => rows.Take(0),
                maximumDepth)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, value => value)
                    .SetProperty(row => row.Code, value => "recursive-budget"),
                token)),
        _ => throw new ArgumentOutOfRangeException(nameof(payload)),
    };
}
