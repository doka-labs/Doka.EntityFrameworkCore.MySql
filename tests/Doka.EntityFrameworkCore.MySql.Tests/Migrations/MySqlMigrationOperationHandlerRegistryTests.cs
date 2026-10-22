namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the fail-closed registration and exact-type ownership contract for
/// custom migration-operation handlers.
/// </summary>
public sealed class MySqlMigrationOperationHandlerRegistryTests
{
    /// <summary>
    /// Verifies registry snapshots handler metadata once.
    /// </summary>
    [Fact]
    public void Registry_snapshots_handler_metadata_once()
    {
        // Arrange
        var handler = new CountingHandler("sample.first", typeof(FirstOperation));

        // Act
        var registry = new MySqlMigrationOperationHandlerRegistry([handler]);

        // Assert
        var registration = AssertRegistration(registry, typeof(FirstOperation));
        Assert.Same(handler, registration.Handler);
        Assert.Equal("sample.first", registration.HandlerId);
        Assert.Equal(1, handler.HandlerIdReadCount);
        Assert.Equal(1, handler.OperationTypeReadCount);
    }

    /// <summary>
    /// Verifies separate handler instances of the same implementation can own distinct types.
    /// </summary>
    [Fact]
    public void Separate_handler_instances_of_the_same_implementation_can_own_distinct_types()
    {
        // Arrange
        var first = new CountingHandler("sample.first", typeof(FirstOperation));
        var second = new CountingHandler("sample.second", typeof(SecondOperation));
        var registry = new MySqlMigrationOperationHandlerRegistry([second, first]);

        // Act
        var actual = AssertRegistration(registry, typeof(FirstOperation))
                .Handler;

        // Assert
        Assert.Same(
            first,
            actual);
        Assert.Same(
            second,
            AssertRegistration(registry, typeof(SecondOperation))
                .Handler);
    }

