using System.Transactions;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlConnector3CompatibilityTests
{
    /// <summary>Separates default and explicitly requested XA isolation levels.</summary>
    public static TheoryData<string, string> XaIsolationCases
    {
        get
        {
            var cases = new TheoryData<string, string>();
            foreach (var target in GetSelectedSupportedTargets())
            {
                cases.Add(target.ToString(), "Default");
                cases.Add(target.ToString(), "Serializable");
                cases.Add(target.ToString(), "ReadCommitted");
                cases.Add(target.ToString(), "RepeatableRead");
            }

            return cases;
        }
    }

    /// <summary>Separates read-committed refresh from repeatable-read snapshot retention.</summary>
    public static TheoryData<string, string> XaVisibilityCases
    {
        get
        {
            var cases = new TheoryData<string, string>();
            foreach (var target in GetSelectedSupportedTargets())
            {
                cases.Add(target.ToString(), "ReadCommitted");
                cases.Add(target.ToString(), "RepeatableRead");
            }

            return cases;
        }
    }

    /// <summary>Distinguishes explicit read-committed visibility from repeatable-read snapshots.</summary>
    /// <param name="target">The selected native database engine.</param>
    /// <param name="requestedIsolation">The explicit ambient read isolation.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(XaVisibilityCases))]
    public async Task Xa_snapshot_visibility_matches_the_qualified_explicit_isolation(
        string target,
        string requestedIsolation
    )
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var settings = new MySqlConnectionStringBuilder(CreateConnectionString(target))
        {
            UseXaTransactions = true,
        };

        await using var owner = new MySqlConnection(settings.ConnectionString);
        await owner.OpenAsync(cancellationToken);
        await ExecuteAsync(owner, "DROP TABLE IF EXISTS `C3XaVisibility`;", cancellationToken);
        await ExecuteAsync(
            owner,
            "CREATE TABLE `C3XaVisibility` (`Id` int NOT NULL PRIMARY KEY, `Value` int NOT NULL) ENGINE=InnoDB;",
            cancellationToken);
        await ExecuteAsync(owner, "INSERT INTO `C3XaVisibility` VALUES (1, 1);", cancellationToken);
        await ExecuteAsync(owner, "SET SESSION TRANSACTION ISOLATION LEVEL REPEATABLE READ;", cancellationToken);

        // WHY: Open before the ambient scope with AutoEnlist=false, so the
        // update commits independently of the owner's XA snapshot.
        await using var observer = new MySqlConnection(CreateConnectionString(target));
        await observer.OpenAsync(cancellationToken);
        await ExecuteAsync(observer, "SET SESSION autocommit = 1;", cancellationToken);
        using var scope = CreateXaScope(requestedIsolation);
        owner.EnlistTransaction(Transaction.Current);
        await using var update = observer.CreateCommand();
        update.CommandText = "UPDATE `C3XaVisibility` SET `Value` = 2 WHERE `Id` = 1;";

        // Act
        var first = await ScalarAsync(owner, "SELECT `Value` FROM `C3XaVisibility` WHERE `Id` = 1;", cancellationToken);
        var affected = await update.ExecuteNonQueryAsync(cancellationToken);
        var committed = await ScalarAsync(observer, "SELECT `Value` FROM `C3XaVisibility` WHERE `Id` = 1;", cancellationToken);
        var second = await ScalarAsync(owner, "SELECT `Value` FROM `C3XaVisibility` WHERE `Id` = 1;", cancellationToken);

        // Assert
        Assert.Equal(1, Convert.ToInt32(first, CultureInfo.InvariantCulture));
        Assert.Equal(1, affected);
        Assert.Equal(2, Convert.ToInt32(committed, CultureInfo.InvariantCulture));

        // WHY: Legacy XA retains the controlled RR baseline for either request.
        // Connector 3 must refresh only RC; making both requests RR cannot pass.
        var expected = UsesConnector3 && requestedIsolation == "ReadCommitted" ? 2 : 1;

        Assert.Equal(expected, Convert.ToInt32(second, CultureInfo.InvariantCulture));
    }

    /// <summary>Proves actual XA read-lock behavior instead of inferring it from cached transaction metadata.</summary>
    /// <param name="target">The selected native database engine.</param>
    /// <param name="requestedIsolation">The ambient isolation, or Default for the .NET default.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(XaIsolationCases))]
    public async Task Xa_read_locks_match_the_qualified_driver_isolation_contract(
        string target,
        string requestedIsolation
    )
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var settings = new MySqlConnectionStringBuilder(CreateConnectionString(target))
        {
            UseXaTransactions = true,
        };

        await using var owner = new MySqlConnection(settings.ConnectionString);
        await owner.OpenAsync(cancellationToken);
        await ExecuteAsync(owner, "DROP TABLE IF EXISTS `C3XaIsolation`;", cancellationToken);
        await ExecuteAsync(owner, "CREATE TABLE `C3XaIsolation` (`Id` int NOT NULL PRIMARY KEY) ENGINE=InnoDB;", cancellationToken);
        await ExecuteAsync(owner, "INSERT INTO `C3XaIsolation` VALUES (1);", cancellationToken);
        await ExecuteAsync(owner, "SET SESSION TRANSACTION ISOLATION LEVEL REPEATABLE READ;", cancellationToken);

        await using var observer = new MySqlConnection(CreateConnectionString(target));
        await observer.OpenAsync(cancellationToken);
        using var scope = CreateXaScope(requestedIsolation);
        owner.EnlistTransaction(Transaction.Current);
        await using var observerTransaction = await observer.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken);

        await using var probe = observer.CreateCommand();
        probe.Transaction = observerTransaction;
        probe.CommandText = "SELECT `Id` FROM `C3XaIsolation` WHERE `Id` = 1 FOR UPDATE NOWAIT;";

        // Act
        var read = await ScalarAsync(owner, "SELECT `Id` FROM `C3XaIsolation` WHERE `Id` = 1;", cancellationToken);
        object? observed = null;
        var failure = await Record.ExceptionAsync(async () =>
        {
            observed = await probe.ExecuteScalarAsync(cancellationToken);
        });

        // Assert
        Assert.Equal(1, Convert.ToInt32(read, CultureInfo.InvariantCulture));
        if (UsesConnector3 && requestedIsolation is "Default" or "Serializable")
        {
            // WHY: A completed ordinary SELECT holds an S lock only under
            // SERIALIZABLE here. NOWAIT observes that lock without timing assumptions.
            var native = Assert.IsType<MySqlException>(failure);
            var isMariaDb = IntegrationTestEnvironment
                .GetServerVersion(Enum.Parse<IntegrationDatabaseTarget>(target))
                .IsMariaDb;

            Assert.Equal(isMariaDb ? 1205 : 3572, native.Number);
            Assert.Null(observed);
        }
        else
        {
            // WHY: Legacy XA ignores the requested isolation and retains the
            // controlled REPEATABLE READ baseline; explicit RC/RR on 3 also has no S lock.
            Assert.Null(failure);
            Assert.Equal(1, Convert.ToInt32(observed, CultureInfo.InvariantCulture));
        }
    }

    private static TransactionScope CreateXaScope(
        string requestedIsolation
    )
    {
        if (requestedIsolation == "Default")
        {
            return new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled);
        }

        return new TransactionScope(
            TransactionScopeOption.RequiresNew,
            new TransactionOptions
            {
                IsolationLevel = Enum.Parse<System.Transactions.IsolationLevel>(requestedIsolation),
            },
            TransactionScopeAsyncFlowOption.Enabled);
    }
}
