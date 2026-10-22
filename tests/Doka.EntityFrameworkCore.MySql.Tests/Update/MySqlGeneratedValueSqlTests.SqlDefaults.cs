using System.Text;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlGeneratedValueSqlTests
{
    /// <summary>An unknown non-identity key cannot be correlated by MySQL's SELECT-after-INSERT protocol.</summary>
    /// <param name="tableName">The scalar or composite generated-key shape.</param>
    /// <param name="bulk">Whether the insert enters through the batch fallback.</param>
    [Theory]
    [InlineData("SqlGuidKeys", false)]
    [InlineData("SqlStringKeys", false)]
    [InlineData("SqlCompositeKeys", false)]
    [InlineData("SqlGuidKeys", true)]
    [InlineData("SqlStringKeys", true)]
    [InlineData("SqlCompositeKeys", true)]
    public void Unknown_sql_generated_key_is_rejected_before_sql_is_appended(
        string tableName,
        bool bulk
    )
    {
        // Arrange
        using var context = CreateSqlKeyContext(mariaDb: false);
        var commands = CreateSqlKeyCommands(context, tableName, bulk ? 2 : 1);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder("unchanged");

        // Act
        var exception = Record.Exception(() => generator.AppendBulkInsertOperation(sql, commands, 0, out _));

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(exception);

        Assert.Contains("cannot retrieve", failure.Message, StringComparison.Ordinal);
        Assert.Contains(tableName, failure.Message, StringComparison.Ordinal);
        Assert.Contains("Id", failure.Message, StringComparison.Ordinal);
        Assert.Contains("LAST_INSERT_ID()", failure.Message, StringComparison.Ordinal);
        Assert.Equal("unchanged", sql.ToString());
    }

    /// <summary>MariaDB's RETURNING protocol does not need an already known generated key.</summary>
    /// <param name="tableName">The scalar or composite generated-key shape.</param>
    /// <param name="count">The number of rows whose generated values must be returned.</param>
    [Theory]
    [InlineData("SqlGuidKeys", 1)]
    [InlineData("SqlStringKeys", 1)]
    [InlineData("SqlCompositeKeys", 1)]
    [InlineData("SqlGuidKeys", 2)]
    [InlineData("SqlStringKeys", 2)]
    [InlineData("SqlCompositeKeys", 2)]
    public void Native_returning_accepts_unknown_sql_generated_keys(
        string tableName,
        int count
    )
    {
        // Arrange
        using var context = CreateSqlKeyContext(mariaDb: true);
        var commands = CreateSqlKeyCommands(context, tableName, count);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        // Act
        _ = generator.AppendBulkInsertOperation(sql, commands, 0, out _);

        // Assert
        Assert.Contains("INSERT INTO", sql.ToString(), StringComparison.Ordinal);
        Assert.Contains("RETURNING `Id`", sql.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("LAST_INSERT_ID()", sql.ToString(), StringComparison.Ordinal);
    }

    private static SqlKeyContext CreateSqlKeyContext(
        bool mariaDb
    ) => new(new DbContextOptionsBuilder<SqlKeyContext>()
        .UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=unused;",
            mariaDb
                ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
                : MySqlServerVersion.MySql(new Version(8, 4, 0)))
        .Options);

    /// <summary>Builds physical key descriptors independently from the convention being repaired.</summary>
    private static IReadOnlyModificationCommand[] CreateSqlKeyCommands(
        SqlKeyContext context,
        string tableName,
        int count
    )
    {
        var table = context.Model.GetRelationalModel().Tables.Single(value => value.Name == tableName);

        return Enumerable.Range(0, count).Select(row =>
        {
            var modifications = new List<IColumnModification>();

            foreach (var column in table.PrimaryKey!.Columns)
            {
                modifications.Add(column.Name == "Id"
                    ? CreateModification(column, null, $"generated{row}", read: true, write: false)
                    : CreateModification(column, row + 1, $"tenant{row}", read: false, write: true));
            }

            return (IReadOnlyModificationCommand)new TestCommand(table, modifications);
        }).ToArray();
    }

    private sealed class SqlKeyContext(DbContextOptions<SqlKeyContext> options) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SqlGuidKey>(entity =>
            {
                entity.ToTable("SqlGuidKeys");
                entity.Property(value => value.Id).HasColumnType("char(36)").HasDefaultValueSql("UUID()");
            });
            modelBuilder.Entity<SqlStringKey>(entity =>
            {
                entity.ToTable("SqlStringKeys");
                entity.Property(value => value.Id).HasColumnType("char(36)").HasDefaultValueSql("UUID()");
            });
            modelBuilder.Entity<SqlCompositeKey>(entity =>
            {
                entity.ToTable("SqlCompositeKeys");
                entity.HasKey(value => new { value.Tenant, value.Id });
                entity.Property(value => value.Tenant).ValueGeneratedNever();
                entity.Property(value => value.Id).HasColumnType("char(36)").HasDefaultValueSql("UUID()");
            });
        }
    }

    private sealed class SqlGuidKey
    {
        public Guid Id { get; set; }
    }

    private sealed class SqlStringKey
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class SqlCompositeKey
    {
        public int Tenant { get; set; }

        public Guid Id { get; set; }
    }
}
