namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies model-differ visibility transitions against live MySQL-family metadata.
/// </summary>
[IntegrationDatabaseTest]
public sealed class InvisibleColumnIntegrationTests
{
    private const string TableName = "InvisibleColumnRecords";

    /// <summary>
    /// Verifies my sql84 invisible column roundtrips through add and alter operations.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_invisible_column_roundtrips_through_add_and_alter_operations()
        => RunInvisibleColumnRoundTrip(IntegrationDatabaseTarget.MySql84, CancellationToken.None);

    /// <summary>
    /// Verifies maria db118 invisible column roundtrips through add and alter operations.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_invisible_column_roundtrips_through_add_and_alter_operations()
        => RunInvisibleColumnRoundTrip(IntegrationDatabaseTarget.MariaDb118, CancellationToken.None);

    /// <summary>
    /// Runs invisible column round trip for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="target">The database target used by the test.</param>
    private static async Task RunInvisibleColumnRoundTrip(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);
        await using var empty = new EmptyInvisibleColumnContext(
            CreateOptions<EmptyInvisibleColumnContext>(connectionString, serverVersion));

        await using var withoutColumn = new WithoutInvisibleColumnContext(
            CreateOptions<WithoutInvisibleColumnContext>(connectionString, serverVersion));

        await using var invisible = new InvisibleColumnContext(
            CreateOptions<InvisibleColumnContext>(connectionString, serverVersion));

        await using var visible = new VisibleColumnContext(
            CreateOptions<VisibleColumnContext>(connectionString, serverVersion));

        await DropTableAsync(connectionString, cancellationToken);

