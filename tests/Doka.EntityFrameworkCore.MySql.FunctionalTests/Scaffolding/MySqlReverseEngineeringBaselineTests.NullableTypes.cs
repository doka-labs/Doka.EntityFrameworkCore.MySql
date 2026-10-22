namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlReverseEngineeringBaselineTests
{
    /// <summary>Provides required and nullable store mappings for every supported scalar and reference shape.</summary>
    public static IEnumerable<object[]> NullableColumnTypeCases()
    {
        foreach (var mapping in new (string StoreType, string ClrName, bool TextGuidOptIn, string ServerVersion)[]
        {
            ("tinyint(1)", "bool", false, "8.4.6"),
            ("tinyint unsigned", "byte", false, "8.4.6"),
            ("tinyint", "sbyte", false, "8.4.6"),
            ("smallint", "short", false, "8.4.6"),
            ("smallint unsigned", "ushort", false, "8.4.6"),
            ("int", "int", false, "8.4.6"),
            ("int unsigned", "uint", false, "8.4.6"),
            ("bigint", "long", false, "8.4.6"),
            ("bigint unsigned", "ulong", false, "8.4.6"),
            ("float", "float", false, "8.4.6"),
            ("double", "double", false, "8.4.6"),
            ("decimal(18,2)", "decimal", false, "8.4.6"),
            ("binary(16)", "Guid", false, "8.4.6"),
            ("datetime(6)", "DateTime", false, "8.4.6"),
            ("date", "DateOnly", false, "8.4.6"),
            ("time(6)", "TimeOnly", false, "8.4.6"),
            ("char(36)", "Guid", true, "8.4.6"),
            ("uuid", "Guid", false, "11.8.3-MariaDB"),
            ("varchar(64)", "string", false, "8.4.6"),
            ("varbinary(64)", "byte[]", false, "8.4.6"),
        })
        {
            yield return [mapping.StoreType, mapping.ClrName, mapping.TextGuidOptIn, mapping.ServerVersion, false];
            yield return [mapping.StoreType, mapping.ClrName, mapping.TextGuidOptIn, mapping.ServerVersion, true];
        }
    }

    /// <summary>Preserves column nullability in the actual entity code emitted by reverse engineering.</summary>
    /// <param name="storeType">The column's physical store type.</param>
    /// <param name="clrName">The expected C# type spelling without a nullable suffix.</param>
    /// <param name="textGuidOptIn">Whether textual GUID columns are explicitly scaffolded as Guid.</param>
    /// <param name="serverVersion">The configured MySQL or MariaDB version used by reverse engineering.</param>
    /// <param name="isNullable">Whether the database column permits null.</param>
    [Theory]
    [MemberData(nameof(NullableColumnTypeCases))]
    public void Reverse_engineering_preserves_scalar_column_nullability(
        string storeType,
        string clrName,
        bool textGuidOptIn,
        string serverVersion,
        bool isNullable
    )
    {
        // Arrange
        var databaseModel = CreateSingleColumnDatabaseModel("nullable_type_record", "Value", storeType);
        var column = databaseModel.Tables.Single().Columns.Single(candidate => candidate.Name == "Value");

        column.IsNullable = isNullable;

        // Act
        var scaffolded = ScaffoldModel(
            databaseModel,
            serverVersion,
            textGuidOptIn ? options => options.ScaffoldTextGuidsAsGuids() : null);

        // Assert
        var entityCode = Assert.Single(scaffolded.AdditionalFiles).Code;
        var expectedType = isNullable ? clrName + "?" : clrName;

        Assert.Contains($"public {expectedType} Value", entityCode, StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"public {(isNullable ? clrName : clrName + "?")} Value",
            entityCode,
            StringComparison.Ordinal);
    }
}
