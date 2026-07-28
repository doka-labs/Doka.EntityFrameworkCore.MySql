using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.MySql.SpecificationAdapters.Update;

/// <summary>
/// Adapts the relational stored-procedure update contract to MySQL syntax and
/// result propagation.
/// </summary>
public abstract class StoredProcedureUpdateMySqlTestAdapter : StoredProcedureUpdateTestBase
{
    protected StoredProcedureUpdateMySqlTestAdapter(
        NonSharedFixture fixture
    ) : base(fixture)
    {
    }

    public override async Task Insert_with_output_parameter(bool async)
    {
        await Insert_with_output_parameter(
            async,
"""
CREATE PROCEDURE Entity_Insert(pName varchar(255), OUT pId int)
BEGIN
    INSERT INTO `Entity` (`Name`) VALUES (pName);
    SET pId = LAST_INSERT_ID();
END
""");

        AssertSql(
"""
@p1='New'

SET @_out_p0 = NULL;
CALL `Entity_Insert`(@p1, @_out_p0);
SELECT @_out_p0;
""");
    }

    public override async Task Insert_twice_with_output_parameter(bool async)
    {
        await Insert_twice_with_output_parameter(
            async,
"""
CREATE PROCEDURE Entity_Insert(pName varchar(255), OUT pId int)
BEGIN
    INSERT INTO `Entity` (`Name`) VALUES (pName);
    SET pId = LAST_INSERT_ID();
END
""");

        AssertSql(
"""
@p1='New1'
@p3='New2'

SET @_out_p0 = NULL;
CALL `Entity_Insert`(@p1, @_out_p0);
SELECT @_out_p0;
SET @_out_p2 = NULL;
CALL `Entity_Insert`(@p3, @_out_p2);
SELECT @_out_p2;
""");
    }

    public override async Task Insert_with_result_column(bool async)
    {
        await Insert_with_result_column(
            async,
            """
            CREATE PROCEDURE Entity_Insert(pName varchar(255))
            BEGIN
                INSERT INTO `Entity` (`Name`) VALUES (pName);
                SELECT LAST_INSERT_ID() AS `Id`;
            END
            """);
    }

    public override async Task Insert_with_two_result_columns(bool async)
    {
        await Insert_with_two_result_columns(
            async,
            """
            CREATE PROCEDURE EntityWithAdditionalProperty_Insert(pName varchar(255))
            BEGIN
                INSERT INTO `EntityWithAdditionalProperty` (`Name`) VALUES (pName);
                SELECT 8 AS `AdditionalProperty`, LAST_INSERT_ID() AS `Id`;
            END
            """);
    }

    public override async Task Insert_with_output_parameter_and_result_column(bool async)
    {
        await Insert_with_output_parameter_and_result_column(
            async,
            """
            CREATE PROCEDURE EntityWithAdditionalProperty_Insert(
                OUT pId int,
                pName varchar(255))
            BEGIN
                INSERT INTO `EntityWithAdditionalProperty` (`Name`) VALUES (pName);
                SET pId = LAST_INSERT_ID();
                SELECT 8 AS `AdditionalProperty`;
            END
            """);
    }

    public override async Task Update(bool async)
    {
        await Update(
            async,
"""
CREATE PROCEDURE Entity_Update(pId int, pName varchar(255))
UPDATE `Entity` SET `Name` = pName WHERE `Id` = pid
""");

        AssertSql(
"""
@p0='1'
@p1='Updated'

CALL `Entity_Update`(@p0, @p1);
""");
    }

    public override async Task Update_partial(bool async)
    {
        await Update_partial(
            async,
"""
CREATE PROCEDURE EntityWithAdditionalProperty_Update(pId int, pName varchar(255), pAdditionalProperty int)
UPDATE `EntityWithAdditionalProperty` SET `Name` = pName, `AdditionalProperty` = pAdditionalProperty WHERE `Id` = pid
""");

        AssertSql(
"""
@p0='1'
@p1='Updated'
@p2='8'

CALL `EntityWithAdditionalProperty_Update`(@p0, @p1, @p2);
""");
    }

