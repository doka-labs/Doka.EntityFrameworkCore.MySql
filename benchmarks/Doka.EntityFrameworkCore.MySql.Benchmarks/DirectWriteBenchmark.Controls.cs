namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

public partial class DirectWriteBenchmark
{
    private async Task ValidateOperationAsync(
        bool upsert,
        CancellationToken cancellationToken
    )
    {
        var model = Context.GetService<IDesignTimeModel>()
            .Model;

        var sourceType = _width == 3
            ? typeof(NarrowSource)
            : typeof(WideSource);

        var targetType = _width == 3
            ? typeof(NarrowTarget)
            : typeof(WideTarget);

        var source = model.FindEntityType(sourceType)
            ?? throw new InvalidOperationException("The source model is missing.");

        var target = model.FindEntityType(targetType)
            ?? throw new InvalidOperationException("The target model is missing.");

        if (source
                .GetProperties()
                .Count()
            != _width
            || target
                .GetProperties()
                .Count()
            != _width
            || target
                .GetTableMappings()
                .Single()
                .Table
                .Columns
                .Count()
            != _width)
        {
            throw new InvalidOperationException("The direct-write model does not have its declared physical width.");
        }

        await Context
            .DisposeAsync()
            .ConfigureAwait(false);

        _context = null;
        var commands = new WriteObservation();
        var transactions = new TransactionObservation();
        PrepareContext(new DirectWriteContext(CreateOptions(commands, transactions)));
        await ResetTargetAsync(cancellationToken)
            .ConfigureAwait(false);

        var affected = upsert
            ? await ExecuteUpsertAsync(cancellationToken)
                .ConfigureAwait(false)
            : await ExecuteInsertAsync(cancellationToken)
                .ConfigureAwait(false);

        var expectedCommands = upsert
            ? (_rowCount + MySqlUpsertCommandBuilder.MaximumRows - 1) / MySqlUpsertCommandBuilder.MaximumRows
            : 1;

        var expectedTransactions = upsert && _rowCount > MySqlUpsertCommandBuilder.MaximumRows
            ? 1
            : 0;

        if (affected != _rowCount
            || commands.Readers != 0
            || commands.DataCommands != expectedCommands
            || commands.NonDataCommands != 0
            || transactions.Starts != expectedTransactions
            || transactions.Commits != expectedTransactions
            || Context.Database.CurrentTransaction is not null
            || Context
                .ChangeTracker
                .Entries()
                .Any())
        {
            throw new InvalidOperationException(
                "The direct write bypassed its expected command, transaction or no-materialization contract.");
        }

        if (upsert && commands.ParameterCounts.Sum() != checked(_rowCount * _width))
        {
            throw new InvalidOperationException("The UPSERT fixture did not bind every mapped scalar input.");
        }

        if (commands.ParameterCounts.Any(count => count > MySqlUpsertCommandBuilder.MaximumParameters)
            || commands.UpsertCommands
            != (upsert
                ? expectedCommands
                : 0)
            || commands.InsertSelectCommands
            != (upsert
                ? 0
                : 1))
        {
            throw new InvalidOperationException("The direct-write statement did not use its selected native protocol.");
        }

        await VerifyAllValuesAsync(cancellationToken)
            .ConfigureAwait(false);

        var fingerprint = string.Join("|", commands.Fingerprints);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
        Console.WriteLine(
            $"DirectWrite {Shape}/{(upsert ? "UPSERT" : "INSERT SELECT")}: target={BenchmarkEnvironment.TargetIdValue}; "
            + $"rows={_rowCount}; model-fields={_width}; native-count={affected}; data-commands={commands.DataCommands}; "
            + $"readers={commands.Readers}; other-provider-commands={commands.NonDataCommands}; "
            + $"owned-transaction-starts={transactions.Starts}; owned-transaction-commits={transactions.Commits}; sql-sha256={digest}");

        Console.WriteLine(
            $"input-collection={(upsert ? (_width == 3 ? _narrowInput.GetType() : _wideInput.GetType()).FullName : "server-side source")}; "
            + $"model-properties={target.GetProperties().GetType().FullName}; physical-columns={target.GetTableMappings().Single().Table.Columns.GetType().FullName}; "
            + $"data-parameter-counts={string.Join(',', commands.ParameterCounts)}; workload=all-new-keys; allocation-scope=full-public-operation-plus-driver");

        // WHY: Diagnostic interceptors allocate event data even when a local recording flag
        // is disabled. The measured context has no custom interceptor; warm that exact path too.
        await Context
            .DisposeAsync()
            .ConfigureAwait(false);

        _context = null;
        PrepareContext(new DirectWriteContext(CreateOptions()));
        await ResetTargetAsync(cancellationToken)
            .ConfigureAwait(false);

        var warmCount = upsert
            ? await ExecuteUpsertAsync(cancellationToken)
                .ConfigureAwait(false)
            : await ExecuteInsertAsync(cancellationToken)
                .ConfigureAwait(false);

        if (warmCount != _rowCount
            || Context
                .ChangeTracker
                .Entries()
                .Any()
            || Context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "The uninstrumented public workload did not retain its verified result contract.");
        }

