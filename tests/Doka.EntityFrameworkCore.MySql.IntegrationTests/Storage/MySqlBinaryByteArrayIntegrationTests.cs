using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies fixed binary bytes through the provider's Binary16 connector contract.</summary>
[IntegrationDatabaseTest]
public sealed partial class MySqlBinaryByteArrayIntegrationTests
    : IClassFixture<MySqlBinaryByteArrayIntegrationTests.BinaryFixture>
{
    private readonly BinaryFixture _fixture;

    /// <summary>Uses the class-owned binary datasets.</summary>
    public MySqlBinaryByteArrayIntegrationTests(
        BinaryFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>Provides selected engines and both configured GUID defaults.</summary>
    public static IEnumerable<object[]> FormatCases => from target in IntegrationTestEnvironment.GetSupportedTargets()
        where IntegrationTestEnvironment.IsTargetSelected(target)
        from format in s_defaultFormats
        select new object[] { target.ToString(), format };

    /// <summary>Separates streaming and retry-buffered reads for synchronous and asynchronous execution.</summary>
    public static IEnumerable<object[]> ReadCases => from formatCase in FormatCases
        from buffered in s_booleanCases
        from asynchronous in s_booleanCases
        select new[] { formatCase[0], formatCase[1], buffered, asynchronous };

    /// <summary>Separates tracked and untracked entity materialization in both reader modes.</summary>
    public static IEnumerable<object[]> EntityCases => from formatCase in FormatCases
        from buffered in s_booleanCases
        from tracking in s_booleanCases
        select new[] { formatCase[0], formatCase[1], buffered, tracking };

    /// <summary>Explicit binary(16) scalar values retain their exact bytes, padding, and row ownership.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(ReadCases))]
    public async Task Fixed_binary_scalar_preserves_exact_stored_bytes(
        string target,
        MySqlGuidFormat format,
        bool buffered,
        bool asynchronous
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            format,
            buffered,
            CancellationToken.None);

        Assert.Equal(
            buffered,
            context.Database.CreateExecutionStrategy()
                .RetriesOnFailure);

        var query = context
            .Set<BinaryItem>()
            .Where(item => item.Id <= 5)
            .OrderBy(item => item.Id)
            .Select(item => item.Bytes);

        // Act
        byte[][] actual;

        if (asynchronous)
        {
            actual = await query.ToArrayAsync(CancellationToken.None);
        }
        else
        {
            // WHY: The synchronous shaper is an independent supported reader path.
            // ReSharper disable once MethodHasAsyncOverload
            actual = query.ToArray();
        }

        // Assert
        Assert.Equal(s_inputHexes.Length, actual.Length);

        for (var index = 0; index < actual.Length; index++)
        {
            Assert.Equal(ExpectedBytes(index + 1), actual[index]);
        }

        Assert.NotSame(actual[1], actual[4]);
    }

    /// <summary>
    /// Materializing mixed rows preserves nullable bytes, GUIDs, converters, and other binary widths.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(EntityCases))]
    public async Task Fixed_binary_entity_preserves_mixed_mapping_values(
        string target,
        MySqlGuidFormat format,
        bool buffered,
        bool tracking
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            format,
            buffered,
            CancellationToken.None);

        Assert.Equal(
            buffered,
            context.Database.CreateExecutionStrategy()
                .RetriesOnFailure);

        var query = context
            .Set<BinaryItem>()
            .Where(item => item.Id <= 5)
            .OrderBy(item => item.Id)
            .AsQueryable();

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        // Act
        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(s_inputHexes.Length, actual.Length);
        Assert.Equal(
            tracking
                ? actual.Length
                : 0,
            context
                .ChangeTracker
                .Entries()
                .Count());

        foreach (var item in actual)
        {
            Assert.Equal(ExpectedBytes(item.Id), item.Bytes);
            Assert.Equal(
                item.Id is 2 or 5
                    ? null
                    : ExpectedBytes(item.Id),
                item.OptionalBytes);

            Assert.Equal(Convert.FromHexString(ShortHex), item.Binary8);
            Assert.Equal(Convert.FromHexString(PayloadHex + PayloadHex), item.Binary32);
            Assert.Equal(Convert.FromHexString(PayloadHex), item.Blob);
            Assert.Equal(Convert.FromHexString(PayloadHex), item.InferredBytes);
            Assert.Equal(s_token, item.Token);
            Assert.Equal(s_convertedToken, item.ConvertedToken);
        }
    }

    /// <summary>SQL NULL remains nullable without invoking the non-null byte adapter.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(FormatCases))]
    public async Task Nullable_binary_scalar_preserves_null_and_non_null_values(
        string target,
        MySqlGuidFormat format
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            format,
            buffered: true,
            CancellationToken.None);

        // Act
        var actual = await context
            .Set<BinaryItem>()
            .Where(item => item.Id <= 5)
            .OrderBy(item => item.Id)
            .Select(item => item.OptionalBytes)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(s_inputHexes.Length, actual.Length);
        Assert.Equal(ExpectedBytes(1), actual[0]);
        Assert.Null(actual[1]);
        Assert.Equal(ExpectedBytes(3), actual[2]);
        Assert.Equal(ExpectedBytes(4), actual[3]);
        Assert.Null(actual[4]);
    }

    /// <summary>User GUID-to-byte converters see their original little-endian provider bytes.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(ReadCases))]
    public async Task Converted_binary_scalar_preserves_user_converter_byte_order(
        string target,
        MySqlGuidFormat format,
        bool buffered,
        bool asynchronous
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            format,
            buffered,
            CancellationToken.None);

        var query = context
            .Set<BinaryItem>()
            .Where(item => item.Id == 1)
            .Select(item => item.ConvertedToken);

        // Act
        Guid actual;

        if (asynchronous)
        {
            actual = await query.SingleAsync(CancellationToken.None);
        }
        else
        {
            // WHY: Converter composition must work through the synchronous shaper too.
            // ReSharper disable once MethodHasAsyncOverload
            actual = query.Single();
        }

        // Assert
        Assert.Equal(s_convertedToken, actual);
    }

    /// <summary>A compiled synchronous query retains the fixed binary reader customization.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(FormatCases))]
    public async Task Compiled_query_materializes_binary_and_guid_properties(
        string target,
        MySqlGuidFormat format
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            format,
            buffered: false,
            CancellationToken.None);

        var query = EF.CompileQuery((BinaryContext source, int id) => source
            .Set<BinaryItem>()
            .Single(item => item.Id == id));

        // Act
        var actual = query(context, 1);

        // Assert
        Assert.Equal(ExpectedBytes(1), actual.Bytes);
        Assert.Equal(s_token, actual.Token);
        Assert.Equal(s_convertedToken, actual.ConvertedToken);
    }

    /// <summary>A buffered compiled asynchronous query preserves all byte arrays after reader disposal.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(FormatCases))]
    public async Task Compiled_async_query_materializes_binary_and_guid_properties(
        string target,
        MySqlGuidFormat format
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            format,
            buffered: true,
            CancellationToken.None);

        Assert.True(
            context.Database.CreateExecutionStrategy()
                .RetriesOnFailure);

        Expression<Func<BinaryContext, IQueryable<BinaryItem>>> expression = source => source
            .Set<BinaryItem>()
            .Where(item => item.Id <= 5)
            .OrderBy(item => item.Id);

        var query = EF.CompileAsyncQuery(expression);
        var actual = new List<BinaryItem>();

        // Act
        await foreach (var item in query(context)
            .WithCancellation(CancellationToken.None))
        {
            actual.Add(item);
        }

        // Assert
        Assert.Equal(s_inputHexes.Length, actual.Count);
        Assert.All(actual, item => Assert.Equal(ExpectedBytes(item.Id), item.Bytes));
        Assert.All(actual, item => Assert.Equal(s_convertedToken, item.ConvertedToken));
    }

    /// <summary>Inserting an opaque binary payload stores the unchanged bytes and supports EF readback.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(FormatCases))]
    public async Task Insert_preserves_binary_bytes_and_readback(
        string target,
        MySqlGuidFormat format
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            format,
            buffered: false,
            CancellationToken.None);

        // WHY: Explicit keys need no asynchronous value generator; SaveChangesAsync owns I/O.
        // ReSharper disable once MethodHasAsyncOverload
        context.Add(CreateItem(100, Convert.FromHexString(PayloadHex)));

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        var storedHex = await ReadStoredHexAsync(context, 100, CancellationToken.None);
        var actual = await context
            .Set<BinaryItem>()
            .AsNoTracking()
            .SingleAsync(item => item.Id == 100, CancellationToken.None);

        // Assert
        Assert.Equal(PayloadHex, storedHex);
        Assert.Equal(Convert.FromHexString(PayloadHex), actual.Bytes);
        Assert.Equal(s_token, actual.Token);
        Assert.Equal(s_convertedToken, actual.ConvertedToken);
    }

    /// <summary>Updating an existing opaque payload retains byte order in both storage and readback.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(FormatCases))]
    public async Task Update_preserves_binary_bytes_and_readback(
        string target,
        MySqlGuidFormat format
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            format,
            buffered: false,
            CancellationToken.None);

        var item = CreateItem(101, new byte[16]);

        // WHY: Explicit keys need no asynchronous value generator; SaveChangesAsync owns I/O.
        // ReSharper disable once MethodHasAsyncOverload
        context.Add(item);
        await context.SaveChangesAsync(CancellationToken.None);
        var replacement = Convert.FromHexString("FFEEDDCCBBAA99887766554433221100");

        // Act
        item.Bytes = replacement;
        await context.SaveChangesAsync(CancellationToken.None);
        var storedHex = await ReadStoredHexAsync(context, 101, CancellationToken.None);
        var actual = await context
            .Set<BinaryItem>()
            .AsNoTracking()
            .SingleAsync(row => row.Id == 101, CancellationToken.None);

        // Assert
        Assert.Equal(Convert.ToHexString(replacement), storedHex);
        Assert.Equal(replacement, actual.Bytes);
        Assert.Equal(s_token, actual.Token);
    }

    /// <summary>
    /// Oversized fixed binary input still fails rather than silently changing schema or truncating bytes.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(FormatCases))]
    public async Task Insert_rejects_oversized_fixed_binary_payload(
        string target,
        MySqlGuidFormat format
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            format,
            buffered: false,
            CancellationToken.None);

        // WHY: Explicit keys need no asynchronous value generator; SaveChangesAsync owns I/O.
        // ReSharper disable once MethodHasAsyncOverload
        context.Add(CreateItem(102, new byte[17]));

        // Act
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context
            .SaveChangesAsync(CancellationToken.None));

        // Assert
        Assert.Equal(
            1406,
            Assert.IsType<MySqlException>(exception.InnerException)
                .Number);
    }
}
