namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlBinaryByteArrayIntegrationTests
{
    private const string TableName = "IntFixedBinaryItems";
    private const string PayloadHex = "00112233445566778899AABBCCDDEEFF";
    private const string ShortHex = "0011223344556677";
    private static readonly Guid s_token = Guid.Parse("f1e2d3c4-b5a6-9788-091a-2b3c4d5e6f70");
    private static readonly Guid s_convertedToken = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly MySqlGuidFormat[] s_defaultFormats = [MySqlGuidFormat.Char36, MySqlGuidFormat.Binary16];
    private static readonly bool[] s_booleanCases = [false, true];
    private static readonly string[] s_inputHexes = [PayloadHex, new('0', 32), new('F', 32), "010203", string.Empty];

    /// <summary>Reconstructs the server's zero padding without relying on GUID conversion.</summary>
    private static byte[] ExpectedBytes(
        int id
    ) => Convert.FromHexString(
        s_inputHexes[id - 1]
            .PadRight(32, '0'));

    /// <summary>Creates a complete writable row with independently controlled binary fields.</summary>
    private static BinaryItem CreateItem(
        int id,
        byte[] bytes
    ) => new()
    {
        Id = id,
        Bytes = bytes,
        OptionalBytes = bytes.ToArray(),
        Binary8 = Convert.FromHexString(ShortHex),
        Binary32 = Convert.FromHexString(PayloadHex + PayloadHex),
        Blob = Convert.FromHexString(PayloadHex),
        InferredBytes = Convert.FromHexString(PayloadHex),
        Token = s_token,
        ConvertedToken = s_convertedToken,
    };

    /// <summary>Reads stored hexadecimal bytes independently of EF's binary reader.</summary>
    private static async Task<string> ReadStoredHexAsync(
        BinaryContext context,
        int id,
        CancellationToken cancellationToken
    )
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = $"SELECT HEX(`Bytes`) FROM `{TableName}` WHERE `Id` = @id;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = id;
        command.Parameters.Add(parameter);

        return Assert.IsType<string>(await command.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>Owns one immutable binary dataset per selected engine and GUID format.</summary>
    public sealed class BinaryFixture : IAsyncLifetime
    {
        private readonly Dictionary<(IntegrationDatabaseTarget Target, MySqlGuidFormat Format), Task> _seeds = [];

        /// <inheritdoc />
        public Task InitializeAsync() => Task.CompletedTask;

        /// <inheritdoc />
        public async Task DisposeAsync()
        {
            List<Exception>? failures = null;

            foreach (var (target, format) in _seeds.Keys)
            {
                try
                {
                    await using var context = CreateContext(target, format, buffered: false);

                    // WHY: Fixture teardown must complete even after a query was cancelled.
                    await context.Database.ExecuteSqlRawAsync(
                        $"DROP TABLE IF EXISTS `{TableName}`;",
                        CancellationToken.None);
                }
                catch (Exception exception)
                {
                    // WHY: One failed endpoint must not prevent cleanup of the other owned schemas.
                    (failures ??= []).Add(exception);
                }
            }

            if (failures is not null)
            {
                throw new AggregateException("Fixed binary fixture cleanup failed.", failures);
            }
        }

        /// <summary>Seeds a format once, then returns a fresh buffered or streaming context.</summary>
        internal async Task<BinaryContext> CreateContextAsync(
            IntegrationDatabaseTarget target,
            MySqlGuidFormat format,
            bool buffered,
            CancellationToken cancellationToken
        )
        {
            // WHY: xUnit serializes this class; a retained failed task also prevents partial reseeding.
            if (!_seeds.TryGetValue((target, format), out var seed))
            {
                seed = SeedAsync(target, format, cancellationToken);
                _seeds.Add((target, format), seed);
            }

            await seed;

            return CreateContext(target, format, buffered);
        }

        /// <summary>Seeds raw byte literals so conversion cannot disguise a matching read/write defect.</summary>
        private static async Task SeedAsync(
            IntegrationDatabaseTarget target,
            MySqlGuidFormat format,
            CancellationToken cancellationToken
        )
        {
            await using var context = CreateContext(target, format, buffered: false);
            await context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{TableName}`;", cancellationToken);
            await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), cancellationToken);

            for (var index = 0; index < s_inputHexes.Length; index++)
            {
                var optional = index is 1 or 4
                    ? "NULL"
                    : $"X'{s_inputHexes[index]}'";

                // WHY: Only class-owned hex literals and integer keys enter this SQL; the GUID stays parameterized.
                var sql = $"INSERT INTO `{TableName}` (`Id`, `Bytes`, `OptionalBytes`, `Binary8`, `Binary32`, "
                    + "`Blob`, `InferredBytes`, `Token`, `ConvertedToken`) VALUES "
                    + $"({index + 1}, X'{s_inputHexes[index]}', {optional}, X'{ShortHex}', "
                    + $"X'{PayloadHex}{PayloadHex}', X'{PayloadHex}', X'{PayloadHex}', {{0}}, "
                    + $"X'{Convert.ToHexString(s_convertedToken.ToByteArray())}');";

                await context.Database.ExecuteSqlRawAsync(
                    sql,
                    [s_token],
                    cancellationToken);
            }
        }

        /// <summary>Keeps schema ownership stable across every partial and execution strategy.</summary>
        private static BinaryContext CreateContext(
            IntegrationDatabaseTarget target,
            MySqlGuidFormat format,
            bool buffered
        )
        {
            var builder = IntegrationTestDbContextOptions.Create<BinaryContext>();
            builder.UseMySql(
                IntegrationTestEnvironment.GetConnectionString(
                    target,
                    $"{nameof(MySqlBinaryByteArrayIntegrationTests)}.{format}"),
                IntegrationTestEnvironment.GetServerVersion(target),
                options =>
                {
                    options.DefaultGuidFormat(format);

                    if (buffered)
                    {
                        options.EnableRetryOnFailure();
                    }
                });

            return new BinaryContext(builder.Options);
        }
    }
}
