namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the fail-closed registration and exact-type ownership contract for
/// custom migration-operation handlers.
/// </summary>
public sealed class MySqlMigrationOperationHandlerRegistryTests
{
    [Fact]
    public void Registry_snapshots_handler_metadata_once()
    {
        var handler = new CountingHandler("sample.first", typeof(FirstOperation));

        var registry = new MySqlMigrationOperationHandlerRegistry([handler]);

        var registration = AssertRegistration(registry, typeof(FirstOperation));

        Assert.Same(handler, registration.Handler);
        Assert.Equal("sample.first", registration.HandlerId);
        Assert.Equal(1, handler.HandlerIdReadCount);
        Assert.Equal(1, handler.OperationTypeReadCount);
    }

    [Fact]
    public void Separate_handler_instances_of_the_same_implementation_can_own_distinct_types()
    {
        var first = new CountingHandler("sample.first", typeof(FirstOperation));
        var second = new CountingHandler("sample.second", typeof(SecondOperation));

        var registry = new MySqlMigrationOperationHandlerRegistry([second, first]);

        Assert.Same(
            first,
            AssertRegistration(registry, typeof(FirstOperation))
                .Handler);
        Assert.Same(
            second,
            AssertRegistration(registry, typeof(SecondOperation))
                .Handler);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("single")]
    [InlineData("bad segment")]
    [InlineData("bad..segment")]
    [InlineData(".bad")]
    [InlineData("bad.")]
    [InlineData("sample.\u00FCnicode")]
    public void Invalid_handler_ids_fail_registration(
        string? handlerId
    )
    {
        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            new MySqlMigrationOperationHandlerRegistry([new CountingHandler(handlerId!, typeof(FirstOperation))]));

