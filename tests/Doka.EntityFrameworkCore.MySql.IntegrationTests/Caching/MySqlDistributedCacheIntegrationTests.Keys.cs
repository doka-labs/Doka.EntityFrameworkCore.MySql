namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies key identity and validation.</summary>
public sealed partial class MySqlDistributedCacheIntegrationTests
{
    /// <summary>Verifies that binary cache keys retain ordinal identity.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Keys_are_ordinal(
        string targetName
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        string[] keys =
        [
            "case",
            "CASE",
            "space",
            "space ",
            "nul",
            "nul\0",
            "\u00e9",
            "e\u0301",
            "\ud83d\ude80",
            "'; DROP TABLE cache; --",
            new string('a', 1024),
            new string('\u00e9', 512),
        ];

        // Act
        // WHY: Colliding case, padding, and normalization forms must coexist in
        // one table; separate cases could miss a non-ordinal key collision.
        for (var index = 0; index < keys.Length; index++)
        {
            await store
                .Cache
                .SetAsync(keys[index], [(byte)index], new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);
        }

        var results = new byte[]?[keys.Length];

        for (var index = 0; index < keys.Length; index++)
        {
            results[index] = await store.Cache.GetAsync(keys[index], CancellationToken.None).ConfigureAwait(false);
        }

        var firstResult = await store.Cache.GetAsync("case", CancellationToken.None).ConfigureAwait(false);

        // Assert
        for (var index = 0; index < keys.Length; index++)
        {
            Assert.Equal(new[] { (byte)index }, results[index]);
        }

        Assert.Equal(new byte[] { 0 }, firstResult);
    }

    /// <summary>Verifies rejection of cache keys that exceed the UTF-8 byte limit.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Oversized_utf8_keys_are_rejected(
        string targetName
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        var oversizedKey = new string('\u00e9', 513);
        const string validKey = "valid-after-oversized";

        await store
            .Cache
            .SetAsync(validKey, [1], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        // Act
        var oversizedFailure = await Record
            .ExceptionAsync(() => store.Cache.SetAsync(
                oversizedKey,
                [1],
                new DistributedCacheEntryOptions(),
                CancellationToken.None))
            .ConfigureAwait(false);

        // Assert
        Assert.IsType<ArgumentException>(oversizedFailure);
        Assert.Equal(
            new byte[] { 1 },
            await store
                .Cache
                .GetAsync(validKey, CancellationToken.None)
                .ConfigureAwait(false));
    }

    /// <summary>Verifies rejection of malformed UTF-16 cache keys.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Malformed_utf16_keys_are_rejected(
        string targetName
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        const string malformedKey = "invalid\ud800";

        // Act
        // WHY: Malformed-key validation must also reject the synchronous Get path.
        // ReSharper disable once MethodHasAsyncOverload
        var malformedFailure = Record.Exception(() => store.Cache.Get(malformedKey));

        // Assert
        Assert.IsType<ArgumentException>(malformedFailure);
    }
}
