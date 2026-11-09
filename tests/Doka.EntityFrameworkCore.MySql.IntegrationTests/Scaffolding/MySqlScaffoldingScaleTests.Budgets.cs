using Xunit.Sdk;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies resource budgets independently of host speed and load.</summary>
public sealed partial class MySqlScaffoldingScaleTests
{
    // WHY: A separate nested test class can exercise the private evaluator without
    // starting the outer class's live-database fixture.
    /// <summary>Verifies synthetic budget evidence without database infrastructure.</summary>
    public sealed class BudgetTests
    {
        /// <summary>Accepts resource-boundary evidence regardless of its diagnostic elapsed time.</summary>
        [Fact]
        public void Elapsed_time_does_not_change_a_passing_resource_verdict()
        {
            // Arrange
            var budgets = LoadPolicy()
                .Budgets;

            var metrics = new ScaffoldingRunMetrics(
                TimeSpan.MaxValue,
                budgets.MaximumAllocatedBytesPerRun,
                budgets.MaximumManagedHeapGrowthBytesPerRun,
                0,
                "unused");

            // Act
            var exception = Record.Exception(() =>
                AssertMeasuredBudgets(metrics, budgets, IntegrationDatabaseTarget.MariaDb123, 0));

            // Assert
            Assert.Null(exception);
        }

        /// <summary>Rejects each resource overrun even when elapsed time is only diagnostic.</summary>
        /// <param name="allocationOverrun">Whether allocation rather than managed-heap growth exceeds its budget.</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Resource_budget_overrun_is_rejected(
            bool allocationOverrun
        )
        {
            // Arrange
            var budgets = LoadPolicy()
                .Budgets;

            var metrics = new ScaffoldingRunMetrics(
                TimeSpan.Zero,
                budgets.MaximumAllocatedBytesPerRun
                + (allocationOverrun
                    ? 1
                    : 0),
                budgets.MaximumManagedHeapGrowthBytesPerRun
                + (allocationOverrun
                    ? 0
                    : 1),
                0,
                "unused");

            // Act
            var exception = Record.Exception(() =>
                AssertMeasuredBudgets(metrics, budgets, IntegrationDatabaseTarget.MariaDb123, 0));

            // Assert
            var failure = Assert.IsType<TrueException>(exception);
            Assert.Contains(allocationOverrun
                    ? "allocated"
                    : "grew the managed heap",
                failure.Message);
        }
    }
}
