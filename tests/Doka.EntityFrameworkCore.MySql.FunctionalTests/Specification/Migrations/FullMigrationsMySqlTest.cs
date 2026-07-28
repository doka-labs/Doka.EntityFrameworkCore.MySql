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
}
