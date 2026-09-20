using System.Text.RegularExpressions;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Doka.EntityFrameworkCore.MySql.SpecificationAdapters.Update;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Update;

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
[SpecificationContractFacade(typeof(StoredProcedureUpdateTestBase))]
/// <summary>
/// Runs the relational stored-procedure update contract against MySQL-family
/// engines.
/// </summary>
public sealed class StoredProcedureUpdateMySqlTest
    : IClassFixture<NonSharedFixture>,
      IAsyncLifetime,
      IDisposable
{
    private readonly StoredProcedureUpdateMySqlExecutor _executor;

    public StoredProcedureUpdateMySqlTest(
        NonSharedFixture fixture
    )
    {
        _executor = new StoredProcedureUpdateMySqlExecutor(fixture);
    }

    public static TheoryData<bool> IsAsyncData => [false, true];

    public ValueTask InitializeAsync() => _executor.InitializeAsync();

    public ValueTask DisposeAsync() => _executor.DisposeAsync();

    public void Dispose() => _executor.Dispose();

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Insert_with_output_parameter(bool async) => _executor.Insert_with_output_parameter(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Insert_twice_with_output_parameter(bool async) => _executor.Insert_twice_with_output_parameter(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Insert_with_result_column(bool async) => _executor.Insert_with_result_column(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Insert_with_two_result_columns(bool async) => _executor.Insert_with_two_result_columns(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Insert_with_output_parameter_and_result_column(bool async) =>
        _executor.Insert_with_output_parameter_and_result_column(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Update(bool async) => _executor.Update(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Update_partial(bool async) => _executor.Update_partial(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Update_with_output_parameter_and_rows_affected_result_column(bool async) =>
        _executor.Update_with_output_parameter_and_rows_affected_result_column(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Update_with_output_parameter_and_rows_affected_result_column_concurrency_failure(bool async) =>
        _executor.Update_with_output_parameter_and_rows_affected_result_column_concurrency_failure(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Delete(bool async) => _executor.Delete(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Delete_and_insert(bool async) => _executor.Delete_and_insert(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Rows_affected_parameter(bool async) => _executor.Rows_affected_parameter(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Rows_affected_parameter_and_concurrency_failure(bool async) =>
        _executor.Rows_affected_parameter_and_concurrency_failure(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Rows_affected_result_column(bool async) => _executor.Rows_affected_result_column(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Rows_affected_result_column_and_concurrency_failure(bool async) =>
        _executor.Rows_affected_result_column_and_concurrency_failure(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Rows_affected_return_value(bool async) => _executor.Rows_affected_return_value(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Rows_affected_return_value_and_concurrency_failure(bool async) =>
        _executor.Rows_affected_return_value_and_concurrency_failure(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Store_generated_concurrency_token_as_in_out_parameter(bool async) =>
        _executor.Store_generated_concurrency_token_as_in_out_parameter(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Store_generated_concurrency_token_as_two_parameters(bool async) =>
        _executor.Store_generated_concurrency_token_as_two_parameters(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task User_managed_concurrency_token(bool async) => _executor.User_managed_concurrency_token(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Original_and_current_value_on_non_concurrency_token(bool async) =>
        _executor.Original_and_current_value_on_non_concurrency_token(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Input_or_output_parameter_with_input(bool async) =>
        _executor.Input_or_output_parameter_with_input(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Input_or_output_parameter_with_output(bool async) =>
        _executor.Input_or_output_parameter_with_output(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Tph(bool async) => _executor.Tph(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Tpt(bool async) => _executor.Tpt(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Tpt_mixed_sproc_and_non_sproc(bool async) => _executor.Tpt_mixed_sproc_and_non_sproc(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Tpc(bool async) => _executor.Tpc(async);

    [Theory]
    [MemberData(nameof(IsAsyncData))]
    public Task Non_sproc_followed_by_sproc_commands_in_the_same_batch(bool async) =>
        _executor.Non_sproc_followed_by_sproc_commands_in_the_same_batch(async);

    private sealed class StoredProcedureUpdateMySqlExecutor(
        NonSharedFixture fixture
    ) : StoredProcedureUpdateMySqlTestAdapter(fixture)
    {
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

            foreach (var batch in batches.Where(
                         static batch => !string.IsNullOrEmpty(batch)
                     ))
            {
                await context.Database.ExecuteSqlRawAsync(batch);
            }
        }

        protected override void ConfigureStoreGeneratedConcurrencyToken(
            EntityTypeBuilder entityTypeBuilder,
            string propertyName
        ) => entityTypeBuilder.Property<byte[]>(propertyName).IsRowVersion();

        protected override ITestStoreFactory NonSharedTestStoreFactory
            => MySqlTestStoreFactory.Instance;
    }
}
