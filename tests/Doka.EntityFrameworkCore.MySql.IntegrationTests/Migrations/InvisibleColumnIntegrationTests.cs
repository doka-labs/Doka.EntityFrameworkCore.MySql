namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies model-differ visibility transitions against live MySQL-family metadata.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class InvisibleColumnIntegrationTests
{
    private const string TableName = "InvisibleColumnRecords";

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_invisible_column_roundtrips_through_add_and_alter_operations() =>
        RunInvisibleColumnRoundTrip(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_invisible_column_roundtrips_through_add_and_alter_operations() =>
        RunInvisibleColumnRoundTrip(IntegrationDatabaseTarget.MariaDb118);

    private static async Task RunInvisibleColumnRoundTrip(
        IntegrationDatabaseTarget target
    )
    {
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

        await DropTableAsync(connectionString);

        try
        {
            await ExecuteOperationsAsync(withoutColumn, connectionString, GetDifferences(empty, withoutColumn));
            await InsertRecordAsync(connectionString);

            var addColumn = Assert.Single(
                GetDifferences(withoutColumn, invisible)
                    .OfType<AddColumnOperation>());

            Assert.Equal(
                true,
                addColumn.FindAnnotation(MySqlAnnotationNames.Invisible)
                    ?.Value);

            await ExecuteOperationsAsync(invisible, connectionString, [addColumn]);

            Assert.Contains(
                "INVISIBLE",
                await ReadColumnExtraAsync(connectionString),
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                nameof(InvisibleColumnRecord.InternalData),
                await ReadWildcardColumnNamesAsync(connectionString));
            Assert.Equal(string.Empty, await ReadExplicitColumnValueAsync(connectionString));

            var makeVisible = Assert.Single(
                GetDifferences(invisible, visible)
                    .OfType<AlterColumnOperation>());

            Assert.Equal(
                false,
                makeVisible.FindAnnotation(MySqlAnnotationNames.Invisible)
                    ?.Value);
            Assert.Equal(
                true,
                makeVisible.OldColumn.FindAnnotation(MySqlAnnotationNames.Invisible)
                    ?.Value);

            await ExecuteOperationsAsync(visible, connectionString, [makeVisible]);

            Assert.DoesNotContain(
                "INVISIBLE",
                await ReadColumnExtraAsync(connectionString),
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                nameof(InvisibleColumnRecord.InternalData),
                await ReadWildcardColumnNamesAsync(connectionString));

            var makeInvisible = Assert.Single(
                GetDifferences(visible, invisible)
                    .OfType<AlterColumnOperation>());

            Assert.Equal(
                true,
                makeInvisible.FindAnnotation(MySqlAnnotationNames.Invisible)
                    ?.Value);
            Assert.Equal(
                false,
                makeInvisible.OldColumn.FindAnnotation(MySqlAnnotationNames.Invisible)
                    ?.Value);

            await ExecuteOperationsAsync(invisible, connectionString, [makeInvisible]);

            Assert.Contains(
                "INVISIBLE",
                await ReadColumnExtraAsync(connectionString),
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                nameof(InvisibleColumnRecord.InternalData),
                await ReadWildcardColumnNamesAsync(connectionString));
        }
        finally
        {
            await DropTableAsync(connectionString);
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

    private static async Task ExecuteOperationsAsync(
        DbContext context,
        string connectionString,
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var commands = context
            .GetService<IMigrationsSqlGenerator>()
            .Generate(operations, context.Model);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var migrationCommand in commands)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migrationCommand.CommandText;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<string> ReadColumnExtraAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXTRA FROM information_schema.columns "
            + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column;";
        command.Parameters.AddWithValue("@table", TableName);
        command.Parameters.AddWithValue("@column", nameof(InvisibleColumnRecord.InternalData));

        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static async Task InsertRecordAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO `{TableName}` (`Id`) VALUES (1);";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<string>> ReadWildcardColumnNamesAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM `{TableName}` LIMIT 0;";
        await using var reader = await command.ExecuteReaderAsync();

        return Enumerable
            .Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToArray();
    }

    private static async Task<string> ReadExplicitColumnValueAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT `{nameof(InvisibleColumnRecord.InternalData)}` FROM `{TableName}` LIMIT 1;";

        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static async Task DropTableAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{TableName}`;";
        await command.ExecuteNonQueryAsync();
    }

    private abstract class InvisibleColumnContextBase(DbContextOptions options) : DbContext(options)
    {
        protected abstract ColumnDisposition Disposition { get; }

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
        protected override ColumnDisposition Disposition => ColumnDisposition.Absent;
    }

    private sealed class InvisibleColumnContext(DbContextOptions<InvisibleColumnContext> options)
        : InvisibleColumnContextBase(options)
    {
        protected override ColumnDisposition Disposition => ColumnDisposition.Invisible;
    }

    private sealed class VisibleColumnContext(DbContextOptions<VisibleColumnContext> options)
        : InvisibleColumnContextBase(options)
    {
        protected override ColumnDisposition Disposition => ColumnDisposition.Visible;
    }

    private sealed class InvisibleColumnRecord
    {
        public int Id { get; set; }

        public string InternalData { get; set; } = string.Empty;
    }

    private enum ColumnDisposition
    {
        Absent,
        Visible,
        Invisible,
    }
}
