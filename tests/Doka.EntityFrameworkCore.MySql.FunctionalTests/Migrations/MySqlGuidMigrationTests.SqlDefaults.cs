namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlGuidMigrationTests
{
    /// <summary>Provides independent AddColumn and CreateTable cases for each provider-owned Guid format.</summary>
    public static IEnumerable<object[]> GuidSqlDefaultMigrationCases()
    {
        foreach (var addColumn in new[] { false, true })
        {
            yield return [MySqlGuidFormat.Char36, "char(36)", "UUID()", addColumn];
            yield return [MySqlGuidFormat.Uuid, "uuid", "UUID()", addColumn];
            yield return
            [
                MySqlGuidFormat.Binary16, "binary(16)", "UNHEX(REPLACE(UUID(), '-', ''))", addColumn,
            ];
        }
    }

    /// <summary>Model-derived SQL defaults retain Guid operation types, compilable C#, and parenthesized SQL.</summary>
    [Theory]
    [MemberData(nameof(GuidSqlDefaultMigrationCases))]
    public void Model_guid_sql_default_columns_preserve_types_and_expressions(
        MySqlGuidFormat format,
        string storeType,
        string defaultSql,
        bool addColumn
    )
    {
        // Arrange
        using var source = new GuidSqlDefaultMigrationSourceContext(
            CreateOptions<GuidSqlDefaultMigrationSourceContext>());

        using var target = new GuidSqlDefaultMigrationTargetContext(
            CreateOptions<GuidSqlDefaultMigrationTargetContext>());

        var tableName = $"SqlDefault{format}";

        // Act
        var operations = addColumn ? GetDifferences(source, target) : GetDifferences(target);
        var code = GenerateMigrationCode(operations);
        var compilation = CompileMigrationCode(code);
        var commands = target.GetService<IMigrationsSqlGenerator>()
            .Generate(operations, target.GetService<IDesignTimeModel>().Model);

        var sql = string.Join(Environment.NewLine, commands.Select(command => command.CommandText));

        // Assert
        var column = addColumn
            ? Assert.Single(operations.OfType<AddColumnOperation>(), operation => operation.Table == tableName)
            : Assert.Single(
                Assert.Single(operations.OfType<CreateTableOperation>(), operation => operation.Name == tableName)
                    .Columns,
                operation => operation.Name == "ExternalId");

        AssertGuidColumn(column, storeType, isNullable: false, guidFormat: format);
        Assert.Equal(defaultSql, column.DefaultValueSql);

        // EF may retain a synthetic CLR fallback; generated source must consume the SQL expression instead.
        Assert.DoesNotContain("defaultValue:", code, StringComparison.Ordinal);
        Assert.Contains(addColumn ? "AddColumn<Guid>" : "table.Column<Guid>", code, StringComparison.Ordinal);
        Assert.Contains($"defaultValueSql: \"{defaultSql}\"", code, StringComparison.Ordinal);
        Assert.Contains($"`ExternalId` {storeType} NOT NULL DEFAULT ({defaultSql})", sql, StringComparison.Ordinal);
        AssertCompilationSucceeded(compilation);
    }

    /// <summary>Hand-authored Guid operations preserve SQL expressions without converting them into literal defaults.</summary>
    [Theory]
    [MemberData(nameof(GuidSqlDefaultMigrationCases))]
    public void Literal_guid_sql_default_columns_preserve_types_and_expressions(
        MySqlGuidFormat format,
        string storeType,
        string defaultSql,
        bool addColumn
    )
    {
        // Arrange
        using var context = new GuidSqlDefaultMigrationTargetContext(
            CreateOptions<GuidSqlDefaultMigrationTargetContext>());

        var builder = new MigrationBuilder("Doka.EntityFrameworkCore.MySql");

        if (addColumn)
        {
            builder.AddColumn<Guid>(
                    name: "ExternalId",
                    table: "LiteralGuidSqlDefaults",
                    type: storeType,
                    nullable: false,
                    defaultValueSql: defaultSql)
                .Annotation(MySqlAnnotationNames.GuidFormat, format);
        }
        else
        {
            builder.CreateTable(
                name: "LiteralGuidSqlDefaults",
                columns: table => new
                {
                    ExternalId = table.Column<Guid>(type: storeType, nullable: false, defaultValueSql: defaultSql)
                        .Annotation(MySqlAnnotationNames.GuidFormat, format),
                });
        }

        // Act
        var code = GenerateMigrationCode(builder.Operations);
        var compilation = CompileMigrationCode(code);
        var commands = context.GetService<IMigrationsSqlGenerator>()
            .Generate(builder.Operations, context.GetService<IDesignTimeModel>().Model);

        var sql = string.Join(Environment.NewLine, commands.Select(command => command.CommandText));

        // Assert
        var operation = Assert.Single(builder.Operations);
        var column = addColumn
            ? Assert.IsType<AddColumnOperation>(operation)
            : Assert.Single(Assert.IsType<CreateTableOperation>(operation).Columns);

        AssertGuidColumn(column, storeType, isNullable: false, guidFormat: format);
        Assert.Equal(defaultSql, column.DefaultValueSql);
        Assert.Null(column.DefaultValue);
        Assert.Contains(addColumn ? "AddColumn<Guid>" : "table.Column<Guid>", code, StringComparison.Ordinal);
        Assert.Contains($"defaultValueSql: \"{defaultSql}\"", code, StringComparison.Ordinal);
        Assert.Contains($"`ExternalId` {storeType} NOT NULL DEFAULT ({defaultSql})", sql, StringComparison.Ordinal);
        AssertCompilationSucceeded(compilation);
    }

    /// <summary>Provides identical tables before and after adding their default-bearing Guid columns.</summary>
    private abstract class GuidSqlDefaultMigrationContext : DbContext
    {
        /// <summary>Creates a context for the supplied migration endpoint.</summary>
        protected GuidSqlDefaultMigrationContext(
            DbContextOptions options
        ) : base(options) { }

        /// <summary>Gets whether the target contains the default-bearing column.</summary>
        protected abstract bool IncludesDefaultColumn { get; }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            foreach (var format in new[] { MySqlGuidFormat.Char36, MySqlGuidFormat.Uuid, MySqlGuidFormat.Binary16 })
            {
                var tableName = $"SqlDefault{format}";

                modelBuilder.SharedTypeEntity<Dictionary<string, object>>(tableName, entity =>
                {
                    entity.ToTable(tableName);
                    entity.IndexerProperty<int>("Id");
                    entity.HasKey("Id");

                    if (IncludesDefaultColumn)
                    {
                        // Binary16 needs a binary expression; the textual UUID() result cannot fit the column.
                        var defaultSql = format == MySqlGuidFormat.Binary16
                            ? "UNHEX(REPLACE(UUID(), '-', ''))"
                            : "UUID()";

                        entity.IndexerProperty<Guid>("ExternalId")
                            .HasMySqlGuidFormat(format)
                            .HasDefaultValueSql(defaultSql);
                    }
                });
            }
        }
    }

    /// <summary>Provides existing tables without their new Guid columns.</summary>
    private sealed class GuidSqlDefaultMigrationSourceContext : GuidSqlDefaultMigrationContext
    {
        /// <summary>Creates the source migration model.</summary>
        public GuidSqlDefaultMigrationSourceContext(
            DbContextOptions<GuidSqlDefaultMigrationSourceContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override bool IncludesDefaultColumn => false;
    }

    /// <summary>Provides tables whose new Guid columns have explicit SQL defaults.</summary>
    private sealed class GuidSqlDefaultMigrationTargetContext : GuidSqlDefaultMigrationContext
    {
        /// <summary>Creates the target migration model.</summary>
        public GuidSqlDefaultMigrationTargetContext(
            DbContextOptions<GuidSqlDefaultMigrationTargetContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override bool IncludesDefaultColumn => true;
    }
}