        try
        {
            // WHY: Each visibility transition consumes the physical column created by the preceding transition.
            // Act
            await ExecuteOperationsAsync(
                withoutColumn,
                connectionString,
                GetDifferences(empty, withoutColumn),
                cancellationToken);

            await InsertRecordAsync(connectionString, cancellationToken);

            var addColumn = Assert.Single(
                GetDifferences(withoutColumn, invisible)
                    .OfType<AddColumnOperation>());

            var addedInvisibleAnnotation = addColumn.FindAnnotation(MySqlAnnotationNames.Invisible)
                ?.Value;

            await ExecuteOperationsAsync(invisible, connectionString, [addColumn], cancellationToken);

            var extraAfterAdd = await ReadColumnExtraAsync(connectionString, cancellationToken);
            var wildcardAfterAdd = await ReadWildcardColumnNamesAsync(connectionString, cancellationToken);
            var explicitValueAfterAdd = await ReadExplicitColumnValueAsync(connectionString, cancellationToken);

            var makeVisible = Assert.Single(
                GetDifferences(invisible, visible)
                    .OfType<AlterColumnOperation>());

            var visibleAnnotation = makeVisible.FindAnnotation(MySqlAnnotationNames.Invisible)
                ?.Value;

            var oldInvisibleAnnotation = makeVisible.OldColumn.FindAnnotation(MySqlAnnotationNames.Invisible)
                ?.Value;

            await ExecuteOperationsAsync(visible, connectionString, [makeVisible], cancellationToken);

            var extraAfterVisible = await ReadColumnExtraAsync(connectionString, cancellationToken);
            var wildcardAfterVisible = await ReadWildcardColumnNamesAsync(connectionString, cancellationToken);

            var makeInvisible = Assert.Single(
                GetDifferences(visible, invisible)
                    .OfType<AlterColumnOperation>());

            var invisibleAnnotation = makeInvisible.FindAnnotation(MySqlAnnotationNames.Invisible)
                ?.Value;

            var oldVisibleAnnotation = makeInvisible.OldColumn.FindAnnotation(MySqlAnnotationNames.Invisible)
                ?.Value;

            await ExecuteOperationsAsync(invisible, connectionString, [makeInvisible], cancellationToken);

            var extraAfterInvisible = await ReadColumnExtraAsync(connectionString, cancellationToken);
            var wildcardAfterInvisible = await ReadWildcardColumnNamesAsync(connectionString, cancellationToken);

            // Assert
            Assert.Equal(true, addedInvisibleAnnotation);
            Assert.Contains("INVISIBLE", extraAfterAdd, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(nameof(InvisibleColumnRecord.InternalData), wildcardAfterAdd);
            Assert.Equal(string.Empty, explicitValueAfterAdd);
            Assert.Equal(false, visibleAnnotation);
            Assert.Equal(true, oldInvisibleAnnotation);
            Assert.DoesNotContain("INVISIBLE", extraAfterVisible, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(nameof(InvisibleColumnRecord.InternalData), wildcardAfterVisible);
            Assert.Equal(true, invisibleAnnotation);
            Assert.Equal(false, oldVisibleAnnotation);
            Assert.Contains("INVISIBLE", extraAfterInvisible, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(nameof(InvisibleColumnRecord.InternalData), wildcardAfterInvisible);
        }
        finally
        {
            await DropTableAsync(connectionString, CancellationToken.None);
        }
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        string connectionString,
        MySqlServerVersion serverVersion
    )
        where TContext : DbContext
    {
        var builder = IntegrationTestDbContextOptions.Create<TContext>();
        builder.UseMySql(connectionString, serverVersion);

        return builder.Options;
    }

    private static IReadOnlyList<MigrationOperation> GetDifferences(
        DbContext source,
        DbContext target
    ) => target
        .GetService<IMigrationsModelDiffer>()
        .GetDifferences(
            source
                .GetService<IDesignTimeModel>()
                .Model
                .GetRelationalModel(),
            target
                .GetService<IDesignTimeModel>()
                .Model
                .GetRelationalModel());

    /// <summary>
    /// Executes operations for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="context">The context used for the database operation.</param>
    /// <param name="connectionString">The target database connection string.</param>
    /// <param name="operations">The migration operations to execute.</param>
    private static async Task ExecuteOperationsAsync(
        DbContext context,
        string connectionString,
        IReadOnlyList<MigrationOperation> operations,
        CancellationToken cancellationToken
    )
    {
        var commands = context
            .GetService<IMigrationsSqlGenerator>()
            .Generate(operations, context.Model);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var migrationCommand in commands)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migrationCommand.CommandText;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Reads column extra for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connectionString">The target database connection string.</param>
    private static async Task<string> ReadColumnExtraAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXTRA FROM information_schema.columns "
            + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column;";

        command.Parameters.AddWithValue("@table", TableName);
        command.Parameters.AddWithValue("@column", nameof(InvisibleColumnRecord.InternalData));

        return Assert.IsType<string>(await command.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>
    /// Inserts record for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connectionString">The target database connection string.</param>
    private static async Task InsertRecordAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO `{TableName}` (`Id`) VALUES (1);";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Reads wildcard column names for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connectionString">The target database connection string.</param>
    private static async Task<IReadOnlyList<string>> ReadWildcardColumnNamesAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM `{TableName}` LIMIT 0;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken: cancellationToken);

        return Enumerable
            .Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToArray();
    }

    /// <summary>
    /// Reads explicit column value for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connectionString">The target database connection string.</param>
    private static async Task<string> ReadExplicitColumnValueAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT `{nameof(InvisibleColumnRecord.InternalData)}` FROM `{TableName}` LIMIT 1;";

        return Assert.IsType<string>(await command.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>
    /// Drops table for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connectionString">The target database connection string.</param>
    private static async Task DropTableAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{TableName}`;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private abstract class InvisibleColumnContextBase(DbContextOptions options) : DbContext(options)
    {
        protected abstract ColumnDisposition Disposition { get; }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<InvisibleColumnRecord>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(record => record.Id);

                if (Disposition == ColumnDisposition.Absent)
                {
                    entity.Ignore(record => record.InternalData);

                    return;
                }

                entity
                    .Property(record => record.InternalData)
                    .HasMaxLength(80)
                    .HasDefaultValue(string.Empty)
                    .IsInvisible(Disposition == ColumnDisposition.Invisible);
            });
        }
    }

    private sealed class EmptyInvisibleColumnContext(DbContextOptions<EmptyInvisibleColumnContext> options)
        : DbContext(options);

    private sealed class WithoutInvisibleColumnContext(DbContextOptions<WithoutInvisibleColumnContext> options)
        : InvisibleColumnContextBase(options)
    {
        /// <inheritdoc />
        protected override ColumnDisposition Disposition => ColumnDisposition.Absent;
    }

    private sealed class InvisibleColumnContext(DbContextOptions<InvisibleColumnContext> options)
        : InvisibleColumnContextBase(options)
    {
        /// <inheritdoc />
        protected override ColumnDisposition Disposition => ColumnDisposition.Invisible;
    }

    private sealed class VisibleColumnContext(DbContextOptions<VisibleColumnContext> options)
        : InvisibleColumnContextBase(options)
    {
        /// <inheritdoc />
        protected override ColumnDisposition Disposition => ColumnDisposition.Visible;
    }

    private sealed class InvisibleColumnRecord
    {
        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the internal data.
        /// </summary>
        public string InternalData { get; set; } = string.Empty;
    }

    private enum ColumnDisposition
    {
        Absent,
        Visible,
        Invisible,
    }
}
