namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlWindowFunctionIntegrationTests
{
    /// <summary>The Count window's actual Int32 materializer rejects a native result beyond its promised range.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Window_count_overflow_remains_visible_with_the_canonical_native_integer_reader(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var overflow = new WindowCountOverflowCommand();
        await using var context = new WindowContext(store.CreateOptions(overflow, false));
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .Count());

        // Act
        var error = await Record.ExceptionAsync(async () => await query.ToArrayAsync(safety.Token));

        // Assert
        Assert.Contains("COUNT(*) OVER", overflow.OriginalSql, StringComparison.Ordinal);
        Assert.Equal(1, overflow.Readers);
        Assert.IsType<OverflowException>(error);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A direct projected rank filter selects the first two rows independently in every partition.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Top_n_projected_window_filter_retains_the_first_rows_of_each_partition(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .Select(row => new
            {
                row.Id,
                row.Group,
                Position = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .RowNumber(),
            })
            .Where(row => row.Position <= 2)
            .OrderBy(row => row.Group)
            .ThenBy(row => row.Position);

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 2, 4, 5, 6], rows.Select(row => row.Id));
        Assert.Equal([1, 1, 2, 2, 3], rows.Select(row => row.Group));
        Assert.Equal([1L, 2L, 1L, 2L, 1L], rows.Select(row => row.Position));
    }

    /// <summary>A window preserves explicit volatile-value sharing without assigning a sharing promise to an ordinary CTE.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(WindowFlagTargets))]
    public async Task Window_projection_preserves_the_selected_volatile_source_sharing_contract(
        string target,
        bool materialized
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var commands = new WindowParameterCommands();
        await using var context = new WindowContext(store.CreateOptions(commands, false));
        var source = context
            .Rows
            .Where(row => row.Id <= 3)
            .Select(row => new
            {
                row.Id,
                Token = Guid.NewGuid(),
            });

        var query = materialized
            ? source.WithMaterializedSource(shared => shared.Join(shared,
                left => left.Id,
                right => right.Id,
                (left, right) => new
                {
                    left.Id,
                    Left = left.Token,
                    Right = right.Token,
                    Position = EF
                        .Functions
                        .Window()
                        .OrderBy(left.Id)
                        .RowNumber(),
                }))
            : source.WithCte(shared => shared.Join(shared,
                left => left.Id,
                right => right.Id,
                (left, right) => new
                {
                    left.Id,
                    Left = left.Token,
                    Right = right.Token,
                    Position = EF
                        .Functions
                        .Window()
                        .OrderBy(left.Id)
                        .RowNumber(),
                }));

        var sql = query.ToQueryString();

        // Act
        var rows = await query
            .OrderBy(row => row.Id)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 2, 3], rows.Select(row => row.Id));
        Assert.Equal([1L, 2L, 3L], rows.Select(row => row.Position));
        Assert.All(rows, row => Assert.NotEqual(Guid.Empty, row.Left));
        Assert.All(rows, row => Assert.NotEqual(Guid.Empty, row.Right));
        Assert.Equal(1, commands.Readers);
        if (materialized)
        {
            Assert.All(rows, row => Assert.Equal(row.Left, row.Right));
        }
        else
        {
            // WHY: Native optimization may merge or materialize an ordinary CTE. Neither
            // equality nor inequality of its volatile references is a portable guarantee.
            Assert.DoesNotContain("WITH RECURSIVE", sql, StringComparison.Ordinal);
        }
    }

    /// <summary>Injects a real native boundary value after capturing the Count window's translated command.</summary>
    private sealed class WindowCountOverflowCommand : DbCommandInterceptor
    {
        internal string OriginalSql { get; private set; } = string.Empty;
        internal int Readers { get; private set; }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            OriginalSql = command.CommandText;
            Readers++;

            // WHY: Exercise the canonical Count materializer against the real connector's
            // checked getter without allocating or counting more than two billion rows.
            command.CommandText = "SELECT CAST(2147483648 AS UNSIGNED);";

            return ValueTask.FromResult(result);
        }
    }
}
