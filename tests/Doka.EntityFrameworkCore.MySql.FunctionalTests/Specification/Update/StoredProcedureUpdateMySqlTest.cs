using Doka.EntityFrameworkCore.MySql.SpecificationAdapters.Update;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Update;

/// <summary>
/// Runs the relational stored-procedure update contract against MySQL-family
/// engines.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class StoredProcedureUpdateMySqlTest
    : StoredProcedureUpdateMySqlTestAdapter
{
    public StoredProcedureUpdateMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    protected override async Task CreateStoredProcedures(
        DbContext context,
        string createSprocSql
    )
    {
        var batches = new Regex(
            @"[\r\n\s]*(?:\r|\n)GO;?[\r\n\s]*",
            RegexOptions.IgnoreCase | RegexOptions.Singleline,
            TimeSpan.FromSeconds(1)
        ).Split(createSprocSql);

        foreach (var batch in batches.Where(static batch => !string.IsNullOrEmpty(batch)
            ))
        {
            await context.Database.ExecuteSqlRawAsync(batch);
        }
    }

    protected override void ConfigureStoreGeneratedConcurrencyToken(
        EntityTypeBuilder entityTypeBuilder,
        string propertyName
    ) => entityTypeBuilder
        .Property<byte[]>(propertyName)
        .IsRowVersion();
}
