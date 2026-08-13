using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Exercises the public migration-operation handler SPI through EF Core's real
/// scoped runtime service graph.
/// </summary>
public sealed class MySqlMigrationOperationHandlerTests
{
    [Theory]
    [MemberData(nameof(GenerationModes))]
    public void Custom_handler_preserves_command_order_boundaries_and_generation_context(
        MigrationsSqlGenerationOptions options
    )
    {
        using var serviceProvider = CreateServiceProvider(
            [typeof(BaselineRenderingHandler)],
            registerBeforeProvider: true);
        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var handler = context
            .GetService<IEnumerable<IMySqlMigrationOperationHandler>>()
            .OfType<BaselineRenderingHandler>()
            .Single();

        var operation = new FirstCustomOperation("payload-not-telemetry");

        var commands = generator.Generate([operation], context.Model, options);

        Assert.Collection(
            commands,
            command =>
            {
                Assert.Equal("SELECT 1;" + Environment.NewLine, command.CommandText);
                Assert.True(command.TransactionSuppressed);
            },
            command =>
            {
                Assert.Equal("SELECT 2;", command.CommandText);
                Assert.False(command.TransactionSuppressed);
            });
        Assert.NotNull(handler.LastContext);
        Assert.Same(operation, handler.LastContext.Operation);
        Assert.Same(context.Model, handler.LastContext.Model);
        Assert.Equal(options, handler.LastContext.Options);
        Assert.Equal(0, handler.LastContext.OperationOrdinal);
        Assert.Equal(new Version(8, 4, 11), handler.LastContext.ServerVersion.Version);
        Assert.Equal(
            MySqlMigrationFeatureSupport.Emulated,
            handler.LastContext.Features.GetSupport(MySqlMigrationFeature.Sequences));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Independent_handlers_coexist_and_dispatch_by_exact_runtime_type(
        bool registerBeforeProvider,
        bool reverseHandlerOrder
    )
    {
        Type[] handlerTypes = reverseHandlerOrder
            ? [typeof(BaselineRenderingHandler), typeof(SecondHandler)]
            : [typeof(SecondHandler), typeof(BaselineRenderingHandler)];

        using var serviceProvider = CreateServiceProvider(handlerTypes, registerBeforeProvider);
        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var handlers = context
            .GetService<IEnumerable<IMySqlMigrationOperationHandler>>()
            .ToArray();
        var first = handlers
            .OfType<BaselineRenderingHandler>()
            .Single();
        var second = handlers
            .OfType<SecondHandler>()
            .Single();

        var commands = generator.Generate(
            [
                new FirstCustomOperation("first"),
                new SecondCustomOperation()
            ],
            context.Model);

        Assert.Equal(3, commands.Count);
        Assert.Equal("SELECT 1;" + Environment.NewLine, commands[0].CommandText);
        Assert.Equal("SELECT 2;", commands[1].CommandText);
        Assert.Equal("SELECT 3;", commands[2].CommandText);
        Assert.Equal(0, first.LastContext?.OperationOrdinal);
        Assert.Equal(1, second.LastContext?.OperationOrdinal);
    }

    [Fact]
    public void Handler_ownership_does_not_match_derived_operation_types()
    {
        using var serviceProvider = CreateServiceProvider(
            [typeof(BaselineRenderingHandler)],
            registerBeforeProvider: true);
        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var handler = context
            .GetService<IEnumerable<IMySqlMigrationOperationHandler>>()
            .OfType<BaselineRenderingHandler>()
            .Single();

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            generator.Generate([new DerivedCustomOperation()], context.Model));

        Assert.Equal(MySqlMigrationHandlerFailureCode.UnknownOperationType, exception.FailureCode);
        Assert.Null(handler.LastContext);
    }

    [Fact]
    public void Unknown_custom_operation_fails_closed()
    {
        using var serviceProvider = CreateServiceProvider([], registerBeforeProvider: true);
        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            generator.Generate([new FirstCustomOperation("unknown")], context.Model));