    public override async Task Update_with_output_parameter_and_rows_affected_result_column(bool async)
    {
        await Update_with_output_parameter_and_rows_affected_result_column(
            async,
            """
            CREATE PROCEDURE EntityWithAdditionalProperty_Update(
                pId int,
                pName varchar(255),
                OUT pAdditionalProperty int)
            BEGIN
                UPDATE `EntityWithAdditionalProperty`
                SET `Name` = pName
                WHERE `Id` = pId;
                SET pAdditionalProperty = 8;
                SELECT ROW_COUNT() AS `RowsAffected`;
            END
            """);
    }

    public override async Task
        Update_with_output_parameter_and_rows_affected_result_column_concurrency_failure(
            bool async)
    {
        await Update_with_output_parameter_and_rows_affected_result_column_concurrency_failure(
            async,
            """
            CREATE PROCEDURE EntityWithAdditionalProperty_Update(
                pId int,
                pName varchar(255),
                OUT pAdditionalProperty int)
            BEGIN
                UPDATE `EntityWithAdditionalProperty`
                SET `Name` = pName
                WHERE `Id` = pId;
                SET pAdditionalProperty = 8;
                SELECT ROW_COUNT() AS `RowsAffected`;
            END
            """);
    }

    public override async Task Delete(bool async)
    {
        await Delete(
            async,
"""
CREATE PROCEDURE Entity_Delete(pId int)
DELETE FROM `Entity` WHERE `Id` = pId
""");

        AssertSql(
"""
@p0='1'

CALL `Entity_Delete`(@p0);
""");
    }

    public override async Task Delete_and_insert(bool async)
    {
        await Delete_and_insert(
            async,
"""
CREATE PROCEDURE Entity_Insert(pName varchar(255), OUT pId int)
BEGIN
    INSERT INTO `Entity` (`Name`) VALUES (pName);
    SET pId = LAST_INSERT_ID();
END;

GO;

CREATE PROCEDURE Entity_Delete(pId int)
DELETE FROM `Entity` WHERE `Id` = pId;
""");

        AssertSql(
"""
@p0='1'
@p2='Entity2'

CALL `Entity_Delete`(@p0);
SET @_out_p1 = NULL;
CALL `Entity_Insert`(@p2, @_out_p1);
SELECT @_out_p1;
""");
    }

    public override async Task Rows_affected_parameter(bool async)
    {
        await Rows_affected_parameter(
            async,
"""
CREATE PROCEDURE Entity_Update(pId int, pName varchar(255), OUT pRowsAffected int)
BEGIN
    UPDATE `Entity` SET `Name` = pName WHERE `Id` = pId;
    SET pRowsAffected = ROW_COUNT();
END
""");

        AssertSql(
"""
@p1='1'
@p2='Updated'

SET @_out_p0 = NULL;
CALL `Entity_Update`(@p1, @p2, @_out_p0);
SELECT @_out_p0;
""");
    }

    public override async Task Rows_affected_parameter_and_concurrency_failure(bool async)
    {
        await Rows_affected_parameter_and_concurrency_failure(
            async,
"""
CREATE PROCEDURE Entity_Update(pId int, pName varchar(255), OUT pRowsAffected int)
BEGIN
    UPDATE `Entity` SET `Name` = pName WHERE `Id` = pId;
    SET pRowsAffected = ROW_COUNT();
END
""");

        AssertSql(
"""
@p1='1'
@p2='Updated'

SET @_out_p0 = NULL;
CALL `Entity_Update`(@p1, @p2, @_out_p0);
SELECT @_out_p0;
""");
    }

    public override async Task Rows_affected_result_column(bool async)
    {
        await Rows_affected_result_column(
            async,
            """
            CREATE PROCEDURE Entity_Update(pId int, pName varchar(255))
            BEGIN
                UPDATE `Entity` SET `Name` = pName WHERE `Id` = pId;
                SELECT ROW_COUNT() AS `RowsAffected`;
            END
            """);
    }

    public override async Task Rows_affected_result_column_and_concurrency_failure(bool async)
    {
        await Rows_affected_result_column_and_concurrency_failure(
            async,
            """
            CREATE PROCEDURE Entity_Update(pId int, pName varchar(255))
            BEGIN
                UPDATE `Entity` SET `Name` = pName WHERE `Id` = pId;
                SELECT ROW_COUNT() AS `RowsAffected`;
            END
            """);
    }