        Assert.Equal(MySqlMigrationHandlerFailureCode.InvalidRegistration, exception.FailureCode);
        Assert.Null(exception.HandlerId);
    }

    [Fact]
    public void Handler_ids_longer_than_200_ascii_characters_fail_registration()
    {
        var handlerId = $"sample.{new string('a', 194)}";

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            new MySqlMigrationOperationHandlerRegistry([new CountingHandler(handlerId, typeof(FirstOperation))]));

        Assert.Equal(MySqlMigrationHandlerFailureCode.InvalidRegistration, exception.FailureCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Duplicate_handler_ids_fail_independently_of_registration_order(
        bool reverseRegistrationOrder
    )
    {
        IMySqlMigrationOperationHandler[] handlers =
        [
            new CountingHandler("sample.shared", typeof(SecondOperation)),
            new CountingHandler("sample.shared", typeof(FirstOperation)),
        ];

        if (reverseRegistrationOrder)
        {
            Array.Reverse(handlers);
        }

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            new MySqlMigrationOperationHandlerRegistry(handlers));

        Assert.Equal(MySqlMigrationHandlerFailureCode.DuplicateHandlerId, exception.FailureCode);
        Assert.Equal("sample.shared", exception.HandlerId);
        Assert.Equal(typeof(FirstOperation).FullName, exception.OperationType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Duplicate_exact_operation_ownership_fails_independently_of_registration_order(
        bool reverseRegistrationOrder
    )
    {
        IMySqlMigrationOperationHandler[] handlers =
        [
            new CountingHandler("sample.first", typeof(FirstOperation)),
            new CountingHandler("sample.second", typeof(FirstOperation)),
        ];

        if (reverseRegistrationOrder)
        {
            Array.Reverse(handlers);
        }

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            new MySqlMigrationOperationHandlerRegistry(handlers));

        Assert.Equal(MySqlMigrationHandlerFailureCode.DuplicateOperationOwnership, exception.FailureCode);
        Assert.Equal("sample.first", exception.HandlerId);
        Assert.Equal(typeof(FirstOperation).FullName, exception.OperationType);
    }

    [Fact]
    public void Duplicate_handler_instance_fails_through_the_public_handler_id_key()
    {
        var handler = new CountingHandler("sample.first", typeof(FirstOperation));

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            new MySqlMigrationOperationHandlerRegistry([handler, handler]));

        Assert.Equal(MySqlMigrationHandlerFailureCode.DuplicateHandlerId, exception.FailureCode);
        Assert.Equal("sample.first", exception.HandlerId);
        Assert.Equal(typeof(FirstOperation).FullName, exception.OperationType);
        Assert.Equal(2, handler.HandlerIdReadCount);
        Assert.Equal(2, handler.OperationTypeReadCount);
    }

    [Fact]
    public void Null_handler_fails_registration()
    {
        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            new MySqlMigrationOperationHandlerRegistry([null!]));

        Assert.Equal(MySqlMigrationHandlerFailureCode.InvalidRegistration, exception.FailureCode);
    }

    [Theory]
    [MemberData(nameof(InvalidOperationTypes))]
    public void Non_concrete_migration_operation_types_fail_registration(
        Type operationType
    )
    {
        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            new MySqlMigrationOperationHandlerRegistry([new CountingHandler("sample.invalid", operationType)]));

        Assert.Equal(MySqlMigrationHandlerFailureCode.InvalidRegistration, exception.FailureCode);
    }

    [Fact]
    public void Every_ef_core_builtin_operation_is_reserved()
    {
        foreach (var operationType in MySqlStandardMigrationOperations.Types)
        {
            var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
                new MySqlMigrationOperationHandlerRegistry([new CountingHandler("sample.reserved", operationType)]));

            Assert.Equal(MySqlMigrationHandlerFailureCode.ReservedOperationType, exception.FailureCode);
            Assert.Equal(operationType.FullName, exception.OperationType);
        }
    }

    [Fact]
    public void Ef_core_dispatch_and_provider_reserved_operation_sets_agree()
    {
        // This private field is intentionally inspected only in the test suite.
        // A supported EF Core patch that changes exact built-in dispatch must
        // fail qualification until the provider ownership set is reconciled.
        var generateActionsField = typeof(MigrationsSqlGenerator).GetField(
            "GenerateActions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var generateActions = Assert.IsAssignableFrom<IDictionary>(generateActionsField?.GetValue(null));
        var efCoreOperationTypes = generateActions
            .Keys.Cast<Type>()
            .ToHashSet();

        Assert.True(
            efCoreOperationTypes.SetEquals(MySqlStandardMigrationOperations.Types),
            "The provider reserved-operation set drifted from EF Core's exact dispatch table.");
    }

    [Fact]
    public void Getter_failure_is_wrapped_without_copying_its_message()
    {
        var secret = "server=private;password=do-not-log";
        var source = new InvalidOperationException(secret);
        var handler = new ThrowingMetadataHandler(source);

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            new MySqlMigrationOperationHandlerRegistry([handler]));

        Assert.Equal(MySqlMigrationHandlerFailureCode.InvalidRegistration, exception.FailureCode);
        Assert.Same(source, exception.InnerException);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    public static TheoryData<Type> InvalidOperationTypes => new()
    {
        null!,
        typeof(string),
        typeof(MigrationOperation),
        typeof(AbstractOperation),
        typeof(OpenGenericOperation<>),
    };

    private static MySqlMigrationOperationHandlerRegistry.Registration AssertRegistration(
        MySqlMigrationOperationHandlerRegistry registry,
        Type operationType
    )
    {
        Assert.True(registry.TryGet(operationType, out var registration));
        return registration;
    }

    private sealed class CountingHandler : IMySqlMigrationOperationHandler
    {
        private readonly string _handlerId;
        private readonly Type _operationType;

        public CountingHandler(
            string handlerId,
            Type operationType
        )
        {
            _handlerId = handlerId;
            _operationType = operationType;
        }

        public int HandlerIdReadCount { get; private set; }

        public int OperationTypeReadCount { get; private set; }

        public string HandlerId
        {
            get
            {
                HandlerIdReadCount++;
                return _handlerId;
            }
        }

        public Type OperationType
        {
            get
            {
                OperationTypeReadCount++;
                return _operationType;
            }
        }

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        ) => throw new NotSupportedException();
    }

    private sealed class ThrowingMetadataHandler : IMySqlMigrationOperationHandler
    {
        private readonly Exception _exception;

        public ThrowingMetadataHandler(
            Exception exception
        )
        {
            _exception = exception;
        }

        public string HandlerId => throw _exception;

        public Type OperationType => typeof(FirstOperation);

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        ) => throw new NotSupportedException();
    }

    private sealed class FirstOperation : MigrationOperation;

    private sealed class SecondOperation : MigrationOperation;

    private abstract class AbstractOperation : MigrationOperation;

    private sealed class OpenGenericOperation<T> : MigrationOperation;
}
