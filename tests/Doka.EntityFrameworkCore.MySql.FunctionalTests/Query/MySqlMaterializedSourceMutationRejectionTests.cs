namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies that statement-local read sources cannot become mutation targets or open a connection.</summary>
public sealed class MySqlMaterializedSourceMutationRejectionTests
{
    /// <summary>Bulk updates reject the read-only source during translation, before acquiring a database connection.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execute_update_rejects_a_materialized_source_before_database_io(bool mariaDb)
    {
        var connections = new UnexpectedConnectionInterceptor();
        await using var context = CreateContext(mariaDb, connections);
        var query = context.Set<MutationNode>().WithMaterializedSource(source => source.Where(node => node.Id > 0));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => query.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.Value, 7), CancellationToken.None));

        Assert.Contains("materialized", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, connections.OpenAttempts);
    }

    /// <summary>Bulk deletes reject the read-only source during translation, before acquiring a database connection.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execute_delete_rejects_a_materialized_source_before_database_io(bool mariaDb)
    {
        var connections = new UnexpectedConnectionInterceptor();
        await using var context = CreateContext(mariaDb, connections);
        var query = context.Set<MutationNode>().WithMaterializedSource(source => source.Where(node => node.Id > 0));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            query.ExecuteDeleteAsync(CancellationToken.None));

        Assert.Contains("materialized", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, connections.OpenAttempts);
    }

    /// <summary>Creates only the services needed to translate a mutation for the configured database family.</summary>
    private static MutationContext CreateContext(bool mariaDb, UnexpectedConnectionInterceptor connections)
    {
        var version = mariaDb
            ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

        var options = MySqlFunctionalTestOptions.CreateTransientBuilder<MutationContext>()
            .UseMySql("Server=localhost;Database=source_rejection;User ID=unused;", version)
            .AddInterceptors(connections)
            .Options;

        return new MutationContext(options);
    }

    /// <summary>Turns unexpected connection acquisition into a deterministic offline-test failure.</summary>
    private sealed class UnexpectedConnectionInterceptor : DbConnectionInterceptor
    {
        private int _openAttempts;

        /// <summary>Gets the number of connection-open attempts, including any rejected attempt.</summary>
        public int OpenAttempts => Volatile.Read(ref _openAttempts);

        /// <inheritdoc />
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _openAttempts);

            throw new InvalidOperationException("Unexpected database I/O before mutation rejection.");
        }
    }

    /// <summary>Provides a minimal mapped entity without any database setup or external fixture.</summary>
    private sealed class MutationContext(DbContextOptions<MutationContext> options) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MutationNode>().ToTable("MaterializedMutationNodes");
        }
    }

    /// <summary>Supplies a regular mapped target whose mutations would otherwise be supported by EF.</summary>
    private sealed class MutationNode
    {
        /// <summary>Gets or sets the primary key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the scalar update target.</summary>
        public int Value { get; set; }
    }
}