    /// <summary>
    /// Verifies invalid handler ids fail registration.
    /// </summary>
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
        // Arrange
        Func<object?> act = () =>
            new MySqlMigrationOperationHandlerRegistry([new CountingHandler(handlerId!, typeof(FirstOperation))]);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlMigrationOperationHandlerException>(recordedException);
        Assert.Equal(MySqlMigrationHandlerFailureCode.InvalidRegistration, exception.FailureCode);
        Assert.Null(exception.HandlerId);
    }

    /// <summary>
    /// Verifies handler ids longer than 200 ascii characters fail registration.
    /// </summary>
    [Fact]
    public void Handler_ids_longer_than_200_ascii_characters_fail_registration()
    {
        // Arrange
        var handlerId = $"sample.{new string('a', 194)}";
        Func<object?> act = () =>
            new MySqlMigrationOperationHandlerRegistry([new CountingHandler(handlerId, typeof(FirstOperation))]);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlMigrationOperationHandlerException>(recordedException);
        Assert.Equal(MySqlMigrationHandlerFailureCode.InvalidRegistration, exception.FailureCode);
    }

    /// <summary>
    /// Verifies duplicate handler ids fail independently of registration order.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Duplicate_handler_ids_fail_independently_of_registration_order(
        bool reverseRegistrationOrder
    )
    {
        // Arrange
        IMySqlMigrationOperationHandler[] handlers =
        [
            new CountingHandler("sample.shared", typeof(SecondOperation)),
            new CountingHandler("sample.shared", typeof(FirstOperation)),
        ];

        if (reverseRegistrationOrder)
        {
            Array.Reverse(handlers);
        }

        Func<object?> act = () =>
            new MySqlMigrationOperationHandlerRegistry(handlers);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlMigrationOperationHandlerException>(recordedException);
        Assert.Equal(MySqlMigrationHandlerFailureCode.DuplicateHandlerId, exception.FailureCode);
        Assert.Equal("sample.shared", exception.HandlerId);
        Assert.Equal(typeof(FirstOperation).FullName, exception.OperationType);
    }

    /// <summary>
    /// Verifies duplicate exact operation ownership fails independently of registration order.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Duplicate_exact_operation_ownership_fails_independently_of_registration_order(
        bool reverseRegistrationOrder
    )
    {
        // Arrange
        IMySqlMigrationOperationHandler[] handlers =
        [
            new CountingHandler("sample.first", typeof(FirstOperation)),
            new CountingHandler("sample.second", typeof(FirstOperation)),
        ];

        if (reverseRegistrationOrder)
        {
            Array.Reverse(handlers);
        }

        Func<object?> act = () =>
            new MySqlMigrationOperationHandlerRegistry(handlers);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlMigrationOperationHandlerException>(recordedException);
        Assert.Equal(MySqlMigrationHandlerFailureCode.DuplicateOperationOwnership, exception.FailureCode);
        Assert.Equal("sample.first", exception.HandlerId);
        Assert.Equal(typeof(FirstOperation).FullName, exception.OperationType);
    }

    /// <summary>
    /// Verifies duplicate handler instance fails through the public handler id key.
    /// </summary>
    [Fact]
    public void Duplicate_handler_instance_fails_through_the_public_handler_id_key()
    {
        // Arrange
        var handler = new CountingHandler("sample.first", typeof(FirstOperation));
        Func<object?> act = () =>
            new MySqlMigrationOperationHandlerRegistry([handler, handler]);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlMigrationOperationHandlerException>(recordedException);
        Assert.Equal(MySqlMigrationHandlerFailureCode.DuplicateHandlerId, exception.FailureCode);
        Assert.Equal("sample.first", exception.HandlerId);
        Assert.Equal(typeof(FirstOperation).FullName, exception.OperationType);
        Assert.Equal(2, handler.HandlerIdReadCount);
        Assert.Equal(2, handler.OperationTypeReadCount);
    }

    /// <summary>
    /// Verifies null handler fails registration.
    /// </summary>
    [Fact]
    public void Null_handler_fails_registration()
    {
        // Arrange
        Func<object?> act = () =>
            new MySqlMigrationOperationHandlerRegistry([null!]);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlMigrationOperationHandlerException>(recordedException);
        Assert.Equal(MySqlMigrationHandlerFailureCode.InvalidRegistration, exception.FailureCode);
    }

    /// <summary>
    /// Verifies non concrete migration operation types fail registration.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidOperationTypes))]
    public void Non_concrete_migration_operation_types_fail_registration(
        Type operationType
    )
    {
        // Arrange
        Func<object?> act = () =>
            new MySqlMigrationOperationHandlerRegistry([new CountingHandler("sample.invalid", operationType)]);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlMigrationOperationHandlerException>(recordedException);
        Assert.Equal(MySqlMigrationHandlerFailureCode.InvalidRegistration, exception.FailureCode);
    }

    /// <summary>
    /// Verifies every ef core builtin operation is reserved.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReservedOperationCases))]
    public void Every_ef_core_builtin_operation_is_reserved(
        Type operationType
    )
    {
        // Arrange
        var handler = new CountingHandler("sample.reserved", operationType);

        // Act
        var recordedException = Record.Exception(() =>
            _ = new MySqlMigrationOperationHandlerRegistry([handler]));

        // Assert
        var exception = Assert.IsType<MySqlMigrationOperationHandlerException>(recordedException);
        Assert.Equal(MySqlMigrationHandlerFailureCode.ReservedOperationType, exception.FailureCode);
        Assert.Equal(operationType.FullName, exception.OperationType);
    }

    /// <summary>Supplies each exact EF Core operation type reserved by the provider.</summary>
    public static IEnumerable<object[]> ReservedOperationCases() => MySqlStandardMigrationOperations.Types
        .Select(type => new object[] { type });

    /// <summary>
    /// Verifies ef core dispatch and provider reserved operation sets agree.
    /// </summary>
    [Fact]
    public void Ef_core_dispatch_and_provider_reserved_operation_sets_agree()
    {
        // Arrange
        // This private field is intentionally inspected only in the test suite.
        // A supported EF Core patch that changes exact built-in dispatch must
        // fail qualification until the provider ownership set is reconciled.
        var generateActionsField = typeof(MigrationsSqlGenerator).GetField(
            "GenerateActions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Act
        var actual = generateActionsField?.GetValue(null);

        // Assert
        var generateActions = Assert.IsAssignableFrom<IDictionary>(actual);
        var efCoreOperationTypes = generateActions
            .Keys.Cast<Type>()
            .ToHashSet();

        Assert.True(
            efCoreOperationTypes.SetEquals(MySqlStandardMigrationOperations.Types),
            "The provider reserved-operation set drifted from EF Core's exact dispatch table.");
    }

    /// <summary>
    /// Verifies getter failure is wrapped without copying its message.
    /// </summary>
    [Fact]
    public void Getter_failure_is_wrapped_without_copying_its_message()
    {
        // Arrange
        var secret = "server=private;password=do-not-log";
        var source = new InvalidOperationException(secret);
        var handler = new ThrowingMetadataHandler(source);
        Func<object?> act = () =>
            new MySqlMigrationOperationHandlerRegistry([handler]);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlMigrationOperationHandlerException>(recordedException);
        Assert.Equal(MySqlMigrationHandlerFailureCode.InvalidRegistration, exception.FailureCode);
        Assert.Same(source, exception.InnerException);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    public static TheoryData<Type> InvalidOperationTypes =>
    [
        null!,
        typeof(string),
        typeof(MigrationOperation),
        typeof(AbstractOperation),
        typeof(OpenGenericOperation<>),
    ];

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
