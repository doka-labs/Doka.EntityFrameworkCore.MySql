namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlUpsertIntegrationTests
{
    /// <summary>Reports the three owned/borrowed connection paths independently.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> ConnectionCases => SelectedTargets.SelectMany(target
        => new[]
        {
            new TheoryDataRow<string, string>(target.Data, "connection-string"),
            new TheoryDataRow<string, string>(target.Data, "borrowed-connection"),
            new TheoryDataRow<string, string>(target.Data, "data-source"),
        });

    /// <summary>Native writes preserve the user's connection and data-source ownership contracts.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(ConnectionCases))]
    public async Task Connection_paths_preserve_caller_ownership_and_allow_followup_use(
        string target,
        string path
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        var capture = new Writes();
        await using var borrowed = new MySqlConnection(store.ConnectionString);
        await using var dataSource = new MySqlDataSourceBuilder(store.ConnectionString).Build();

        if (path == "borrowed-connection")
        {
            await borrowed.OpenAsync(CancellationToken.None);
        }

        var context = path switch
        {
            "connection-string" => store.CreateContext(capture),
            "borrowed-connection" => store.CreateContext(capture, borrowed),
            "data-source" => store.CreateDataSourceContext(dataSource, capture),
            _ => throw new ArgumentOutOfRangeException(nameof(path)),
        };

        // Act
        long affected;

        await using (context)
        {
            affected = await context.ExecuteUpsertAsync(NewRow(1),
                new[] { nameof(UpsertRow.Name) },
                CancellationToken.None);
        }

        await using var followup = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = new MySqlCommand("SELECT COUNT(*) FROM `UpsertRows`;", followup);
        var persistedCount = Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None),
            CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(1, affected);
        Assert.Equal(1, persistedCount);
        Assert.Single(capture.Statements);

        if (path == "borrowed-connection")
        {
            Assert.Equal(ConnectionState.Open, borrowed.State);
        }
    }

    /// <summary>Confirmed ambient enlistment governs a single statement without a second independent transaction.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TransactionCases))]
    public async Task Ambient_single_command_follows_the_scope_owner(
        string target,
        bool complete
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        var capture = new Writes();
        long affected;

        // Act
        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await using var context = store.CreateContext(capture);
            affected = await context.ExecuteUpsertAsync(NewRow(1),
                new[] { nameof(UpsertRow.Name) },
                CancellationToken.None);

            if (complete)
            {
                scope.Complete();
            }
        }

        await using var reader = store.CreateContext();
        var count = await reader.Rows.CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, affected);
        Assert.Equal(complete
                ? 1
                : 0,
            count);

        Assert.Single(capture.Statements);
    }

    /// <summary>Ambient presence does not imply a qualified multi-command savepoint boundary.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Multi_command_ambient_call_rejects_before_its_first_write(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        var rows = Enumerable
            .Range(1, 1001)
            .Select(id => NewRow(id))
            .ToArray();

        Exception? error;

        // Act
        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            error = await Record.ExceptionAsync(()
                => context.ExecuteUpsertRangeAsync(rows, new[] { nameof(UpsertRow.Name) }, CancellationToken.None));

            scope.Complete();
        }

        var count = await context.Rows.CountAsync(CancellationToken.None);

        // Assert
        Assert.Contains("qualified savepoint boundary",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Empty(capture.Statements);
        Assert.Equal(0, count);
    }

    /// <summary>The explicit operation never enters a configured retry strategy for a native transient-looking failure.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Enabled_retry_strategy_does_not_retry_a_direct_upsert_command(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await ExecuteNativeAsync(store.Observer,
            "CREATE TRIGGER `UpsertTransientGuard` BEFORE INSERT ON `UpsertRows` FOR EACH ROW SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO = 1205, MESSAGE_TEXT = 'controlled transient write failure';",
            CancellationToken.None);

        var capture = new Writes();

        // WHY: Retry options are immutable provider settings. This independent graph avoids
        // reconfiguring the fixture graph already initialized without retries.
        await using var context = new UpsertContext(IntegrationTestDbContextOptions
                .Create<UpsertContext>()
                .EnableServiceProviderCaching(false)
                .UseMySql(store.ConnectionString,
                    store.Version,
                    provider => provider.EnableRetryOnFailure(1, TimeSpan.FromMilliseconds(1)))
                .AddInterceptors(capture)
                .Options,
            "ordinary");

        // Act
        var error = await Record.ExceptionAsync(()
            => context.ExecuteUpsertAsync(NewRow(1), new[] { nameof(UpsertRow.Name) }, CancellationToken.None));

        await using var reader = store.CreateContext();
        var count = await reader.Rows.CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1205,
            Assert.IsType<MySqlException>(error)
                .Number);

        Assert.Single(capture.Statements);
        Assert.Equal(0, count);
    }
}
