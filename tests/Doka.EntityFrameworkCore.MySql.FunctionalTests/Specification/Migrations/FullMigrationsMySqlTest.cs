using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Migrations;

/// <summary>
/// Executes the official live migration contract against the active
/// MySQL-family test engine.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class FullMigrationsMySqlTest
    : MigrationsTestBase<FullMigrationsMySqlTest.FullMigrationsMySqlFixture>
{
    private const string MariaDbJsonColumnAlterSql = """
        ALTER TABLE `Entity` MODIFY COLUMN `Name` longtext COLLATE utf8mb4_bin CHECK (JSON_VALID(`Name`));
        """;

    private const string MySqlJsonColumnAlterSql = """
        ALTER TABLE `Entity` MODIFY COLUMN `Name` json NULL;
        """;

    private static string ExpectedJsonColumnAlterSql =>
        MySqlTestStore.ServerVersion.IsMariaDb
            ? MariaDbJsonColumnAlterSql
            : MySqlJsonColumnAlterSql;

    /// <summary>
    /// Creates a migration specification test instance with an isolated SQL baseline log.
    /// </summary>
    /// <param name="fixture">Shared live-database fixture for the migration suite.</param>
    public FullMigrationsMySqlTest(
        FullMigrationsMySqlFixture fixture
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
    }

    protected override bool AssertSchemaNames => false;

    protected override bool AssertIndexFilters => false;

    protected override bool AssertConstraintNames => false;

    protected override string NonDefaultCollation => "utf8mb4_bin";

    /// <summary>
    /// Verifies the full table-settings contract inside one explicitly selected MySQL
    /// database. MySQL schemas are databases, so both sides of the foreign key must name
    /// the same database when the principal is not in the active connection database.
    /// </summary>
    public override async Task Create_table_all_settings()
    {
        var intStoreType = TypeMappingSource.FindMapping(typeof(int))!.StoreType;
        var char11StoreType = TypeMappingSource.FindMapping(typeof(string), storeTypeName: null, size: 11)!.StoreType;

        await Test(
            builder => builder.Entity(
                "Employers",
                entity =>
                {
                    entity.ToTable("Employers", "dbo2");
                    entity.Property<int>("Id");
                    entity.HasKey("Id");
                }),
            _ => { },
            builder => builder.Entity(
                "People",
                entity =>
                {
                    entity.ToTable(
                        "People",
                        "dbo2",
                        table =>
                        {
                            table.HasCheckConstraint("CK_People_EmployerId", $"{DelimitIdentifier("EmployerId")} > 0");
                            table.HasComment("Table comment");
                        });

                    entity.Property<int>("CustomId");
                    entity
                        .Property<int>("EmployerId")
                        .HasComment("Employer ID comment");
                    entity
                        .Property<string>("SSN")
                        .HasColumnType(char11StoreType)
                        .UseCollation(NonDefaultCollation)
                        .IsRequired(false);

                    entity.HasKey("CustomId");
                    entity.HasAlternateKey("SSN");
                    entity
                        .HasOne("Employers")
                        .WithMany("People")
                        .HasForeignKey("EmployerId");
                }),
            model =>
            {
                var employersTable = Assert.Single(model.Tables, table => table.Name == "Employers");
                var peopleTable = Assert.Single(model.Tables, table => table.Name == "People");

                Assert.Equal("dbo2", employersTable.Schema);
                Assert.Equal("dbo2", peopleTable.Schema);
                Assert.Collection(
                    peopleTable.Columns.OrderBy(column => column.Name),
                    column =>
                    {
                        Assert.Equal("CustomId", column.Name);
                        Assert.False(column.IsNullable);
                        Assert.Equal(intStoreType, column.StoreType);
                        Assert.Null(column.Comment);
                    },
                    column =>
                    {
                        Assert.Equal("EmployerId", column.Name);
                        Assert.False(column.IsNullable);
                        Assert.Equal(intStoreType, column.StoreType);
                        Assert.Equal("Employer ID comment", column.Comment);
                    },
                    column =>
                    {
                        Assert.Equal("SSN", column.Name);
                        Assert.False(column.IsNullable);
                        Assert.Equal(char11StoreType, column.StoreType);
                        Assert.Null(column.Comment);
                    });

                Assert.Same(
                    peopleTable.Columns.Single(column => column.Name == "CustomId"),
                    Assert.Single(peopleTable.PrimaryKey!.Columns));
                Assert.Same(
                    peopleTable.Columns.Single(column => column.Name == "SSN"),
                    Assert.Single(
                        Assert.Single(peopleTable.UniqueConstraints)
                            .Columns));

                var foreignKey = Assert.Single(peopleTable.ForeignKeys);

                Assert.Same(peopleTable, foreignKey.Table);
                Assert.Same(
                    peopleTable.Columns.Single(column => column.Name == "EmployerId"),
                    Assert.Single(foreignKey.Columns));
                Assert.Same(employersTable, foreignKey.PrincipalTable);
                Assert.Same(employersTable.Columns.Single(), Assert.Single(foreignKey.PrincipalColumns));
                Assert.Equal("Table comment", peopleTable.Comment);
            });
    }

    /// <summary>
    /// Keeps the official filtered-index fact discoverable while documenting the missing
    /// predicate grammar on every supported MySQL-family target.
    /// </summary>
    [SpecEngineLimitationFact(
        "MYSQL-MARIADB-FILTERED-INDEXES",
        "mysql84",
        "mariadb114",
        "mariadb118")]
    public override Task Create_index_with_filter() =>
        base.Create_index_with_filter();

    /// <summary>
    /// Keeps the official unique filtered-index fact discoverable while documenting the
    /// engine boundary that prevents preserving its conditional uniqueness semantics.
    /// </summary>
    [SpecEngineLimitationFact(
        "MYSQL-MARIADB-FILTERED-INDEXES",
        "mysql84",
        "mariadb114",
        "mariadb118")]
    public override Task Create_unique_index_with_filter() =>
        base.Create_unique_index_with_filter();

    /// <summary>
    /// Verifies conversion of a string column to a JSON-owned reference and records the
    /// provider-specific DDL emitted for that relational type change.
    /// </summary>
    public override async Task Convert_string_column_to_a_json_column_containing_reference()
    {
        await Test(
            BuildStringJsonSourceModel,
            BuildJsonReferenceTargetModel,
            AssertStringToJsonDatabaseModel);

        AssertSql(ExpectedJsonColumnAlterSql);
    }

    /// <summary>
    /// Verifies conversion of a string column to a JSON-owned collection and records the
    /// provider-specific DDL emitted for that relational type change.
    /// </summary>
    public override async Task Convert_string_column_to_a_json_column_containing_collection()
    {
        await Test(
            BuildStringJsonSourceModel,
            BuildJsonCollectionTargetModel,
            AssertStringToJsonDatabaseModel);

        AssertSql(ExpectedJsonColumnAlterSql);
    }

    public override Task
        Add_required_primitive_collection_with_custom_default_value_sql_to_existing_table() =>
        Add_required_primitive_collection_with_custom_default_value_sql_to_existing_table_core(
            "JSON_ARRAY()");

    public override Task
        Add_required_primitve_collection_with_custom_default_value_sql_to_existing_table() =>
        Add_required_primitve_collection_with_custom_default_value_sql_to_existing_table_core(
            "JSON_ARRAY()");

    /// <summary>
    /// Activates the upstream-skipped custom-converter migration fact because
    /// the provider preserves the converted required-column default correctly.
    /// </summary>
    [Fact]
    public override Task
        Add_required_primitive_collection_with_custom_converter_to_existing_table() =>
        base.Add_required_primitive_collection_with_custom_converter_to_existing_table();

    /// <summary>
    /// Activates the legacy misspelled variant of the same passing migration
    /// contract so it cannot remain hidden by the upstream skip.
    /// </summary>
    [Fact]
    public override Task
        Add_required_primitve_collection_with_custom_converter_to_existing_table() =>
        base.Add_required_primitve_collection_with_custom_converter_to_existing_table();

    /// <summary>
    /// Verifies every sequence option while adapting the relational schema assertion to
    /// the MySQL-family database boundary.
    /// </summary>
    public override Task Create_sequence_all_settings() =>
        Test(
            _ =>
            {
            },
            builder =>
            {
                builder
                    .HasSequence<long>("TestSequence", "dbo2")
                    .StartsAt(3)
                    .IncrementsBy(2)
                    .HasMin(2)
                    .HasMax(916)
                    .IsCyclic();
            },
            model =>
            {
                var sequence = Assert.Single(model.Sequences);

                Assert.Equal("TestSequence", sequence.Name);
                Assert.Null(sequence.Schema);
                Assert.Equal(3, sequence.StartValue);
                Assert.Equal(2, sequence.IncrementBy);
                Assert.Equal(2, sequence.MinValue);
                Assert.Equal(916, sequence.MaxValue);
                Assert.True(sequence.IsCyclic);
            });

    /// <summary>
    /// Verifies that changing only a relational schema is a no-op on MySQL-family
    /// engines and does not drop or duplicate the sequence.
    /// </summary>
    public override Task Move_sequence() =>
        Test(
            builder => builder.HasSequence<int>("TestSequence"),
            builder => builder.HasSequence<int>("TestSequence", "TestSequenceSchema"),
            model =>
            {
                var sequence = Assert.Single(model.Sequences);

                Assert.Equal("TestSequence", sequence.Name);
                Assert.Null(sequence.Schema);
            });

    /// <summary>
    /// Extends the official migration harness with MySQL's schema-as-database
    /// semantics. Explicitly referenced databases are reverse engineered together
    /// and test-owned secondary databases are dropped even when an assertion fails.
    /// </summary>
    protected override async Task Test(
        IModel sourceModel,
        IModel? targetModel,
        IReadOnlyList<MigrationOperation> operations,
        Action<DatabaseModel> asserter,
        MigrationsSqlGenerationOptions migrationsSqlGenerationOptions = MigrationsSqlGenerationOptions.Default
    )
    {
        using var context = CreateContext();
        var serviceProvider = ((IInfrastructure<IServiceProvider>)context).Instance;
        var migrationsSqlGenerator = serviceProvider.GetRequiredService<IMigrationsSqlGenerator>();
        var modelDiffer = serviceProvider.GetRequiredService<IMigrationsModelDiffer>();
        var migrationsCommandExecutor = serviceProvider.GetRequiredService<IMigrationCommandExecutor>();
        var connection = serviceProvider.GetRequiredService<IRelationalConnection>();
        var databaseModelFactory = serviceProvider.GetRequiredService<IDatabaseModelFactory>();
        var selectedDatabases = GetSelectedDatabases(
            connection.DbConnection.Database,
            sourceModel,
            targetModel);

        try
        {
            using (Fixture.TestSqlLoggerFactory.SuspendRecordingEvents())
            {
                await migrationsCommandExecutor.ExecuteNonQueryAsync(
                    migrationsSqlGenerator.Generate(
                        modelDiffer.GetDifferences(null, sourceModel.GetRelationalModel()),
                        sourceModel,
                        migrationsSqlGenerationOptions),
                    connection);
            }

            await migrationsCommandExecutor.ExecuteNonQueryAsync(
                migrationsSqlGenerator.Generate(
                    operations,
                    targetModel,
                    migrationsSqlGenerationOptions),
                connection);

            var schemaFilter = selectedDatabases.Length > 1
                ? selectedDatabases
                : [];

            var scaffoldedModel = databaseModelFactory.Create(
                context.Database.GetDbConnection(),
                new DatabaseModelFactoryOptions([], schemaFilter));

            asserter?.Invoke(scaffoldedModel);
        }
        finally
        {
            try
            {
                await DropSecondaryDatabasesAsync(
                    context.Database.GetDbConnection(),
                    selectedDatabases.Skip(1));
            }
            finally
            {
                using var _ = Fixture.TestSqlLoggerFactory.SuspendRecordingEvents();
                await Fixture.TestStore.CleanAsync(context);
            }
        }
    }

    /// <summary>
    /// MySQL fixture for the official live migration suite.
    /// </summary>
    public sealed class FullMigrationsMySqlFixture : MigrationsFixtureBase
    {
        protected override string StoreName =>
            nameof(FullMigrationsMySqlTest);

        protected override ITestStoreFactory TestStoreFactory =>
            FullMigrationsTestStoreFactory.Instance;

        public override RelationalTestHelpers TestHelpers =>
            MySqlTestHelpers.Instance;
    }

    /// <summary>
    /// Adds schema reverse-engineering services used by migration assertions.
    /// </summary>
    private sealed class FullMigrationsTestStoreFactory
        : MySqlTestStoreFactory
    {
        public static new FullMigrationsTestStoreFactory Instance { get; } =
            new();

        private FullMigrationsTestStoreFactory()
        {
        }

        public override IServiceCollection AddProviderServices(
            IServiceCollection serviceCollection
        ) => serviceCollection
            .AddEntityFrameworkDokaMySqlDesignTime()
            .AddEntityFrameworkDokaMySqlNetTopologySuite();
    }

    private static void BuildStringJsonSourceModel(
        ModelBuilder builder
    )
    {
        builder.Entity(
            "Entity",
            entity =>
            {
                entity.Property<int>("Id").ValueGeneratedOnAdd();
                entity.HasKey("Id");
                entity.Property<string>("Name");
            });
    }

    private static void BuildJsonReferenceTargetModel(
        ModelBuilder builder
    )
    {
        builder.Entity(
            "Entity",
            entity =>
            {
                entity.Property<int>("Id").ValueGeneratedOnAdd();
                entity.HasKey("Id");
                entity.OwnsOne(
                    "Owned",
                    "OwnedReference",
                    owned =>
                    {
                        owned.ToJson("Name");
                        owned.OwnsOne(
                            "Nested",
                            "NestedReference",
                            nested => nested.Property<int>("Number"));
                        owned.OwnsMany(
                            "Nested2",
                            "NestedCollection",
                            nested => nested.Property<int>("Number2"));
                        owned.Property<DateTime>("Date");
                    });
            });
    }

    private static void BuildJsonCollectionTargetModel(
        ModelBuilder builder
    )
    {
        builder.Entity(
            "Entity",
            entity =>
            {
                entity.Property<int>("Id").ValueGeneratedOnAdd();
                entity.HasKey("Id");
                entity.OwnsMany(
                    "Owned2",
                    "OwnedCollection",
                    owned =>
                    {
                        owned.OwnsOne(
                            "Nested3",
                            "NestedReference2",
                            nested => nested.Property<int>("Number3"));
                        owned.OwnsMany(
                            "Nested4",
                            "NestedCollection2",
                            nested => nested.Property<int>("Number4"));
                        owned.Property<DateTime>("Date2");
                        owned.ToJson("Name");
                    });
            });
    }

    private static void AssertStringToJsonDatabaseModel(
        DatabaseModel model
    )
    {
        Assert.Collection(
            model.Tables.Single().Columns,
            column => Assert.Equal("Id", column.Name),
            column => Assert.Equal("Name", column.Name));
    }

    private static string[] GetSelectedDatabases(
        string activeDatabase,
        IModel sourceModel,
        IModel? targetModel
    )
    {
        var databases = new List<string>
        {
            activeDatabase,
        };

        databases.AddRange(
            sourceModel
                .GetRelationalModel()
                .Tables.Select(table => table.Schema)
                .Concat(
                    targetModel
                        ?.GetRelationalModel()
                        .Tables.Select(table => table.Schema)
                    ?? [])
                .Where(schema => !string.IsNullOrWhiteSpace(schema))
                .Select(schema => schema!));

        return databases
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task DropSecondaryDatabasesAsync(
        DbConnection connection,
        IEnumerable<string> databaseNames
    )
    {
        var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync();
        }

        try
        {
            foreach (var databaseName in databaseNames)
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"DROP DATABASE IF EXISTS {MySqlIdentifierEscaping.DelimitIdentifier(databaseName)};";
                await command.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
