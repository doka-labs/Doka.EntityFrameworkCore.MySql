namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlQueryTranslationIntegrationTests
{
    /// <summary>
    /// Verifies binary GUID formatting and width-aware integral shifts against
    /// MySQL's actual binary and unsigned-bitwise behavior.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Scalar_edge_cases_execute_correctly_on_mysql84() =>
        await RunScalarEdgeCaseTests(IntegrationDatabaseTarget.MySql84);

    /// <summary>
    /// Verifies binary GUID formatting and width-aware integral shifts against
    /// MySQL 9.7's actual binary and unsigned-bitwise behavior.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task Scalar_edge_cases_execute_correctly_on_mysql97() =>
        await RunScalarEdgeCaseTests(IntegrationDatabaseTarget.MySql97);

    /// <summary>
    /// Verifies binary GUID formatting and width-aware integral shifts against
    /// MariaDB 10.11's actual binary and unsigned-bitwise behavior.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task Scalar_edge_cases_execute_correctly_on_mariadb1011() =>
        await RunScalarEdgeCaseTests(IntegrationDatabaseTarget.MariaDb1011);

    /// <summary>
    /// Verifies binary GUID formatting and width-aware integral shifts against
    /// MariaDB 11.4's actual binary and unsigned-bitwise behavior.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task Scalar_edge_cases_execute_correctly_on_mariadb114() =>
        await RunScalarEdgeCaseTests(IntegrationDatabaseTarget.MariaDb114);

    /// <summary>
    /// Verifies binary GUID formatting and width-aware integral shifts against
    /// MariaDB 11.8's actual binary and unsigned-bitwise behavior.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Scalar_edge_cases_execute_correctly_on_mariadb118() =>
        await RunScalarEdgeCaseTests(IntegrationDatabaseTarget.MariaDb118);

    /// <summary>
    /// Verifies binary GUID formatting and width-aware integral shifts against
    /// MariaDB 12.3's actual binary and unsigned-bitwise behavior.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task Scalar_edge_cases_execute_correctly_on_mariadb123() =>
        await RunScalarEdgeCaseTests(IntegrationDatabaseTarget.MariaDb123);

    private static async Task RunScalarEdgeCaseTests(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        var connectionString = GetConnectionString(target);
        await using var context = new QueryContext(CreateOptions(connectionString, target));
        await SetupAsync(context);

        var token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        try
        {
            var sourceItems = new[]
            {
                new QueryItem
                {
                    Name = "negative",
                    Value = 0,
                    Category = "S",
                    CreatedAt = new DateTime(2025, 1, 1),
                    SignedValue = -8,
                    SignedLongValue = -8,
                    UnsignedValue = 0x80000000,
                    UnsignedLongValue = 0x8000000000000000,
                    ShiftCount = 1,
                    Token = token,
                },
                new QueryItem
                {
                    Name = "positive",
                    Value = 0,
                    Category = "S",
                    CreatedAt = new DateTime(2025, 1, 1),
                    SignedValue = 8,
                    SignedLongValue = 8,
                    UnsignedValue = 8,
                    UnsignedLongValue = 8,
                    ShiftCount = 1,
                    Token = token,
                },
                new QueryItem
                {
                    Name = "int-width",
                    Value = 0,
                    Category = "S",
                    CreatedAt = new DateTime(2025, 1, 1),
                    SignedValue = -8,
                    SignedLongValue = 1,
                    UnsignedValue = 0x80000000,
                    UnsignedLongValue = 1,
                    ShiftCount = 32,
                    Token = token,
                },
                new QueryItem
                {
                    Name = "long-width",
                    Value = 0,
                    Category = "S",
                    CreatedAt = new DateTime(2025, 1, 1),
                    SignedValue = -8,
                    SignedLongValue = -8,
                    UnsignedValue = 0x80000000,
                    UnsignedLongValue = 0x8000000000000000,
                    ShiftCount = 64,
                    Token = token,
                },
                new QueryItem
                {
                    Name = "negative-count",
                    Value = 0,
                    Category = "S",
                    CreatedAt = new DateTime(2025, 1, 1),
                    SignedValue = 1,
                    SignedLongValue = 1,
                    UnsignedValue = 1,
                    UnsignedLongValue = 1,
                    ShiftCount = -1,
                    Token = token,
                },
                new QueryItem
                {
                    Name = "overflow",
                    Value = 0,
                    Category = "S",
                    CreatedAt = new DateTime(2025, 1, 1),
                    SignedValue = int.MaxValue,
                    SignedLongValue = long.MaxValue,
                    UnsignedValue = uint.MaxValue,
                    UnsignedLongValue = ulong.MaxValue,
                    ShiftCount = 1,
                    Token = token,
                },
            };

            context.Items.AddRange(sourceItems);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // Act
            // WHY: All edge projections deliberately share one translation pipeline
            // to detect width or GUID-mapping interference between columns.
            var results = await context
                .Items
                .OrderBy(item => item.Name)
                .Select(item => new
                {
                    item.Name,
                    LeftShift = item.SignedValue << item.ShiftCount,
                    RightShift = item.SignedValue >> item.ShiftCount,
                    LongLeftShift = item.SignedLongValue << item.ShiftCount,
                    LongRightShift = item.SignedLongValue >> item.ShiftCount,
                    UnsignedLeftShift = item.UnsignedValue << item.ShiftCount,
                    UnsignedRightShift = item.UnsignedValue >> item.ShiftCount,
                    UnsignedLongLeftShift = item.UnsignedLongValue << item.ShiftCount,
                    UnsignedLongRightShift = item.UnsignedLongValue >> item.ShiftCount,
                    Complement = ~item.SignedValue,
                    Token = item.Token.ToString(),
                })
                .ToListAsync();

            // Assert
            foreach (var source in sourceItems)
            {
                var result = Assert.Single(results, item => item.Name == source.Name);

                Assert.Equal(source.SignedValue << source.ShiftCount, result.LeftShift);
                Assert.Equal(source.SignedValue >> source.ShiftCount, result.RightShift);
                Assert.Equal(source.SignedLongValue << source.ShiftCount, result.LongLeftShift);
                Assert.Equal(source.SignedLongValue >> source.ShiftCount, result.LongRightShift);
                Assert.Equal(source.UnsignedValue << source.ShiftCount, result.UnsignedLeftShift);
                Assert.Equal(source.UnsignedValue >> source.ShiftCount, result.UnsignedRightShift);
                Assert.Equal(
                    source.UnsignedLongValue << source.ShiftCount,
                    result.UnsignedLongLeftShift);
                Assert.Equal(
                    source.UnsignedLongValue >> source.ShiftCount,
                    result.UnsignedLongRightShift);
                Assert.Equal(~source.SignedValue, result.Complement);
                Assert.Equal(token.ToString(), result.Token);
            }
        }
        finally
        {
            await CleanupAsync(context);
        }
    }
}
