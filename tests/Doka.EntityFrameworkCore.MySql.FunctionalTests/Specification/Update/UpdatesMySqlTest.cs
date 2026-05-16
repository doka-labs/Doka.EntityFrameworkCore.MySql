using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Update;

/// <summary>
/// Updates specification subclass. Exercises the modification-command-batch pipeline end-to-end
/// against the MySQL provider's <see cref="MySqlModificationCommandBatch"/> implementation: insert
/// + update + delete shapes, store-generated keys, concurrency tokens, and multi-statement
/// batching at the MySQL <c>max_allowed_packet</c> + 65535-placeholder cap.
/// </summary>
[Trait("Category", "Spec")]
public class UpdatesMySqlTest : UpdatesRelationalTestBase<UpdatesMySqlTest.UpdatesMySqlFixture>
{
    public UpdatesMySqlTest(
        UpdatesMySqlFixture fixture
    ) : base(fixture)
    {
    }

    // The upstream test asserts that the spec's deliberately-long entity-type name flows through
    // the provider's identifier pipeline UNTRUNCATED into table / key / constraint / index names.
    // Doka's MySqlModelValidator.ValidateConstraintNameLengths rejects any FK or index name above
    // MySQL's 64-character limit at model-build time rather than silently truncating; the design
    // choice favors explicit error over silent name collision. The upstream assertion shape does
    // not apply: Doka throws at CreateContext() before the assertion runs. Listed under
    // "Permanent skips" in SkipList.md per ADR D-011 bucket 3.
    [Fact(Skip =
        "Doka rejects identifiers above the MySQL 64-character limit at model-build time; "
        + "the upstream assertion assumes silent truncation, which is not the provider's design. "
        + "See ADR D-011 and SkipList.md.")]
    public override void Identifiers_are_generated_correctly()
    {
        // Skipped per attribute.
    }

    public class UpdatesMySqlFixture : UpdatesRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}
