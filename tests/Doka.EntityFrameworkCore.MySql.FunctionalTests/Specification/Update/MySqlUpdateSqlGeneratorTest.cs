using System.Text;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Update;

/// <summary>
/// Applies the official EF Core update-SQL-generator contract to the
/// provider's deterministic MySQL 8.4 SQL surface.
/// </summary>
[Trait("Category", "Spec")]
public sealed class MySqlUpdateSqlGeneratorTest : UpdateSqlGeneratorTestBase, IDisposable
{
    private readonly DbContext _context = MySqlTestHelpers.Instance.CreateContext();

    protected override TestHelpers TestHelpers => MySqlTestHelpers.Instance;

    protected override string RowsAffected => "ROW_COUNT()";

    protected override string Identity => "LAST_INSERT_ID()";

    protected override string OpenDelimiter => "`";

    protected override string CloseDelimiter => "`";

    protected override string? Schema => null;

    protected override IUpdateSqlGenerator CreateSqlGenerator()
        => _context.GetService<IUpdateSqlGenerator>();

    /// <summary>
    /// Verifies that relational schema input does not leak into a MySQL-family sequence
    /// identifier because the database itself is the provider's schema boundary.
    /// </summary>
    public override void GenerateNextSequenceValueOperation_correctly_handles_schemas()
    {
        var statement = CreateSqlGenerator()
            .GenerateNextSequenceValueOperation("mysequence", "dbo");

        Assert.Equal("SELECT NEXT VALUE FOR `mysequence`", statement);
    }

    protected override void AppendDeleteOperation_creates_full_delete_command_text_verification(
        StringBuilder stringBuilder
    ) => AssertSql(
        """
        DELETE FROM `Ducks`
        WHERE `Id` = @p0;
        SELECT ROW_COUNT();
        """,
        stringBuilder);

    protected override void AppendDeleteOperation_creates_full_delete_command_text_with_concurrency_check_verification(
        StringBuilder stringBuilder
    ) => AssertSql(
        """
        DELETE FROM `Ducks`
        WHERE `Id` = @p0 AND `ConcurrencyToken` IS NULL;
        SELECT ROW_COUNT();
        """,
        stringBuilder);

    protected override void AppendInsertOperation_insert_if_store_generated_columns_exist_verification(
        StringBuilder stringBuilder
    ) => AssertSql(
        """
        INSERT INTO `Ducks` (`Name`, `Quacks`, `ConcurrencyToken`)
        VALUES (@p0, @p1, @p2);
        SELECT `Id`, `Computed`
        FROM `Ducks`
        WHERE ROW_COUNT() = 1 AND `Id` = LAST_INSERT_ID();
        """,
        stringBuilder);

    protected override void AppendInsertOperation_for_store_generated_columns_but_no_identity_verification(
        StringBuilder stringBuilder
    ) => AssertSql(
        """
        INSERT INTO `Ducks` (`Id`, `Name`, `Quacks`, `ConcurrencyToken`)
        VALUES (@p0, @p1, @p2, @p3);
        SELECT `Computed`
        FROM `Ducks`
        WHERE ROW_COUNT() = 1 AND `Id` = @p0;
        """,
        stringBuilder);

    protected override void AppendInsertOperation_for_only_identity_verification(
        StringBuilder stringBuilder
    ) => AssertSql(
        """
        INSERT INTO `Ducks` (`Name`, `Quacks`, `ConcurrencyToken`)
        VALUES (@p0, @p1, @p2);
        SELECT `Id`
        FROM `Ducks`
        WHERE ROW_COUNT() = 1 AND `Id` = LAST_INSERT_ID();
        """,
        stringBuilder);

    protected override void AppendInsertOperation_for_all_store_generated_columns_verification(
        StringBuilder stringBuilder
    ) => AssertSql(
        """
        INSERT INTO `Ducks` ()
        VALUES ();
        SELECT `Id`, `Computed`
        FROM `Ducks`
        WHERE ROW_COUNT() = 1 AND `Id` = LAST_INSERT_ID();
        """,
        stringBuilder);

    protected override void AppendInsertOperation_for_only_single_identity_columns_verification(
        StringBuilder stringBuilder
    ) => AssertSql(
        """
        INSERT INTO `Ducks` ()
        VALUES ();
        SELECT `Id`
        FROM `Ducks`
        WHERE ROW_COUNT() = 1 AND `Id` = LAST_INSERT_ID();
        """,
        stringBuilder);

    protected override void AppendUpdateOperation_if_store_generated_columns_exist_verification(
        StringBuilder stringBuilder
    ) => AssertSql(
        """
        UPDATE `Ducks` SET `Name` = @p0, `Quacks` = @p1, `ConcurrencyToken` = @p2
        WHERE `Id` = @p3 AND `ConcurrencyToken` IS NULL;
        SELECT `Computed`
        FROM `Ducks`
        WHERE ROW_COUNT() = 1 AND `Id` = @p3;
        """,
        stringBuilder);

    protected override void AppendUpdateOperation_if_store_generated_columns_dont_exist_verification(
        StringBuilder stringBuilder
    ) => AssertSql(
        """
        UPDATE `Ducks` SET `Name` = @p0, `Quacks` = @p1, `ConcurrencyToken` = @p2
        WHERE `Id` = @p3;
        SELECT ROW_COUNT();
        """,
        stringBuilder);

    protected override void AppendUpdateOperation_appends_where_for_concurrency_token_verification(
        StringBuilder stringBuilder
    ) => AssertSql(
        """
        UPDATE `Ducks` SET `Name` = @p0, `Quacks` = @p1, `ConcurrencyToken` = @p2
        WHERE `Id` = @p3 AND `ConcurrencyToken` IS NULL;
        SELECT ROW_COUNT();
        """,
        stringBuilder);

    protected override void AppendUpdateOperation_for_computed_property_verification(
        StringBuilder stringBuilder
    ) => AssertSql(
        """
        UPDATE `Ducks` SET `Name` = @p0, `Quacks` = @p1, `ConcurrencyToken` = @p2
        WHERE `Id` = @p3;
        SELECT `Computed`
        FROM `Ducks`
        WHERE ROW_COUNT() = 1 AND `Id` = @p3;
        """,
        stringBuilder);

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();
    }

    private static void AssertSql(
        string expected,
        StringBuilder actual
    ) => Assert.Equal(
        expected.TrimEnd(),
        actual.ToString().TrimEnd(),
        ignoreLineEndingDifferences: true);
}
