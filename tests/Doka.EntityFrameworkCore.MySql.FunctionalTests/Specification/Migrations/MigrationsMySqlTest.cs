using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Migrations;

/// <summary>
/// Migrations-infrastructure specification subclass. Exercises the standard EF Core migration
/// runner (apply, generate-script, revert, idempotent-script) against the MySQL provider's
/// <see cref="MySqlHistoryRepository"/> + <c>__EFMigrationsHistory</c> table contract.
/// </summary>
[Trait("Category", "Spec")]
public class MigrationsMySqlTest : MigrationsInfrastructureTestBase<MigrationsMySqlTest.MigrationsMySqlFixture>
{
    // Structural-skip rationale for the four Can_diff_against_X_X(_Identity)_model overrides
    // below: each upstream test verifies that an EF-Core-X.Y-era ModelSnapshot diffs to zero
    // operations against the current model. The premise is that the provider existed during
    // that prior EF Core version and produced a real-world snapshot artifact. Doka's first
    // release is on EF Core 10; no prior-version Doka-MySQL snapshot exists in the wild, so
    // a hand-fabricated snapshot would only verify symmetry with the fabrication itself
    // (round-tripping our own assumptions). Listed under "Permanent skips" in SkipList.md
    // per ADR D-011 bucket 3 ("structural reason makes the upstream test inapplicable").
    private const string SnapshotInapplicableReason =
        "Doka first ships on EF Core 10; no prior-EF-Core-version model snapshots exist for this provider. "
        + "See ADR D-011 and SkipList.md.";

    public MigrationsMySqlTest(
        MigrationsMySqlFixture fixture
    ) : base(fixture)
    {
    }

    protected override async Task ExecuteSqlAsync(
        string value
    )
    {
        var connectionString = ((MySqlTestStore)Fixture.TestStore).ConnectionString;
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = value;
        command.CommandTimeout = MySqlTestStore.DefaultCommandTimeout;
        await command.ExecuteNonQueryAsync();
    }

    [Fact(Skip = SnapshotInapplicableReason)]
    public override void Can_diff_against_2_2_model()
    {
        // Skipped per attribute.
    }

    [Fact(Skip = SnapshotInapplicableReason)]
    public override void Can_diff_against_2_1_ASP_NET_Identity_model()
    {
        // Skipped per attribute.
    }

    [Fact(Skip = SnapshotInapplicableReason)]
    public override void Can_diff_against_2_2_ASP_NET_Identity_model()
    {
        // Skipped per attribute.
    }

    [Fact(Skip = SnapshotInapplicableReason)]
    public override void Can_diff_against_3_0_ASP_NET_Identity_model()
    {
        // Skipped per attribute.
    }

    public class MigrationsMySqlFixture : MigrationsInfrastructureFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}
