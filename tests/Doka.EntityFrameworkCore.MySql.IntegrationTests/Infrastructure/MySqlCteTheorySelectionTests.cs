namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Checks capability-specific CTE theory discovery for isolated and combined engine selections without database I/O.</summary>
public sealed class MySqlCteTheorySelectionTests
{
    /// <summary>Eligible selected engines keep native cases; unselected eligibility is explicit rather than empty-data suppression.</summary>
    [Theory]
    [InlineData(
        nameof(MySqlCteGraphIntegrationTests.Constraint_failure_preserves_native_error_and_no_partial_cte_update),
        nameof(MySqlCteGraphIntegrationTests.SupportedMutationTargets),
        false)]
    [InlineData(nameof(MySqlCteGraphIntegrationTests.Native_trigger_runs_once_per_affected_physical_row),
        nameof(MySqlCteGraphIntegrationTests.SupportedMutationTargets),
        false)]
    [InlineData(
        nameof(MySqlCteGraphIntegrationTests
            .Failure_after_native_completion_is_preserved_without_replay_or_rollback_claim),
        nameof(MySqlCteGraphIntegrationTests.SupportedMutationTargets),
        false)]
    [InlineData(
        nameof(MySqlCteGraphIntegrationTests
            .Native_cte_deadlock_preserves_the_engine_abort_and_rolls_back_prior_caller_work),
        nameof(MySqlCteGraphIntegrationTests.SupportedMutationTargets),
        false)]
    [InlineData(
        nameof(MySqlCteGraphIntegrationTests.Unsafe_self_referencing_cte_write_is_rejected_before_native_execution),
        nameof(MySqlCteGraphIntegrationTests.IsolatedMutationTargets),
        true)]
    public void Capability_theory_eligibility_matches_its_data_and_the_current_selection(
        string methodName,
        string dataMemberName,
        bool requiresTargetIsolation
    )
    {
        // Arrange
        var type = typeof(MySqlCteGraphIntegrationTests);
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("The capability-specific CTE test method is missing.");

        var eligible = IntegrationTestEnvironment
            .GetSupportedTargets()
            .Where(target =>
            {
                var profile = IntegrationTestEnvironment.GetServerVersion(target)
                    .Profile;

                return profile.Engine.Has(EngineCapability.CteUpdateDelete)
                    && (!requiresTargetIsolation
                        || profile.GetSupport(ProviderCapability.SelfReferencingMutations)
                        == ProviderSupportStatus.Emulated);
            })
            .Order()
            .ToArray();

        var selected = IntegrationTestEnvironment
            .GetSelectedTargets()
            .Where(eligible.Contains)
            .Order()
            .ToArray();

        // Act
        var theory = method.GetCustomAttribute<TheoryAttribute>();
        var data = method.GetCustomAttribute<MemberDataAttribute>();
        var declaration = method
            .GetCustomAttributesData()
            .SingleOrDefault(attribute => attribute.AttributeType == typeof(RequiresDatabaseTargetsTheoryAttribute));

        var declaredArguments =
            declaration?.ConstructorArguments[0].Value as IReadOnlyCollection<CustomAttributeTypedArgument>;

        var declared = declaredArguments
            ?.Select(argument
                => (IntegrationDatabaseTarget)Convert.ToInt32(argument.Value, CultureInfo.InvariantCulture))
            .Order()
            .ToArray();

        // WHY: Inspect the actual typed xUnit rows instead of inferring eligibility from the attribute alone.
        var rows = (IEnumerable<TheoryDataRow<string>>)(type
                .GetProperty(dataMemberName, BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null)
            ?? throw new InvalidOperationException("The capability-specific CTE data member is missing."));

        var actual = rows
            .Select(row => Enum.Parse<IntegrationDatabaseTarget>(row.Data))
            .Order()
            .ToArray();

        // Assert
        var attribute = Assert.IsType<RequiresDatabaseTargetsTheoryAttribute>(theory);
        Assert.False(attribute.SkipTestWithoutData);
        Assert.Equal(dataMemberName,
            Assert.IsType<MemberDataAttribute>(data)
                .MemberName);

        Assert.Equal(eligible, declared);
        Assert.Equal(selected, actual);
        Assert.Equal(selected.Length == 0
                ? IntegrationTestEnvironment.GetTargetSelectionSkipReason(eligible)
                : null,
            attribute.Skip);
    }
}
