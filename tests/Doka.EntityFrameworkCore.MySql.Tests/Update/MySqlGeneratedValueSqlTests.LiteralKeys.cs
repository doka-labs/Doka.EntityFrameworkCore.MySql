using System.Text;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlGeneratedValueSqlTests
{
    /// <summary>Provides mapped literal keys for both direct inserts and the batch fallback.</summary>
    public static IEnumerable<object[]> LiteralReadKeyCases()
    {
        foreach (var bulk in new[] { false, true })
        {
            yield return ["LiteralIntKeys", "27", bulk];
            yield return ["LiteralBoolKeys", "1", bulk];
            yield return ["LiteralStringKeys", "'lit''eral'", bulk];
            yield return ["LiteralGuidChar36Keys", "'00112233-4455-4677-8899-aabbccddeeff'", bulk];
            yield return ["LiteralGuidBinary16Keys", "X'00112233445546778899AABBCCDDEEFF'", bulk];
            yield return ["LiteralConvertedKeys", "73", bulk];
        }
    }

    /// <summary>
    /// Known literal read keys retain the complete composite predicate without using identity readback.
    /// </summary>
    /// <param name="tableName">The mapped literal key shape.</param>
    /// <param name="expectedLiteral">The expected SQL representation of its configured default.</param>
    /// <param name="bulk">Whether commands enter through the bulk insert fallback.</param>
    [Theory]
    [MemberData(nameof(LiteralReadKeyCases))]
    public void Literal_read_key_uses_mapped_default_in_complete_composite_predicate(
        string tableName,
        string expectedLiteral,
        bool bulk
    )
    {
        // Arrange
        using var context = new LiteralReadKeyContext(new DbContextOptionsBuilder<LiteralReadKeyContext>()
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=unused;",
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .Options);

        var table = context.Model.GetRelationalModel().Tables.Single(candidate => candidate.Name == tableName);
        var commands = Enumerable.Range(0, bulk ? 2 : 1).Select(row =>
        {
            // The read key has no current value; only its configured literal can complete the predicate.
            return (IReadOnlyModificationCommand)new TestCommand(
                table,
                [
                    CreateModification(table.FindColumn("Id")!, row + 101, $"known{row}", read: false, write: true),
                    CreateModification(
                        table.FindColumn("LiteralKey")!, null, $"literal{row}", read: true, write: false),
                ]);
        }).ToArray();

        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        // Act
        _ = bulk
            ? generator.AppendBulkInsertOperation(sql, commands, 0, out _)
            : generator.AppendInsertOperation(sql, commands[0], 0, out _);

        var text = sql.ToString();

        // Assert
        Assert.Equal(commands.Length, Regex.Count(text, "INSERT INTO"));
        Assert.Equal(commands.Length, Regex.Count(text, "SELECT "));
        Assert.DoesNotContain("LAST_INSERT_ID()", text, StringComparison.Ordinal);

        for (var row = 0; row < commands.Length; row++)
        {
            Assert.Contains(
                $"WHERE ROW_COUNT() = 1 AND `Id` = @known{row} AND `LiteralKey` = {expectedLiteral}",
                text,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A shared physical key resolves the root property's literal when the descriptor belongs to its dependent.
    /// </summary>
    /// <param name="bulk">Whether commands enter through the bulk insert fallback.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shared_literal_read_key_uses_physical_column_default(
        bool bulk
    )
    {
        // Arrange
        using var context = new LiteralReadKeyContext(new DbContextOptionsBuilder<LiteralReadKeyContext>()
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=unused;",
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .Options);

        var table = context.Model.GetRelationalModel().Tables
            .Single(candidate => candidate.Name == "SharedLiteralReadKeys");
        var column = table.FindColumn("LiteralKey")!;
        var property = context.Model.FindEntityType(typeof(LiteralSharedDependent))!
            .FindProperty(nameof(LiteralSharedDependent.LiteralKey))!;

        var storeObject = StoreObjectIdentifier.Table(table.Name, table.Schema);
        var hasPropertyDefault = property.TryGetDefaultValue(out _);
        var hasColumnDefault = property.TryGetDefaultValue(storeObject, out var physicalDefault);
        var commands = Enumerable.Range(0, bulk ? 2 : 1).Select(row =>
        {
            // The descriptor must retain the dependent property and its actual shared column.
            var literal = new ColumnModification(new ColumnModificationParameters(
                column,
                originalValue: null,
                value: null,
                property,
                property.GetRelationalTypeMapping(),
                read: true,
                write: false,
                key: true,
                condition: false,
                sensitiveLoggingEnabled: false,
                isNullable: false)
            {
                GenerateParameterName = () => $"sharedLiteral{row}",
            });

            return (IReadOnlyModificationCommand)new TestCommand(
                table,
                [
                    CreateModification(table.FindColumn("Id")!, row + 101, $"known{row}", read: false, write: true),
                    literal,
                ]);
        }).ToArray();

        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        // Act
        var failure = Record.Exception(() =>
        {
            _ = bulk
                ? generator.AppendBulkInsertOperation(sql, commands, 0, out _)
                : generator.AppendInsertOperation(sql, commands[0], 0, out _);
        });

        var text = sql.ToString();

        // Assert
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));
        Assert.False(hasPropertyDefault);
        Assert.True(hasColumnDefault);
        Assert.Equal(41, Assert.IsType<int>(physicalDefault));
        Assert.All(commands, command =>
        {
            var literal = command.ColumnModifications[1];

            Assert.Same(column, literal.Column);
            Assert.Same(property, literal.Property);
            Assert.False(MySqlUpdateSqlGenerator.IsIdentityColumn(literal));
        });
        Assert.Null(failure);
        Assert.DoesNotContain("LAST_INSERT_ID()", text, StringComparison.Ordinal);

        for (var row = 0; row < commands.Length; row++)
        {
            Assert.Contains(
                $"WHERE ROW_COUNT() = 1 AND `Id` = @known{row} AND `LiteralKey` = 41",
                text,
                StringComparison.Ordinal);
        }
    }

    /// <summary>Provides composite keys whose second component is a known literal generated by the store.</summary>
    private sealed class LiteralReadKeyContext : DbContext
    {
        /// <summary>Creates the context for literal key readback descriptors.</summary>
        public LiteralReadKeyContext(
            DbContextOptions<LiteralReadKeyContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            ConfigureLiteralReadKey(modelBuilder, "LiteralIntKeys", 27);
            ConfigureLiteralReadKey(modelBuilder, "LiteralBoolKeys", true);
            ConfigureLiteralReadKey(modelBuilder, "LiteralStringKeys", "lit'eral");
            ConfigureLiteralReadKey(
                    modelBuilder,
                    "LiteralGuidChar36Keys",
                    new Guid("00112233-4455-4677-8899-aabbccddeeff"))
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
            ConfigureLiteralReadKey(
                    modelBuilder,
                    "LiteralGuidBinary16Keys",
                    new Guid("00112233-4455-4677-8899-aabbccddeeff"))
                .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
            ConfigureLiteralReadKey(modelBuilder, "LiteralConvertedKeys", new ConvertedKey(73))
                .HasConversion(new ValueConverter<ConvertedKey, int>(
                    key => key.Value,
                    value => new ConvertedKey(value)));
            modelBuilder.Entity<LiteralSharedPrincipal>(entity =>
            {
                entity.ToTable("SharedLiteralReadKeys");
                entity.HasKey(row => new { row.Id, row.LiteralKey });
                entity.Property(row => row.Id).ValueGeneratedNever();
                entity.Property(row => row.LiteralKey).HasDefaultValue(41).ValueGeneratedOnAdd();
            });
            modelBuilder.Entity<LiteralSharedDependent>(entity =>
            {
                entity.ToTable("SharedLiteralReadKeys");
                entity.HasKey(row => new { row.Id, row.LiteralKey });
                entity.Property(row => row.Id).ValueGeneratedNever();
                entity.Property(row => row.LiteralKey).ValueGeneratedOnAdd();
                entity.HasOne<LiteralSharedPrincipal>().WithOne()
                    .HasForeignKey<LiteralSharedDependent>(row => new { row.Id, row.LiteralKey });
            });
        }

        /// <summary>Configures one written key plus one default-bearing read key.</summary>
        private static PropertyBuilder<TValue> ConfigureLiteralReadKey<TValue>(
            ModelBuilder modelBuilder,
            string tableName,
            TValue defaultValue
        )
        {
            var entity = modelBuilder.SharedTypeEntity<Dictionary<string, object>>(tableName);

            entity.ToTable(tableName);
            entity.IndexerProperty<int>("Id").ValueGeneratedNever();

            var property = entity.IndexerProperty<TValue>("LiteralKey")
                .HasDefaultValue(defaultValue)
                .ValueGeneratedOnAdd();

            entity.HasKey("Id", "LiteralKey");

            return property;
        }
    }

    /// <summary>Owns the shared physical column's literal default.</summary>
    private sealed class LiteralSharedPrincipal
    {
        /// <summary>Gets or sets the written key component.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the default-bearing shared key component.</summary>
        public int LiteralKey { get; set; }
    }

    /// <summary>Maps the shared key without configuring its own literal default.</summary>
    private sealed class LiteralSharedDependent
    {
        /// <summary>Gets or sets the written key component.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the key component whose default belongs to the principal.</summary>
        public int LiteralKey { get; set; }

        /// <summary>Gets or sets the dependent's table-split payload.</summary>
        public int Detail { get; set; }
    }
}