        Assert.Equal(MySqlMigrationHandlerFailureCode.UnknownOperationType, exception.FailureCode);
        Assert.Null(exception.HandlerId);
        Assert.Equal(typeof(FirstCustomOperation).FullName, exception.OperationType);
        Assert.Equal(0, exception.OperationOrdinal);
    }

    [Fact]
    public void Handler_exception_is_wrapped_without_copying_sensitive_text()
    {
        const string sensitiveText = "password=never-log-this";
        const string sensitiveData = "tenant=never-export-this";
        using var serviceProvider = CreateServiceProvider([typeof(ThrowingHandler)], registerBeforeProvider: true);
        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            generator.Generate([new FirstCustomOperation("secret")], context.Model));

        Assert.Equal(MySqlMigrationHandlerFailureCode.HandlerFailed, exception.FailureCode);
        var innerException = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(sensitiveData, innerException.Data["private-context"]);
        Assert.DoesNotContain(sensitiveText, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveData, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_handler_result_fails_before_any_command_is_returned()
    {
        using var serviceProvider = CreateServiceProvider([typeof(NullResultHandler)], registerBeforeProvider: true);
        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            generator.Generate([new FirstCustomOperation("invalid")], context.Model));

        Assert.Equal(MySqlMigrationHandlerFailureCode.InvalidHandlerResult, exception.FailureCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Invalid_staged_command_never_mutates_the_outer_builder(
        int invalidCommandIndex
    )
    {
        using var serviceProvider = CreateServiceProvider(
            [typeof(InvalidCommandResultHandler)],
            registerBeforeProvider: true);
        using var context = CreateContext(serviceProvider);
        var generator = Assert.IsType<MySqlMigrationsSqlGenerator>(context.GetService<IMigrationsSqlGenerator>());
        var builder = new MigrationCommandListBuilder(context.GetService<MigrationsSqlGeneratorDependencies>());
        var generate = typeof(MySqlMigrationsSqlGenerator).GetMethod(
            "Generate",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(MigrationOperation),
                typeof(IModel),
                typeof(MigrationCommandListBuilder)
            ],
            modifiers: null);

        var invocation = Assert.Throws<TargetInvocationException>(() => generate!.Invoke(
            generator,
            [
                new InvalidResultOperation(invalidCommandIndex),
                context.Model,
                builder,
            ]));
        var exception = Assert.IsType<MySqlMigrationOperationHandlerException>(invocation.InnerException);

        Assert.Equal(MySqlMigrationHandlerFailureCode.InvalidHandlerResult, exception.FailureCode);
        Assert.Empty(builder.GetCommandList());
    }

    [Fact]
    public void Stateful_handler_result_is_snapshotted_once_before_validation()
    {
        using var serviceProvider = CreateServiceProvider(
            [typeof(StatefulResultHandler)],
            registerBeforeProvider: true);
        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var handler = context
            .GetService<IEnumerable<IMySqlMigrationOperationHandler>>()
            .OfType<StatefulResultHandler>()
            .Single();

        var commands = generator.Generate([new StatefulResultOperation()], context.Model);

        Assert.Collection(
            commands,
            command => Assert.Equal("SELECT 1;", command.CommandText),
            command => Assert.Equal("SELECT 2;", command.CommandText));
        Assert.Equal(1, handler.Commands?.EnumerationCount);
    }

    [Fact]
    public void Rendering_context_expires_at_the_handler_boundary()
    {
        using var serviceProvider = CreateServiceProvider(
            [typeof(BaselineRenderingHandler)],
            registerBeforeProvider: true);
        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var handler = context
            .GetService<IEnumerable<IMySqlMigrationOperationHandler>>()
            .OfType<BaselineRenderingHandler>()
            .Single();

        _ = generator.Generate([new FirstCustomOperation("first")], context.Model);

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            handler.LastContext!.RenderStandardOperation(new SqlOperation { Sql = "SELECT 4;" }));
        Assert.Equal(MySqlMigrationHandlerFailureCode.ContextExpired, exception.FailureCode);
    }

    [Fact]
    public void Rendering_context_rejects_custom_operations()
    {
        using var serviceProvider = CreateServiceProvider(
            [typeof(CustomRenderingHandler)],
            registerBeforeProvider: true);
        using var context = CreateContext(serviceProvider);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            generator.Generate([new FirstCustomOperation("first")], context.Model));

        Assert.Equal(MySqlMigrationHandlerFailureCode.UnknownOperationType, exception.FailureCode);
    }

    [Fact]
    public void Baseline_renderer_preserves_every_reserved_operation_command_sequence()
    {
        foreach (var serverVersion in ActiveLtsServerVersions)
        {
            using var serviceProvider = CreateServiceProvider(
                [typeof(BaselineRenderingHandler)],
                registerBeforeProvider: true);
            using var context = CreateContext(serviceProvider, serverVersion);
            var generator = context.GetService<IMigrationsSqlGenerator>();
            var operations = CreateStandardOperationFixtures();

            Assert.True(
                operations
                    .Select(operation => operation.GetType())
                    .ToHashSet()
                    .SetEquals(MySqlStandardMigrationOperations.Types),
                "The baseline-renderer fixture set drifted from the reserved operation set.");

            foreach (var operation in operations)
            {
                var expected = generator.Generate([operation], context.Model);
                var actual = generator.Generate(
                    [new FirstCustomOperation("baseline-parity", operation)],
                    context.Model);

                Assert.Equal(expected.Count + 1, actual.Count);

                for (var index = 0; index < expected.Count; index++)
                {
                    Assert.Equal(expected[index].CommandText, actual[index].CommandText);
                    Assert.Equal(expected[index].TransactionSuppressed, actual[index].TransactionSuppressed);
                }

                Assert.Equal("SELECT 2;", actual[^1].CommandText);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Plugin_options_extension_registers_handler_before_or_after_use_mysql(
        bool registerBeforeUseMySql
    )
    {
        var builder = new DbContextOptionsBuilder<HandlerContext>();

        if (registerBeforeUseMySql)
        {
            AddPlugin<SecondHandler>(builder);
        }

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 11)));

        if (!registerBeforeUseMySql)
        {
            AddPlugin<SecondHandler>(builder);
        }

        using var context = new HandlerContext(builder.Options);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var command = Assert.Single(generator.Generate([new SecondCustomOperation()], context.Model));

        Assert.Equal("SELECT 3;", command.CommandText);
    }

    [Fact]
    public void Independent_plugin_options_extensions_compose_in_the_internal_service_graph()
    {
        var builder = new DbContextOptionsBuilder<HandlerContext>();
        AddPlugin<BaselineRenderingHandler>(builder);
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 11)));
        AddPlugin<SecondHandler>(builder);

        using var context = new HandlerContext(builder.Options);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var commands = generator.Generate(
            [
                new FirstCustomOperation("first"),
                new SecondCustomOperation(),
            ],
            context.Model);

        Assert.Equal(3, commands.Count);
    }

    [Theory]
    [InlineData(typeof(DuplicateIdHandler), MySqlMigrationHandlerFailureCode.DuplicateHandlerId)]
    [InlineData(typeof(DuplicateOperationHandler), MySqlMigrationHandlerFailureCode.DuplicateOperationOwnership)]
    public void Conflicting_plugin_options_extensions_fail_service_graph_construction(
        Type conflictingHandlerType,
        MySqlMigrationHandlerFailureCode expectedFailureCode
    )
    {
        var builder = new DbContextOptionsBuilder<HandlerContext>();
        AddPlugin<BaselineRenderingHandler>(builder);
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 11)));
        if (conflictingHandlerType == typeof(DuplicateIdHandler))
        {
            AddPlugin<DuplicateIdHandler>(builder);
        }
        else
        {
            Assert.Equal(typeof(DuplicateOperationHandler), conflictingHandlerType);
            AddPlugin<DuplicateOperationHandler>(builder);
        }

        using var context = new HandlerContext(builder.Options);

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(context.GetService<IMigrationsSqlGenerator>);

        Assert.Equal(expectedFailureCode, exception.FailureCode);
    }

    public static TheoryData<MigrationsSqlGenerationOptions> GenerationModes => new()
    {
        MigrationsSqlGenerationOptions.Default,
        MigrationsSqlGenerationOptions.Script,
        MigrationsSqlGenerationOptions.Idempotent,
        MigrationsSqlGenerationOptions.NoTransactions,
        MigrationsSqlGenerationOptions.Script | MigrationsSqlGenerationOptions.Idempotent,
        MigrationsSqlGenerationOptions.Script | MigrationsSqlGenerationOptions.NoTransactions,
        MigrationsSqlGenerationOptions.Idempotent | MigrationsSqlGenerationOptions.NoTransactions,
        MigrationsSqlGenerationOptions.Script
        | MigrationsSqlGenerationOptions.Idempotent
        | MigrationsSqlGenerationOptions.NoTransactions,
        (MigrationsSqlGenerationOptions)8,
    };

    private static IReadOnlyList<MySqlServerVersion> ActiveLtsServerVersions =>
    [
        MySqlServerVersion.MySql(new Version(8, 4, 11)),
        MySqlServerVersion.MySql(new Version(9, 7, 2)),
        MySqlServerVersion.MariaDb(new Version(10, 11, 18)),
        MySqlServerVersion.MariaDb(new Version(11, 4, 12)),
        MySqlServerVersion.MariaDb(new Version(11, 8, 8)),
        MySqlServerVersion.MariaDb(new Version(12, 3, 2)),
    ];

    private static IReadOnlyList<MigrationOperation> CreateStandardOperationFixtures()
    {
        var alterTable = new AlterTableOperation
        {
            Name = "Entries",
            Comment = "new comment",
            OldTable = { Comment = "old comment" },
        };

        return
        [
            new AddColumnOperation
            {
                Table = "Entries",
                Name = "Value",
                ClrType = typeof(int),
                ColumnType = "int",
                IsNullable = true,
            },
            new AddForeignKeyOperation
            {
                Name = "FK_Entries_Parents",
                Table = "Entries",
                Columns = ["ParentId"],
                PrincipalTable = "Parents",
                PrincipalColumns = ["Id"],
            },
            new AddPrimaryKeyOperation
            {
                Name = "PK_Entries",
                Table = "Entries",
                Columns = ["Id"],
            },
            new AddUniqueConstraintOperation
            {
                Name = "AK_Entries_Code",
                Table = "Entries",
                Columns = ["Code"],
            },
            new AlterColumnOperation
            {
                Table = "Entries",
                Name = "Value",
                ClrType = typeof(long),
                ColumnType = "bigint",
                IsNullable = true,
                OldColumn = new AddColumnOperation
                {
                    Table = "Entries",
                    Name = "Value",
                    ClrType = typeof(int),
                    ColumnType = "int",
                    IsNullable = true,
                },
            },
            new AlterDatabaseOperation(),
            new AlterSequenceOperation
            {
                Name = "EntrySequence",
                IncrementBy = 2,
            },
            alterTable,
            new AddCheckConstraintOperation
            {
                Name = "CK_Entries_Value",
                Table = "Entries",
                Sql = "`Value` >= 0",
            },
            new CreateIndexOperation
            {
                Name = "IX_Entries_Code",
                Table = "Entries",
                Columns = ["Code"],
            },
            new CreateSequenceOperation
            {
                Name = "EntrySequence",
                ClrType = typeof(long),
                StartValue = 1,
                IncrementBy = 1,
            },
            CreateTableFixture(),
            new DropColumnOperation
            {
                Table = "Entries",
                Name = "Value",
            },
            new DropForeignKeyOperation
            {
                Name = "FK_Entries_Parents",
                Table = "Entries",
            },
            new DropIndexOperation
            {
                Name = "IX_Entries_Code",
                Table = "Entries",
            },
            new DropPrimaryKeyOperation
            {
                Name = "PK_Entries",
                Table = "Entries",
            },
            new DropSchemaOperation { Name = "tenant_database" },
            new DropSequenceOperation { Name = "EntrySequence" },
            new DropTableOperation { Name = "Entries" },
            new DropUniqueConstraintOperation
            {
                Name = "AK_Entries_Code",
                Table = "Entries",
            },
            new DropCheckConstraintOperation
            {
                Name = "CK_Entries_Value",
                Table = "Entries",
            },
            new EnsureSchemaOperation { Name = "tenant_database" },
            new RenameColumnOperation
            {
                Table = "Entries",
                Name = "Value",
                NewName = "RenamedValue",
            },
            new RenameIndexOperation
            {
                Table = "Entries",
                Name = "IX_Entries_Code",
                NewName = "IX_Entries_Value",
            },
            new RenameSequenceOperation
            {
                Name = "EntrySequence",
                NewName = "RenamedSequence",
            },
            new RenameTableOperation
            {
                Name = "Entries",
                NewName = "RenamedEntries",
            },
            new RestartSequenceOperation
            {
                Name = "EntrySequence",
                StartValue = 5,
            },
            new SqlOperation
            {
                Sql = "SELECT 1;",
                SuppressTransaction = true,
            },
            new InsertDataOperation
            {
                Table = "Entries",
                Columns = ["Id"],
                ColumnTypes = ["int"],
                Values = new object[,] { { 1 } },
            },
            new DeleteDataOperation
            {
                Table = "Entries",
                KeyColumns = ["Id"],
                KeyColumnTypes = ["int"],
                KeyValues = new object[,] { { 1 } },
            },
            new UpdateDataOperation
            {
                Table = "Entries",
                Columns = ["Value"],
                ColumnTypes = ["int"],
                Values = new object[,] { { 2 } },
                KeyColumns = ["Id"],
                KeyColumnTypes = ["int"],
                KeyValues = new object[,] { { 1 } },
            },
        ];
    }

    private static CreateTableOperation CreateTableFixture()
    {
        var operation = new CreateTableOperation { Name = "Entries" };
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = operation.Name,
                Name = "Id",
                ClrType = typeof(int),
                ColumnType = "int",
                IsNullable = false,
            });
        operation.PrimaryKey = new AddPrimaryKeyOperation
        {
            Name = "PK_Entries",
            Table = operation.Name,
            Columns = ["Id"],
        };

        return operation;
    }

    private static ServiceProvider CreateServiceProvider(
        IReadOnlyList<Type> handlerTypes,
        bool registerBeforeProvider
    )
    {
        var services = new ServiceCollection();

        if (registerBeforeProvider)
        {
            RegisterHandlers(services, handlerTypes);
        }

        services.AddEntityFrameworkDokaMySql();

        if (!registerBeforeProvider)
        {
            RegisterHandlers(services, handlerTypes);
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static void RegisterHandlers(
        IServiceCollection services,
        IEnumerable<Type> handlerTypes
    )
    {
        foreach (var handlerType in handlerTypes)
        {
            services.TryAddEnumerable(
                new ServiceDescriptor(typeof(IMySqlMigrationOperationHandler), handlerType, ServiceLifetime.Scoped));
        }
    }

    private static void AddPlugin<THandler>(
        DbContextOptionsBuilder builder
    )
        where THandler : class, IMySqlMigrationOperationHandler =>
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new HandlerOptionsExtension<THandler>());

    private static HandlerContext CreateContext(
        IServiceProvider serviceProvider,
        MySqlServerVersion? serverVersion = null
    )
    {
        var builder = new DbContextOptionsBuilder<HandlerContext>();
        builder
            .UseInternalServiceProvider(serviceProvider)
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=password;",
                serverVersion ?? MySqlServerVersion.MySql(new Version(8, 4, 11)));

        return new HandlerContext(builder.Options);
    }

    private class FirstCustomOperation : MigrationOperation
    {
        public FirstCustomOperation(
            string payload,
            MigrationOperation? standardOperation = null
        )
        {
            Payload = payload;
            StandardOperation = standardOperation;
        }

        public string Payload { get; }

        public MigrationOperation? StandardOperation { get; }
    }

    private sealed class DerivedCustomOperation : FirstCustomOperation
    {
        public DerivedCustomOperation() : base("derived") { }
    }

    private sealed class SecondCustomOperation : MigrationOperation;

    private sealed class InvalidResultOperation : MigrationOperation
    {
        public InvalidResultOperation(
            int invalidCommandIndex
        )
        {
            InvalidCommandIndex = invalidCommandIndex;
        }

        public int InvalidCommandIndex { get; }
    }

    private sealed class StatefulResultOperation : MigrationOperation;

    private sealed class BaselineRenderingHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "tests.baseline";

        public Type OperationType => typeof(FirstCustomOperation);

        public MySqlMigrationOperationContext? LastContext { get; private set; }

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            LastContext = context;
            var operation = (FirstCustomOperation)context.Operation;
            var baseline = context.RenderStandardOperation(
                operation.StandardOperation
                ?? new SqlOperation
                {
                    Sql = "SELECT 1;",
                    SuppressTransaction = true,
                });

            return MySqlMigrationOperationResult.Generated(
                baseline.Append(MySqlMigrationCommandSpec.Create("SELECT 2;")),
                "generated");
        }
    }

    private sealed class SecondHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "tests.second";

        public Type OperationType => typeof(SecondCustomOperation);

        public MySqlMigrationOperationContext? LastContext { get; private set; }

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            LastContext = context;
            return MySqlMigrationOperationResult.Generated(
                [MySqlMigrationCommandSpec.Create("SELECT 3;")],
                "generated");
        }
    }

    private sealed class DuplicateIdHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "tests.baseline";

        public Type OperationType => typeof(SecondCustomOperation);

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        ) => throw new NotSupportedException();
    }

    private sealed class DuplicateOperationHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "tests.duplicate_operation";

        public Type OperationType => typeof(FirstCustomOperation);

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        ) => throw new NotSupportedException();
    }

    private sealed class ThrowingHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "tests.throwing";

        public Type OperationType => typeof(FirstCustomOperation);

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            var exception = new InvalidOperationException("password=never-log-this")
            {
                Data =
                {
                    ["private-context"] = "tenant=never-export-this",
                },
            };
            throw exception;
        }
    }

    private sealed class NullResultHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "tests.null_result";

        public Type OperationType => typeof(FirstCustomOperation);

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        ) => null!;
    }

    private sealed class InvalidCommandResultHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "tests.invalid_command";

        public Type OperationType => typeof(InvalidResultOperation);

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            var operation = (InvalidResultOperation)context.Operation;
            var commands = new[]
            {
                MySqlMigrationCommandSpec.Create("SELECT 1;"),
                MySqlMigrationCommandSpec.Create("SELECT 2;"),
                MySqlMigrationCommandSpec.Create("SELECT 3;"),
            };
            var commandConstructor = typeof(MySqlMigrationCommandSpec).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [
                    typeof(string),
                    typeof(bool),
                ],
                modifiers: null);
            commands[operation.InvalidCommandIndex] = (MySqlMigrationCommandSpec)commandConstructor!.Invoke(
            [
                " ",
                false,
            ]);
            var resultConstructor = typeof(MySqlMigrationOperationResult).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [
                    typeof(IReadOnlyList<MySqlMigrationCommandSpec>),
                    typeof(string),
                ],
                modifiers: null);

            // The public factories prevent this shape. Reflection models a
            // hostile plugin so the provider's independent trust-boundary
            // validation and all-or-nothing append remain directly tested.
            return (MySqlMigrationOperationResult)resultConstructor!.Invoke(
            [
                Array.AsReadOnly(commands),
                "generated",
            ]);
        }
    }

    private sealed class StatefulResultHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "tests.stateful_result";

        public Type OperationType => typeof(StatefulResultOperation);

        public StatefulCommandList? Commands { get; private set; }

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            var commandConstructor = typeof(MySqlMigrationCommandSpec).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [
                    typeof(string),
                    typeof(bool),
                ],
                modifiers: null);

            var invalidCommand = (MySqlMigrationCommandSpec)commandConstructor!.Invoke([" ", false]);

            Commands = new StatefulCommandList(
                [
                    MySqlMigrationCommandSpec.Create("SELECT 1;"),
                    MySqlMigrationCommandSpec.Create("SELECT 2;"),
                ],
                [
                    MySqlMigrationCommandSpec.Create("SELECT 1;"),
                    invalidCommand,
                ]);

            var resultConstructor = typeof(MySqlMigrationOperationResult).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [
                    typeof(IReadOnlyList<MySqlMigrationCommandSpec>),
                    typeof(string),
                ],
                modifiers: null);

            // A stateful list models the strongest IReadOnlyList adversary: a
            // second enumeration yields different data. The provider must copy
            // once and validate exactly the command snapshot it later appends.
            return (MySqlMigrationOperationResult)resultConstructor!.Invoke([Commands, "generated"]);
        }
    }

    private sealed class StatefulCommandList : IReadOnlyList<MySqlMigrationCommandSpec>
    {
        private readonly IReadOnlyList<MySqlMigrationCommandSpec> _firstEnumeration;
        private readonly IReadOnlyList<MySqlMigrationCommandSpec> _laterEnumerations;

        public StatefulCommandList(
            IReadOnlyList<MySqlMigrationCommandSpec> firstEnumeration,
            IReadOnlyList<MySqlMigrationCommandSpec> laterEnumerations
        )
        {
            _firstEnumeration = firstEnumeration;
            _laterEnumerations = laterEnumerations;
        }

        public int EnumerationCount { get; private set; }

        public int Count => _firstEnumeration.Count;

        public MySqlMigrationCommandSpec this[
            int index
        ] => _firstEnumeration[index];

        public IEnumerator<MySqlMigrationCommandSpec> GetEnumerator()
        {
            EnumerationCount++;
            return (EnumerationCount == 1 ? _firstEnumeration : _laterEnumerations).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CustomRenderingHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "tests.custom_rendering";

        public Type OperationType => typeof(FirstCustomOperation);

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            _ = context.RenderStandardOperation(new SecondCustomOperation());
            throw new InvalidOperationException("The custom rendering guard did not run.");
        }
    }

    private sealed class HandlerContext : DbContext
    {
        public HandlerContext(
            DbContextOptions<HandlerContext> options
        ) : base(options) { }
    }

    private sealed class HandlerOptionsExtension<THandler> : IDbContextOptionsExtension
        where THandler : class, IMySqlMigrationOperationHandler
    {
        private DbContextOptionsExtensionInfo? _info;

        public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

        public void ApplyServices(
            IServiceCollection services
        ) => services.TryAddEnumerable(ServiceDescriptor.Scoped<IMySqlMigrationOperationHandler, THandler>());

        public void Validate(
            IDbContextOptions options
        )
        { }

        private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
        {
            public ExtensionInfo(
                IDbContextOptionsExtension extension
            ) : base(extension) { }

            public override bool IsDatabaseProvider => false;

            public override string LogFragment => $"migration-handler={typeof(THandler).FullName} ";

            public override int GetServiceProviderHashCode() => typeof(THandler).GetHashCode();

            public override void PopulateDebugInfo(
                IDictionary<string, string> debugInfo
            ) => debugInfo[$"DokaMySqlMigrationHandler:{typeof(THandler).FullName}"] = "1";

            public override bool ShouldUseSameServiceProvider(
                DbContextOptionsExtensionInfo other
            ) => other is ExtensionInfo;
        }
    }
}
