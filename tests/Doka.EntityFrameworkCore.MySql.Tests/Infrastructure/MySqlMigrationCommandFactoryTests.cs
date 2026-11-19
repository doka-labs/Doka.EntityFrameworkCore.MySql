namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies that migration-only command construction preserves query rental and custom factory contracts.</summary>
public sealed class MySqlMigrationCommandFactoryTests
{
    /// <summary>Ordinary migration commands retain EF's separate executable and redacted text without query policy wrappers.</summary>
    [Fact]
    public void Migration_factory_builds_plain_commands_with_sensitive_text_redaction()
    {
        // Arrange
        using var services = new ServiceCollection()
            .AddEntityFrameworkDokaMySql()
            .BuildServiceProvider();

        using var context = CreateContext(services);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var dependencies = ReadMigrationDependencies(generator);
        var builder = dependencies.CommandBuilderFactory.Create();
        builder.Append("SELECT '");
        builder.Append("migration-secret-canary", sensitive: true);
        builder.Append("'");

        // Act
        var command = builder.Build();
        var generated = generator.Generate([new SqlOperation { Sql = "SELECT 1;" }], context.Model);

        // Assert
        Assert.IsType<RelationalCommandBuilderFactory>(dependencies.CommandBuilderFactory);
        Assert.IsType<RelationalCommand>(command);
        Assert.Contains("migration-secret-canary", command.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("migration-secret-canary", command.LogCommandText, StringComparison.Ordinal);
        Assert.NotEqual(command.CommandText, command.LogCommandText);
        Assert.Equal("SELECT 1;",
            Assert
                .Single(generated)
                .CommandText
                .Trim());
    }

    /// <summary>Resolving migration services cannot replace the typed factory or policies used by query command rental.</summary>
    [Fact]
    public void Migration_generation_keeps_the_global_query_factory_and_rented_policy_typed()
    {
        // Arrange
        using var services = new ServiceCollection()
            .AddEntityFrameworkDokaMySql()
            .BuildServiceProvider();

        using var context = CreateContext(services);
        var factory = context.GetService<IRelationalCommandBuilderFactory>();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        _ = generator.Generate([new SqlOperation { Sql = "SELECT 1;" }], context.Model);
        var builder = factory.Create();
        builder.Append("SELECT '");
        builder.Append("query-secret-canary", sensitive: true);
        builder.Append("'");
        var template = Assert.IsType<MySqlRelationalCommand>(builder.Build());
        template.LockingReadMode = MySqlLockingReadMode.NoWait;
        var connection = context.GetService<IRelationalConnection>();

        // Act
        var rented = connection.RentCommand();
        rented.PopulateFrom(template);

        // Assert
        Assert.IsType<MySqlRelationalCommandBuilderFactory>(factory);
        Assert.Same(factory, context.GetService<IRelationalCommandBuilderFactory>());
        Assert.NotSame(factory,
            ReadMigrationDependencies(generator)
                .CommandBuilderFactory);

        Assert.Equal(MySqlLockingReadMode.NoWait,
            Assert.IsType<MySqlRelationalCommand>(rented)
                .LockingReadMode);

        Assert.Equal(template.CommandText, rented.CommandText);
        Assert.Equal(template.LogCommandText, rented.LogCommandText);
        Assert.DoesNotContain("query-secret-canary", rented.LogCommandText, StringComparison.Ordinal);
        connection.ReturnCommand(rented);
    }

    /// <summary>An application-provided factory is retained and actually builds the generated migration command.</summary>
    [Fact]
    public void Custom_command_factory_is_preserved_for_migration_generation()
    {
        // Arrange
        var descriptors = new ServiceCollection().AddEntityFrameworkDokaMySql();
        RecordingFactory? custom = null;
        descriptors.Replace(ServiceDescriptor.Singleton<IRelationalCommandBuilderFactory>(provider =>
        {
            custom = new RecordingFactory(provider.GetRequiredService<RelationalCommandBuilderDependencies>());

            return custom;
        }));

        using var services = descriptors.BuildServiceProvider();
        using var context = CreateContext(services);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var generated = generator.Generate([new SqlOperation { Sql = "SELECT 42;" }], context.Model);

        // Assert
        Assert.NotNull(custom);
        Assert.Same(custom,
            ReadMigrationDependencies(generator)
                .CommandBuilderFactory);

        Assert.Same(custom, context.GetService<IRelationalCommandBuilderFactory>());
        Assert.Equal("SELECT 42;",
            Assert
                .Single(custom.Commands)
                .CommandText
                .Trim());

        Assert.Equal("SELECT 42;",
            Assert
                .Single(generated)
                .CommandText
                .Trim());
    }

    private static MigrationContext CreateContext(
        IServiceProvider services
    ) => new(new DbContextOptionsBuilder<MigrationContext>()
        .UseInternalServiceProvider(services)
        .UseMySql("Server=localhost;Database=migration_factory_unit;User ID=unused;Password=unused;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)))
        .Options);

    private static MigrationsSqlGeneratorDependencies ReadMigrationDependencies(
        IMigrationsSqlGenerator generator
    )
    {
        // WHY: EF11 exposes this record through a protected property. Inspect that
        // declared seam to verify the migration factory without reading private SQL buffers.
        var property = typeof(MigrationsSqlGenerator).GetProperty("Dependencies",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(property);

        return Assert.IsType<MigrationsSqlGeneratorDependencies>(property.GetValue(generator));
    }

    private sealed class MigrationContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public MigrationContext(
            DbContextOptions<MigrationContext> options
        ) : base(options) { }
    }

    private sealed class RecordingFactory : IRelationalCommandBuilderFactory
    {
        private readonly RelationalCommandBuilderDependencies _dependencies;

        /// <summary>Initializes the factory with the dependencies and observations shared by this test.</summary>
        public RecordingFactory(
            RelationalCommandBuilderDependencies dependencies
        )
        {
            _dependencies = dependencies;
        }

        internal List<IRelationalCommand> Commands { get; } = [];

        public IRelationalCommandBuilder Create() => new RecordingBuilder(_dependencies, this);

        private sealed class RecordingBuilder : RelationalCommandBuilder
        {
            private readonly RecordingFactory _owner;

            /// <summary>Initializes the test helper with its supplied dependencies and values.</summary>
            public RecordingBuilder(
                RelationalCommandBuilderDependencies commandDependencies,
                RecordingFactory owner
            ) : base(commandDependencies)
            {
                _owner = owner;
            }

            public override IRelationalCommand Build()
            {
                var command = base.Build();
                _owner.Commands.Add(command);

                return command;
            }
        }
    }
}
