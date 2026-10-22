namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    private sealed class KeylessPeopleContext : DbContext
    {
        /// <summary>
        /// Initializes the keyless people context fixture.
        /// </summary>
        public KeylessPeopleContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigurePeople(modelBuilder, "People", hasKey: false, propertyName: "SomeField");
    }

    private sealed class KeyedPeopleContext : DbContext
    {
        /// <summary>
        /// Initializes the keyed people context fixture.
        /// </summary>
        public KeyedPeopleContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigurePeople(modelBuilder, "People", hasKey: true, propertyName: "SomeField");
    }

    private sealed class KeyedPersonsContext : DbContext
    {
        /// <summary>
        /// Initializes the keyed persons context fixture.
        /// </summary>
        public KeyedPersonsContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigurePeople(modelBuilder, "Persons", hasKey: true, propertyName: "SomeField");
    }

    private sealed class KeyedPeopleWithReplacementContext : DbContext
    {
        /// <summary>
        /// Initializes the keyed people with replacement context fixture.
        /// </summary>
        public KeyedPeopleWithReplacementContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            ConfigurePeople(modelBuilder, "People", hasKey: true, propertyName: "SomeField");
            modelBuilder
                .Entity("Person")
                .Property<int>("ReplacementField");
        }
    }

    private sealed class ReplacementPeopleContext : DbContext
    {
        /// <summary>
        /// Initializes the replacement people context fixture.
        /// </summary>
        public ReplacementPeopleContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigurePeople(modelBuilder, "People", hasKey: false, propertyName: "ReplacementField");
    }

    private static AlterColumnOperation CreateJsonAlterColumn(
        Type clrType,
        bool? isUnicode,
        int? maxLength
    ) => new()
    {
        Name = "Name",
        Table = "Entity",
        ClrType = clrType,
        ColumnType = "json",
        IsUnicode = isUnicode,
        MaxLength = maxLength,
        IsNullable = true,
        OldColumn = new AddColumnOperation
        {
            Name = "Name",
            Table = "Entity",
            ClrType = typeof(string),
            ColumnType = "longtext",
            IsUnicode = true,
            IsNullable = true,
        },
    };

    private static AlterColumnOperation CreateRequiredJsonAlterColumn(
        Type clrType,
        string defaultValueSql
    )
    {
        var operation = CreateJsonAlterColumn(clrType, isUnicode: null, maxLength: null);
        operation.IsNullable = false;
        operation.DefaultValueSql = defaultValueSql;
        operation.OldColumn.IsNullable = true;

        return operation;
    }

    private sealed class FixedMigrationsModelDiffer : IMigrationsModelDiffer
    {
        private readonly MigrationOperation[] _operations;

        /// <summary>
        /// Initializes the fixed migrations model differ fixture.
        /// </summary>
        public FixedMigrationsModelDiffer(
            params MigrationOperation[] operations
        )
        {
            _operations = operations;
        }

        /// <inheritdoc />
        public bool HasDifferences(
            IRelationalModel? source,
            IRelationalModel? target
        ) => _operations.Length > 0;

        /// <inheritdoc />
        public IReadOnlyList<MigrationOperation> GetDifferences(
            IRelationalModel? source,
            IRelationalModel? target
        ) => _operations;
    }

    private static void ConfigurePeople(
        ModelBuilder modelBuilder,
        string tableName,
        bool hasKey,
        string propertyName
    )
    {
        modelBuilder.Entity(
            "Person",
            entity =>
            {
                entity.ToTable(tableName);
                entity.Property<int>(propertyName);

                if (hasKey)
                {
                    entity.HasKey(propertyName);
                }
                else
                {
                    entity.HasNoKey();
                }
            });
    }
}
