namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies immutability and reentry guarantees on the public handler value
/// objects.
/// </summary>
public sealed partial class MySqlMigrationOperationContractTests
{
    /// <summary>Provides a nonstandard operation for rendering-lease contract tests.</summary>
    private sealed class CustomOperation : MigrationOperation;

    /// <summary>Creates the conventional GUID column used by metadata projection cases.</summary>
    private static AddColumnOperation CreateColumnOperation() => new()
    {
        Name = "Id",
        Table = "Entries",
        ClrType = typeof(Guid),
        ColumnType = "char(36)",
    };

    /// <summary>Creates a GUID column whose physical type matches the selected format.</summary>
    private static AddColumnOperation CreateGuidColumnOperation(
        MySqlGuidFormat format
    )
    {
        var operation = CreateColumnOperation();
        operation.ColumnType = format == MySqlGuidFormat.Binary16
            ? "binary(16)"
            : "char(36)";

        return operation;
    }
}
