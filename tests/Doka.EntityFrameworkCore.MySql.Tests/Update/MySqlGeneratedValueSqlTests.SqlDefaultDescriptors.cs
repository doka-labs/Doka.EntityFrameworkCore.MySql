using System.Text;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlGeneratedValueSqlTests
{
    /// <summary>Reuses the mapped literal contracts with and without descriptor-supplied type mappings.</summary>
    public static IEnumerable<object[]> PropertyOnlyLiteralDescriptorCases()
    {
        foreach (var literalCase in LiteralReadKeyCases())
        {
            if ((bool)literalCase[2])
            {
                continue;
            }

            yield return [literalCase[0], literalCase[1], true];
            yield return [literalCase[0], literalCase[1], false];
        }
    }

    /// <summary>
    /// Property-only descriptors retain the complete key and use the mapped literal or mapping fallback.
    /// </summary>
    /// <param name="tableName">The actual mapped literal key shape.</param>
    /// <param name="expectedLiteral">The expected SQL representation, including escaping or conversion.</param>
    /// <param name="provideTypeMapping">
    /// Whether the descriptor supplies its mapping rather than resolving the property.
    /// </param>
    [Theory]
    [MemberData(nameof(PropertyOnlyLiteralDescriptorCases))]
    public void Property_only_literal_descriptor_uses_complete_mapped_predicate(
        string tableName,
        string expectedLiteral,
        bool provideTypeMapping
    )
    {
        // Arrange
        using var context = new LiteralReadKeyContext(new DbContextOptionsBuilder<LiteralReadKeyContext>()
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=unused;",
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .Options);

        var table = context.Model.GetRelationalModel().Tables.Single(candidate => candidate.Name == tableName);
        var column = table.FindColumn("LiteralKey")!;
        var literal = CreatePropertyOnlyReadKeyDescriptor(column, provideTypeMapping);
        var command = new TestCommand(
            table,
            [
                CreateModification(table.FindColumn("Id")!, 101, "known", read: false, write: true),
                literal,
            ]);

        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        // Act
        _ = generator.AppendInsertOperation(sql, command, 0, out _);
        var text = sql.ToString();

        // Assert
        Assert.Null(literal.Column);
        Assert.Equal(provideTypeMapping, literal.TypeMapping is not null);
        Assert.Contains(
            $"WHERE ROW_COUNT() = 1 AND `Id` = @known AND `LiteralKey` = {expectedLiteral}",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("LAST_INSERT_ID()", text, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(text, "INSERT INTO"));
        Assert.Equal(1, Regex.Count(text, "SELECT "));
    }

    /// <summary>Provides unknown store defaults through both physical and property-only descriptors.</summary>
    public static IEnumerable<object[]> UnknownDefaultDescriptorCases()
    {
        foreach (var physicalColumn in new[] { false, true })
        {
            yield return ["SqlDescriptorKeys", physicalColumn];
            yield return ["TriggerDescriptorKeys", physicalColumn];
            yield return ["NullDescriptorKeys", physicalColumn];
            yield return ["UnspecifiedDescriptorKeys", physicalColumn];
        }
    }

    /// <summary>Unknown defaults cannot be mistaken for known literals or append a partial insert.</summary>
    /// <param name="tableName">The SQL, trigger, absent, or unspecified default contract.</param>
    /// <param name="physicalColumn">Whether the descriptor carries its physical column.</param>
    [Theory]
    [MemberData(nameof(UnknownDefaultDescriptorCases))]
    public void Unknown_default_descriptor_is_rejected_without_modifying_sql(
        string tableName,
        bool physicalColumn
    )
    {
        // Arrange
        using var context = new SqlDefaultDescriptorContext(new DbContextOptionsBuilder<SqlDefaultDescriptorContext>()
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=unused;",
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .Options);

        var table = context.Model.GetRelationalModel().Tables.Single(candidate => candidate.Name == tableName);
        var column = table.FindColumn("LiteralKey")!;
        var unknown = physicalColumn
            ? CreateModification(column, null, "unknown", read: true, write: false)
            : CreatePropertyOnlyReadKeyDescriptor(column, provideTypeMapping: true);

        var property = column.PropertyMappings[0].Property;
        var hasLiteralDefault = property.TryGetDefaultValue(out var defaultValue);
        var command = new TestCommand(
            table,
            [
                CreateModification(table.FindColumn("Id")!, 101, "known", read: false, write: true),
                unknown,
            ]);

        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder("unchanged");

        // Act
        var failure = Record.Exception(() => generator.AppendInsertOperation(sql, command, 0, out _));

        // Assert
        Assert.Equal(physicalColumn, unknown.Column is IColumn);

        if (tableName == "UnspecifiedDescriptorKeys")
        {
            Assert.True(hasLiteralDefault);
            Assert.Same(DBNull.Value, defaultValue);
        }
        else
        {
            Assert.False(hasLiteralDefault);
        }

        var exception = Assert.IsType<InvalidOperationException>(failure);

        Assert.Contains("cannot retrieve", exception.Message, StringComparison.Ordinal);
        Assert.Contains(tableName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("LiteralKey", exception.Message, StringComparison.Ordinal);
        Assert.Equal("unchanged", sql.ToString());
    }

    /// <summary>
    /// Uses EF's public descriptor overload for callers that have property metadata but no physical column.
    /// </summary>
    private static ColumnModification CreatePropertyOnlyReadKeyDescriptor(
        IColumn column,
        bool provideTypeMapping
    )
    {
        var property = column.PropertyMappings[0].Property;

        // The public name-based descriptor permits omitted mappings; the generator must resolve the mapped property.
        return new ColumnModification(new ColumnModificationParameters(
            columnName: column.Name,
            originalValue: null,
            value: null,
            property,
            columnType: column.StoreType,
            typeMapping: provideTypeMapping ? property.GetRelationalTypeMapping() : null,
            read: true,
            write: false,
            key: true,
            condition: false,
            sensitiveLoggingEnabled: false,
            isNullable: false));
    }

    /// <summary>Models existing store-generated keys whose actual generated values are unknown to the client.</summary>
    private sealed class SqlDefaultDescriptorContext : DbContext
    {
        /// <summary>Creates the context for unknown-default descriptor contracts.</summary>
        public SqlDefaultDescriptorContext(
            DbContextOptions<SqlDefaultDescriptorContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            ConfigureUnknownDescriptor(modelBuilder, "SqlDescriptorKeys").HasDefaultValueSql("UUID()");
            ConfigureUnknownDescriptor(modelBuilder, "TriggerDescriptorKeys");
            ConfigureUnknownDescriptor(modelBuilder, "NullDescriptorKeys").HasDefaultValue((object?)null);

            // HasDefaultValue() describes an existing database default without claiming its value is known.
            ConfigureUnknownDescriptor(modelBuilder, "UnspecifiedDescriptorKeys").HasDefaultValue();
        }

        /// <summary>
        /// Creates a real composite key, with store-side generation represented by a modeled trigger.
        /// </summary>
        private static PropertyBuilder<string> ConfigureUnknownDescriptor(
            ModelBuilder modelBuilder,
            string tableName
        )
        {
            var entity = modelBuilder.SharedTypeEntity<Dictionary<string, object>>(tableName);

            entity.ToTable(tableName, table => table.HasTrigger($"{tableName}_BeforeInsert"));
            entity.IndexerProperty<int>("Id").ValueGeneratedNever();

            var property = entity.IndexerProperty<string>("LiteralKey")
                .HasColumnType("varchar(64)")
                .ValueGeneratedOnAdd();

            entity.HasKey("Id", "LiteralKey");

            return property;
        }
    }
}
