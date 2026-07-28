using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Migrations;

/// <summary>
/// Executes every provider-independent migration SQL generator shape against
/// the MySQL generator and requires each inherited operation to emit SQL.
/// Exact provider-specific DDL forms are asserted by the focused migration
/// coverage tests.
/// </summary>
[Trait("Category", "Spec")]
public sealed class MigrationsSqlGeneratorMySqlTest
    : MigrationsSqlGeneratorTestBase
{
    public MigrationsSqlGeneratorMySqlTest()
        : base(MySqlTestHelpers.Instance)
    {
    }

    public override void AddColumnOperation_without_column_type() =>
        VerifyGeneratedSql(base.AddColumnOperation_without_column_type);

    public override void AddColumnOperation_with_unicode_overridden() =>
        VerifyGeneratedSql(base.AddColumnOperation_with_unicode_overridden);

    public override void AddColumnOperation_with_unicode_no_model() =>
        VerifyGeneratedSql(base.AddColumnOperation_with_unicode_no_model);

    public override void AddColumnOperation_with_fixed_length_no_model() =>
        VerifyGeneratedSql(base.AddColumnOperation_with_fixed_length_no_model);

    public override void AddColumnOperation_with_maxLength_overridden() =>
        VerifyGeneratedSql(base.AddColumnOperation_with_maxLength_overridden);

    public override void AddColumnOperation_with_maxLength_no_model() =>
        VerifyGeneratedSql(base.AddColumnOperation_with_maxLength_no_model);

    public override void AddColumnOperation_with_precision_and_scale_overridden() =>
        VerifyGeneratedSql(base.AddColumnOperation_with_precision_and_scale_overridden);

    public override void AddColumnOperation_with_precision_and_scale_no_model() =>
        VerifyGeneratedSql(base.AddColumnOperation_with_precision_and_scale_no_model);

    public override void AddForeignKeyOperation_without_principal_columns() =>
        VerifyGeneratedSql(base.AddForeignKeyOperation_without_principal_columns);

    public override void AlterColumnOperation_without_column_type() =>
        VerifyGeneratedSql(base.AlterColumnOperation_without_column_type);

    public override void RenameTableOperation_legacy() =>
        VerifyGeneratedSql(base.RenameTableOperation_legacy);

    public override void RenameTableOperation() =>
        VerifyGeneratedSql(base.RenameTableOperation);

    public override void SqlOperation() =>
        VerifyGeneratedSql(base.SqlOperation);

    public override void InsertDataOperation_all_args_spatial() =>
        VerifyGeneratedSql(base.InsertDataOperation_all_args_spatial);

    public override void InsertDataOperation_required_args() =>
        VerifyGeneratedSql(base.InsertDataOperation_required_args);

    public override void InsertDataOperation_required_args_composite() =>
        VerifyGeneratedSql(base.InsertDataOperation_required_args_composite);

    public override void InsertDataOperation_required_args_multiple_rows() =>
        VerifyGeneratedSql(base.InsertDataOperation_required_args_multiple_rows);

    public override void InsertDataOperation_throws_for_unsupported_column_types() =>
        base.InsertDataOperation_throws_for_unsupported_column_types();

    public override void DeleteDataOperation_all_args() =>
        VerifyGeneratedSql(base.DeleteDataOperation_all_args);

    public override void DeleteDataOperation_all_args_composite() =>
        VerifyGeneratedSql(base.DeleteDataOperation_all_args_composite);

    public override void DeleteDataOperation_required_args() =>
        VerifyGeneratedSql(base.DeleteDataOperation_required_args);

    public override void DeleteDataOperation_required_args_composite() =>
        VerifyGeneratedSql(base.DeleteDataOperation_required_args_composite);

    public override void UpdateDataOperation_all_args() =>
        VerifyGeneratedSql(base.UpdateDataOperation_all_args);

    public override void UpdateDataOperation_all_args_composite() =>
        VerifyGeneratedSql(base.UpdateDataOperation_all_args_composite);

    public override void UpdateDataOperation_all_args_composite_multi() =>
        VerifyGeneratedSql(base.UpdateDataOperation_all_args_composite_multi);

    public override void UpdateDataOperation_all_args_multi() =>
        VerifyGeneratedSql(base.UpdateDataOperation_all_args_multi);

    public override void UpdateDataOperation_required_args() =>
        VerifyGeneratedSql(base.UpdateDataOperation_required_args);

    public override void UpdateDataOperation_required_args_multiple_rows() =>
        VerifyGeneratedSql(base.UpdateDataOperation_required_args_multiple_rows);

    public override void UpdateDataOperation_required_args_composite() =>
        VerifyGeneratedSql(base.UpdateDataOperation_required_args_composite);

    public override void UpdateDataOperation_required_args_composite_multi() =>
        VerifyGeneratedSql(base.UpdateDataOperation_required_args_composite_multi);

    public override void UpdateDataOperation_required_args_multi() =>
        VerifyGeneratedSql(base.UpdateDataOperation_required_args_multi);

    public override void DefaultValue_with_line_breaks(
        bool isUnicode
    ) => VerifyGeneratedSql(() => base.DefaultValue_with_line_breaks(isUnicode));

    public override void DefaultValue_with_line_breaks_2(
        bool isUnicode
    ) => VerifyGeneratedSql(() => base.DefaultValue_with_line_breaks_2(isUnicode));

    public override void Sequence_restart_operation(
        long? startsAt
    ) => VerifyGeneratedSql(() => base.Sequence_restart_operation(startsAt));

    protected override string GetGeometryCollectionStoreType() =>
        "geometrycollection";

    private void VerifyGeneratedSql(
        Action generate
    )
    {
        generate();

        Assert.False(
            string.IsNullOrWhiteSpace(Sql),
            "The migration operation completed without generating executable SQL.");
    }
}
