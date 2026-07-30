using System.Text;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Update;

/// <summary>
/// Runs the official update-pipeline contract for entities with no, some, or
/// exclusively database-generated values. The inherited matrix covers single
/// and paired insert, update, and delete operations in synchronous and
/// asynchronous execution modes.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class StoreValueGenerationMySqlTest
    : StoreValueGenerationTestBase<
        StoreValueGenerationMySqlTest.StoreValueGenerationMySqlFixture>
{
    public StoreValueGenerationMySqlTest(
        StoreValueGenerationMySqlFixture fixture
    ) : base(fixture)
    {
    }

    protected override bool ShouldCreateImplicitTransaction(
        EntityState firstOperationType,
        EntityState? secondOperationType,
        GeneratedValues generatedValues,
        bool withSameEntityType
    )
    {
        var supportsReturning = MySqlTestEnvironment
            .ServerVersion
            .Profile
            .Supports(ProviderCapability.ReturningClause);

        if (supportsReturning)
        {
            // Same-shape inserts are emitted as one multi-row INSERT RETURNING
            // command on MariaDB, so no cross-command atomicity boundary exists.
            return generatedValues != GeneratedValues.None
                    && firstOperationType == EntityState.Modified
                || secondOperationType is not null
                    && !(firstOperationType == secondOperationType
                        && firstOperationType == EntityState.Added
                        && withSameEntityType);
        }

        return generatedValues != GeneratedValues.None
                && firstOperationType != EntityState.Deleted
            || secondOperationType is not null
                && !(firstOperationType == secondOperationType
                    && firstOperationType == EntityState.Added
                    && withSameEntityType);
    }

    protected override int ShouldExecuteInNumberOfCommands(
        EntityState firstOperationType,
        EntityState? secondOperationType,
        GeneratedValues generatedValues,
        bool withSameEntityType
    ) => 1;

    /// <summary>
    /// Uses TRUNCATE for deterministic identity values between inherited theory
    /// cases. The official model has no foreign-key edges between these tables,
    /// so each statement is independent on every supported engine.
    /// </summary>
    public sealed class StoreValueGenerationMySqlFixture :
        StoreValueGenerationFixtureBase
    {
        private string? _cleanDataSql;

        protected override ITestStoreFactory TestStoreFactory =>
            MySqlTestStoreFactory.Instance;

        public override void CleanData()
        {
            using var context = CreateContext();
            context.Database.ExecuteSqlRaw(_cleanDataSql ??= GetCleanDataSql(context));
        }

        private static string GetCleanDataSql(
            DbContext context
        )
        {
            var sql = new StringBuilder();
            var helper = context.GetService<ISqlGenerationHelper>();
            var tables = context.Model
                .GetEntityTypes()
                .SelectMany(entity => entity
                    .GetTableMappings()
                    .Select(mapping => helper.DelimitIdentifier(
                        mapping.Table.Name,
                        mapping.Table.Schema)))
                .Distinct(StringComparer.Ordinal);

            foreach (var table in tables)
            {
                sql
                    .Append("TRUNCATE TABLE ")
                    .Append(table)
                    .AppendLine(";");
            }

            return sql.ToString();
        }
    }
}
