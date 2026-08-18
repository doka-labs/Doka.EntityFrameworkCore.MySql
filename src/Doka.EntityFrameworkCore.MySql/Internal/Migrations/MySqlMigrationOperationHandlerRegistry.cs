namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Validates handler registrations once per scoped provider graph and exposes
/// constant-time exact-type dispatch.
/// </summary>
internal sealed class MySqlMigrationOperationHandlerRegistry
{
    private static readonly Regex s_handlerIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9_-]*(\\.[A-Za-z0-9][A-Za-z0-9_-]*)+$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private readonly FrozenDictionary<Type, Registration> _registrations;

    public MySqlMigrationOperationHandlerRegistry(
        IEnumerable<IMySqlMigrationOperationHandler> handlers
    )
    {
        ArgumentNullException.ThrowIfNull(handlers);

        var candidates = new List<Registration>();

        foreach (var handler in handlers)
        {
            if (handler is null)
            {
                throw CreateRegistrationException(
                    MySqlMigrationHandlerFailureCode.InvalidRegistration,
                    null,
                    typeof(MigrationOperation));
            }

            string? handlerId;
            Type? operationType;

            try
            {
                // A handler may implement computed getters. Reading each value
                // once makes validation and dispatch observe one stable snapshot.
                handlerId = handler.HandlerId;
                operationType = handler.OperationType;
            }
            catch (Exception exception)
            {
                throw CreateRegistrationException(
                    MySqlMigrationHandlerFailureCode.InvalidRegistration,
                    null,
                    typeof(MigrationOperation),
                    exception);
            }

            if (!IsValidHandlerId(handlerId))
            {
                throw CreateRegistrationException(
                    MySqlMigrationHandlerFailureCode.InvalidRegistration,
                    null,
                    operationType ?? typeof(MigrationOperation));
            }

            if (!IsConcreteOperationType(operationType))
            {
                throw CreateRegistrationException(
                    MySqlMigrationHandlerFailureCode.InvalidRegistration,
                    handlerId,
                    operationType ?? typeof(MigrationOperation));
            }

            if (MySqlStandardMigrationOperations.Contains(operationType!))
            {
                throw CreateRegistrationException(
                    MySqlMigrationHandlerFailureCode.ReservedOperationType,
                    handlerId,
                    operationType!);
            }

            candidates.Add(new Registration(handler, handlerId!, operationType!));
        }

        // Canonical ordering makes conflict metadata independent of service
        // registration order. Handler identity is deliberately not a key: the
        // public contract is owned by HandlerId and exact operation type.
        var orderedCandidates = candidates
            .OrderBy(candidate => candidate.HandlerId, StringComparer.Ordinal)
            .ThenBy(candidate => GetStableTypeName(candidate.OperationType), StringComparer.Ordinal)
            .ToArray();

        var handlerIds = new Dictionary<string, Registration>(StringComparer.Ordinal);

        foreach (var candidate in orderedCandidates)
        {
            if (!handlerIds.TryAdd(candidate.HandlerId, candidate))
            {
                var owner = handlerIds[candidate.HandlerId];
                throw CreateRegistrationException(
                    MySqlMigrationHandlerFailureCode.DuplicateHandlerId,
                    owner.HandlerId,
                    owner.OperationType);
            }
        }

        var registrations = new Dictionary<Type, Registration>();

        foreach (var candidate in orderedCandidates)
        {
            if (!registrations.TryAdd(candidate.OperationType, candidate))
            {
                var owner = registrations[candidate.OperationType];
                throw CreateRegistrationException(
                    MySqlMigrationHandlerFailureCode.DuplicateOperationOwnership,
                    owner.HandlerId,
                    owner.OperationType);
            }
        }

        _registrations = registrations.ToFrozenDictionary();
    }

    public bool TryGet(
        Type operationType,
        out Registration registration
    ) => _registrations.TryGetValue(operationType, out registration!);

    private static bool IsValidHandlerId(
        string? handlerId
    ) => handlerId is { Length: >= 1 and <= 200 } && s_handlerIdPattern.IsMatch(handlerId);

    private static bool IsConcreteOperationType(
        Type? operationType
    ) => operationType is not null
        && operationType.IsClass
        && operationType is { IsAbstract: false, ContainsGenericParameters: false }
        && typeof(MigrationOperation).IsAssignableFrom(operationType);

    private static string GetStableTypeName(
        Type operationType
    ) => operationType.AssemblyQualifiedName ?? operationType.FullName ?? operationType.Name;

    private static MySqlMigrationOperationHandlerException CreateRegistrationException(
        MySqlMigrationHandlerFailureCode failureCode,
        string? handlerId,
        Type operationType,
        Exception? innerException = null
    ) => new(failureCode, handlerId, operationType, MigrationsSqlGenerationOptions.Default, -1, innerException);

    internal sealed record Registration(
        IMySqlMigrationOperationHandler Handler,
        string HandlerId,
        Type OperationType
    );
}