        await ResetTargetAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task VerifyAllValuesAsync(
        CancellationToken cancellationToken
    )
    {
        var columns = string.Join(", ",
            Enumerable
                .Range(0, _width)
                .Select(index => "`" + FieldName(index) + "`"));

        await using var command =
            new MySqlCommand("SELECT " + columns + " FROM `" + TargetTable + "` ORDER BY `Id`;", Observer);

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = 0;
        var mismatch = reader.FieldCount != _width;

        while (await reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            rows++;
            mismatch |= reader.GetInt32(0) != rows;

            // WHY: Every physical field must retain its ordinal-derived value; a count
            // or one sampled property would miss truncated/default-filled wide writes.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 1; index < _width; index++)
            {
                mismatch |= reader.GetInt32(index) != checked(rows * 100 + index);
            }
        }

        if (mismatch || rows != _rowCount)
        {
            throw new InvalidOperationException(
                "The direct-write setup did not preserve every expected row and scalar value.");
        }
    }

    private sealed class WriteObservation : DbCommandInterceptor
    {
        internal int Readers { get; private set; }
        internal int DataCommands { get; private set; }
        internal int NonDataCommands { get; private set; }
        internal int UpsertCommands { get; private set; }
        internal int InsertSelectCommands { get; private set; }
        internal List<int> ParameterCounts { get; } = [];
        internal List<string> Fingerprints { get; } = [];

        /// <inheritdoc />
        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result
        )
        {
            Record(command);

            return result;
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Record(command);

            return ValueTask.FromResult(result);
        }

        /// <inheritdoc />
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            Readers++;

            return result;
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Readers++;

            return ValueTask.FromResult(result);
        }

        private void Record(
            DbCommand command
        )
        {
            if (!command.CommandText.StartsWith("INSERT INTO ", StringComparison.OrdinalIgnoreCase))
            {
                NonDataCommands++;

                return;
            }

            DataCommands++;
            ParameterCounts.Add(command.Parameters.Count);
            Fingerprints.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.CommandText)))
                + ":"
                + command.Parameters.Count.ToString(CultureInfo.InvariantCulture));

            UpsertCommands +=
                command.CommandText.Contains("ON DUPLICATE KEY UPDATE", StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0;

            InsertSelectCommands += command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
        }
    }

    private sealed class TransactionObservation : DbTransactionInterceptor
    {
        internal int Starts { get; private set; }
        internal int Commits { get; private set; }

        /// <inheritdoc />
        public override DbTransaction TransactionStarted(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction result
        )
        {
            Starts++;

            return result;
        }

        /// <inheritdoc />
        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction result,
            CancellationToken cancellationToken = default
        )
        {
            Starts++;

            return ValueTask.FromResult(result);
        }

        /// <inheritdoc />
        public override void TransactionCommitted(
            DbTransaction transaction,
            TransactionEndEventData eventData
        ) => Commits++;

        /// <inheritdoc />
        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            Commits++;

            return Task.CompletedTask;
        }
    }
}