    public override async Task Rows_affected_return_value(bool async)
    {
        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Rows_affected_return_value(async, createSprocSql: ""));

        Assert.Equal(
            "MySQL-family stored procedures do not expose a return-value "
            + "channel compatible with EF Core's stored-procedure mapping.",
            exception.Message);
    }

    public override async Task Rows_affected_return_value_and_concurrency_failure(bool async)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Rows_affected_return_value(
                async,
                createSprocSql: ""));

        Assert.Equal(
            "MySQL-family stored procedures do not expose a return-value "
            + "channel compatible with EF Core's stored-procedure mapping.",
            exception.Message);
    }

    public override async Task Store_generated_concurrency_token_as_in_out_parameter(bool async)
    {
        await Store_generated_concurrency_token_as_in_out_parameter(
            async,
"""
CREATE PROCEDURE Entity_Update(pId int, INOUT pConcurrencyToken timestamp(6), pName varchar(255), OUT pRowsAffected int)
BEGIN
    UPDATE `Entity` SET `Name` = pName WHERE `Id` = pId AND `ConcurrencyToken` = pConcurrencyToken;
    SET pRowsAffected = ROW_COUNT();
    SELECT `ConcurrencyToken` INTO pConcurrencyToken FROM `Entity` WHERE `Id` = pId;
END
""");

        Assert.StartsWith(
            """
@p2='1'
@p0='
""",
            TestSqlLoggerFactory.Sql);

        Assert.EndsWith(
"""
' (Nullable = true) (DbType = DateTime)
@p3='Updated'

SET @_out_p0 = @p0;
SET @_out_p1 = NULL;
CALL `Entity_Update`(@p2, @_out_p0, @p3, @_out_p1);
SELECT @_out_p0, @_out_p1;
""",
            TestSqlLoggerFactory.Sql);

    }

    public override async Task Store_generated_concurrency_token_as_two_parameters(bool async)
    {
        await Store_generated_concurrency_token_as_two_parameters(
            async,
"""
CREATE PROCEDURE Entity_Update(
    pId int,
    pConcurrencyTokenIn timestamp(6),
    pName varchar(255),
    OUT pConcurrencyTokenOut timestamp(6),
    OUT pRowsAffected int)
BEGIN
    UPDATE `Entity`
    SET `Name` = pName
    WHERE `Id` = pId
        AND `ConcurrencyToken` = pConcurrencyTokenIn;
    SET pRowsAffected = ROW_COUNT();
    SELECT `ConcurrencyToken`
    INTO pConcurrencyTokenOut
    FROM `Entity`
    WHERE `Id` = pId;
END
""");

        Assert.StartsWith(
"""
@p2='1'
@p3='
""",
            TestSqlLoggerFactory.Sql);

        Assert.EndsWith(
            """
' (Nullable = true) (DbType = DateTime)
@p4='Updated'

SET @_out_p0 = NULL;
SET @_out_p1 = NULL;
CALL `Entity_Update`(@p2, @p3, @p4, @_out_p0, @_out_p1);
SELECT @_out_p0, @_out_p1;
""",
            TestSqlLoggerFactory.Sql);

    }

    public override async Task User_managed_concurrency_token(bool async)
    {
        await User_managed_concurrency_token(
            async,
"""
CREATE PROCEDURE EntityWithAdditionalProperty_Update(
    pId int,
    pConcurrencyTokenOriginal int,
    pName varchar(255),
    pConcurrencyTokenCurrent int,
    OUT pRowsAffected int)
BEGIN
    UPDATE `EntityWithAdditionalProperty`
    SET `Name` = pName,
        `AdditionalProperty` = pConcurrencyTokenCurrent
    WHERE `Id` = pId
        AND `AdditionalProperty` = pConcurrencyTokenOriginal;
    SET pRowsAffected = ROW_COUNT();
END
""");

        AssertSql(
"""
@p1='1'
@p2='8'
@p3='Updated'
@p4='9'

SET @_out_p0 = NULL;
CALL `EntityWithAdditionalProperty_Update`(@p1, @p2, @p3, @p4, @_out_p0);
SELECT @_out_p0;
""");
    }

    public override async Task Original_and_current_value_on_non_concurrency_token(bool async)
    {
        await Original_and_current_value_on_non_concurrency_token(
            async,
"""
CREATE PROCEDURE Entity_Update(pId int, pNameCurrent varchar(255), pNameOriginal varchar(255))
BEGIN
    IF pNameCurrent <> pNameOriginal THEN
        UPDATE `Entity` SET `Name` = pNameCurrent WHERE `Id` = pId;
    END IF;
END
""");

        AssertSql(
"""
@p0='1'
@p1='Updated'
@p2='Initial'

CALL `Entity_Update`(@p0, @p1, @p2);
""");
    }

    public override async Task Input_or_output_parameter_with_input(bool async)
    {
        await Input_or_output_parameter_with_input(
            async,
"""
CREATE PROCEDURE Entity_Insert(OUT pId int, INOUT pName varchar(255))
BEGIN
    IF pName IS NULL THEN
        INSERT INTO `Entity` (`Name`) VALUES ('Some default value');
        SET pName = 'Some default value';
    ELSE
        INSERT INTO `Entity` (`Name`) VALUES (pName);
        SET pName = NULL;
    END IF;

    SET pId = LAST_INSERT_ID();
END
""");

        AssertSql(
"""
@p1='Initial' (Nullable = false)

SET @_out_p0 = NULL;
SET @_out_p1 = @p1;
CALL `Entity_Insert`(@_out_p0, @_out_p1);
SELECT @_out_p0, @_out_p1;
""");
    }

    public override async Task Input_or_output_parameter_with_output(bool async)
    {
        await Input_or_output_parameter_with_output(
            async,
"""
CREATE PROCEDURE Entity_Insert(OUT pId int, INOUT pName varchar(255))
BEGIN
    IF pName IS NULL THEN
        INSERT INTO `Entity` (`Name`) VALUES ('Some default value');
        SET pName = 'Some default value';
    ELSE
        INSERT INTO `Entity` (`Name`) VALUES (pName);
        SET pName = NULL;
    END IF;

    SET pId = LAST_INSERT_ID();
END
""");

        AssertSql(
"""
SET @_out_p0 = NULL;
SET @_out_p1 = @p1;
CALL `Entity_Insert`(@_out_p0, @_out_p1);
SELECT @_out_p0, @_out_p1;
""");
    }

    public override async Task Tph(bool async)
    {
        await Tph(
            async,
            """
            CREATE PROCEDURE Tph_Insert(
                OUT pId int,
                pDiscriminator varchar(255),
                pName varchar(255),
                pChild2InputProperty int,
                OUT pChild2OutputParameterProperty int,
                pChild1Property int)
            BEGIN
                INSERT INTO `Tph` (
                    `Discriminator`,
                    `Name`,
                    `Child2InputProperty`,
                    `Child2OutputParameterProperty`,
                    `Child2ResultColumnProperty`,
                    `Child1Property`)
                VALUES (
                    pDiscriminator,
                    pName,
                    pChild2InputProperty,
                    8,
                    9,
                    pChild1Property);
                SET pId = LAST_INSERT_ID();
                SET pChild2OutputParameterProperty = 8;
                SELECT 9 AS `Child2ResultColumnProperty`;
            END
            """);
    }

    public override async Task Tpt(bool async)
    {
        await Tpt(
            async,
            """
            CREATE PROCEDURE Parent_Insert(OUT pId int, pName varchar(255))
            BEGIN
                INSERT INTO `Parent` (`Name`) VALUES (pName);
                SET pId = LAST_INSERT_ID();
            END;

            GO;

            CREATE PROCEDURE Child1_Insert(pId int, pChild1Property int)
            INSERT INTO `Child1` (`Id`, `Child1Property`)
            VALUES (pId, pChild1Property);
            """);
    }

    public override async Task Tpt_mixed_sproc_and_non_sproc(bool async)
    {
        await Tpt_mixed_sproc_and_non_sproc(
            async,
"""
CREATE PROCEDURE Parent_Insert(OUT pId int, pName varchar(255))
BEGIN
    INSERT INTO `Parent` (`Name`) VALUES (pName);
    SET pId = LAST_INSERT_ID();
END
""");

        AssertSql(
"""
@p1='Child'

SET @_out_p0 = NULL;
CALL `Parent_Insert`(@_out_p0, @p1);
SELECT @_out_p0;
""",

                """
@p2='1'
@p3='8'

SET AUTOCOMMIT = 1;
INSERT INTO `Child1` (`Id`, `Child1Property`)
VALUES (@p2, @p3);
""");
    }

    public override async Task Tpc(bool async)
    {
        var createSprocSql =
"""
ALTER TABLE `Child1` MODIFY COLUMN `Id` INT AUTO_INCREMENT;
ALTER TABLE `Child1` AUTO_INCREMENT = 100000;

GO;

CREATE PROCEDURE Child1_Insert(OUT pId int, pName varchar(255), pChild1Property int)
BEGIN
    INSERT INTO `Child1` (`Name`, `Child1Property`) VALUES (pName, pChild1Property);
    SET pId = LAST_INSERT_ID();
END
""";

        var contextFactory = await InitializeAsync<DbContext>(
            modelBuilder =>
            {
                modelBuilder.Entity<Parent>().UseTpcMappingStrategy();

                modelBuilder.Entity<Child1>()
                    .UseTpcMappingStrategy()
                    .InsertUsingStoredProcedure(
                        nameof(Child1) + "_Insert",
                        spb => spb
                            .HasParameter(w => w.Id, pb => pb.IsOutput())
                            .HasParameter(w => w.Name)
                            .HasParameter(w => w.Child1Property))
                    .Property(e => e.Id).UseMySqlAutoIncrementColumn(); // <--
            },
            seed: ctx => CreateStoredProcedures(ctx, createSprocSql),
            onConfiguring: optionsBuilder =>
            {
                optionsBuilder.ConfigureWarnings(builder =>
                    builder.Ignore(RelationalEventId.TpcStoreGeneratedIdentityWarning)); // <-- added
            });

        await using var context = contextFactory.CreateContext();

        var entity1 = new Child1 { Name = "Child", Child1Property = 8 };
        context.Set<Child1>().Add(entity1);
        await SaveChanges(context, async);

        context.ChangeTracker.Clear();

        using (TestSqlLoggerFactory.SuspendRecordingEvents())
        {
            var entity2 = context.Set<Child1>().Single(b => b.Id == entity1.Id);

            Assert.Equal("Child", entity2.Name);
            Assert.Equal(8, entity2.Child1Property);
        }

        AssertSql(
"""
@p1='Child'
@p2='8'

SET @_out_p0 = NULL;
CALL `Child1_Insert`(@_out_p0, @p1, @p2);
SELECT @_out_p0;
""");
    }

    public override async Task Non_sproc_followed_by_sproc_commands_in_the_same_batch(bool async)
    {
        await Non_sproc_followed_by_sproc_commands_in_the_same_batch(
            async,
            """
            CREATE PROCEDURE EntityWithAdditionalProperty_Insert(pName text, OUT pId int, pAdditional_property int)
            BEGIN
                INSERT INTO EntityWithAdditionalProperty (`Name`, `AdditionalProperty`)
                VALUES (pName, pAdditional_property);
                SET pId = LAST_INSERT_ID();
            END
            """);

        AssertSql(
"""
@p2='1'
@p0='2'
@p3='1'
@p1='Entity1_Modified'
@p5='Entity2'
@p6='0'

UPDATE `EntityWithAdditionalProperty` SET `AdditionalProperty` = @p0, `Name` = @p1
WHERE `Id` = @p2 AND `AdditionalProperty` = @p3;
SELECT ROW_COUNT();

SET @_out_p4 = NULL;
CALL `EntityWithAdditionalProperty_Insert`(@p5, @_out_p4, @p6);
SELECT @_out_p4;
""");
    }

    private static async Task SaveChanges(DbContext context, bool async)
    {
        if (async)
        {
            await context.SaveChangesAsync();
        }
        else
        {
            // The false theory row intentionally exercises EF's synchronous
            // update pipeline.
            context.SaveChanges();
        }
    }
}
