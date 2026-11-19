namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies immutable application text defaults and their provider identity.</summary>
public sealed partial class MySqlDefaultTextOptionsTests
{
    private const string ConnectionString = "Server=localhost;Database=unused;User ID=root;Password=unused;";

    /// <summary>Both public options use the existing builder and immutable extension.</summary>
    [Fact]
    public void Text_defaults_preserve_builder_identity_and_extension_snapshot()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();
        builder.UseMySql(ConnectionString, MySqlServerVersion.MariaDb(new Version(12, 3, 0)));
        var provider = new MySqlDbContextOptionsBuilder(builder);
        var original = builder.Options.FindExtension<MySqlOptionsExtension>()!;

        // Act
        var result = provider
            .DefaultCharSet("utf8mb4")
            .DefaultCollation("utf8mb4_unicode_ci");

        var updated = builder.Options.FindExtension<MySqlOptionsExtension>()!;

        // Assert
        Assert.Same(provider, result);
        Assert.Null(original.DefaultCharSet);
        Assert.Null(original.DefaultCollation);
        Assert.Equal("utf8mb4", updated.DefaultCharSet);
        Assert.Equal("utf8mb4_unicode_ci", updated.DefaultCollation);
    }

    /// <summary>Each removed option resets only its own immutable fallback.</summary>
    /// <param name="resetCharSet">Whether to remove the charset rather than collation.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Null_resets_only_the_selected_default(
        bool resetCharSet
    )
    {
        // Arrange
        var builder = CreateOptions("utf8mb4", "utf8mb4_unicode_ci");
        var original = builder.Options.FindExtension<MySqlOptionsExtension>()!;
        var provider = new MySqlDbContextOptionsBuilder(builder);

        // Act
        if (resetCharSet)
        {
            provider.DefaultCharSet(null);
        }
        else
        {
            provider.DefaultCollation(null);
        }

        var updated = builder.Options.FindExtension<MySqlOptionsExtension>()!;

        // Assert
        Assert.Equal(resetCharSet
                ? null
                : "utf8mb4",
            updated.DefaultCharSet);

        Assert.Equal(resetCharSet
                ? "utf8mb4_unicode_ci"
                : null,
            updated.DefaultCollation);

        Assert.Equal("utf8mb4", original.DefaultCharSet);
        Assert.Equal("utf8mb4_unicode_ci", original.DefaultCollation);
    }

    /// <summary>Model-affecting defaults separate otherwise identical internal providers.</summary>
    /// <param name="charSet">The changed optional charset.</param>
    /// <param name="collation">The changed optional collation.</param>
    [Theory]
    [InlineData("utf8mb4", "utf8mb4_bin")]
    [InlineData("latin1", "latin1_swedish_ci")]
    [InlineData(null, "utf8mb4_unicode_ci")]
    [InlineData("utf8mb4", null)]
    public void Changed_defaults_have_distinct_service_provider_identity(
        string? charSet,
        string? collation
    )
    {
        // Arrange
        var initial = CreateOptions("utf8mb4", "utf8mb4_unicode_ci")
            .Options
            .FindExtension<MySqlOptionsExtension>()!;

        var changed = CreateOptions(charSet, collation)
            .Options
            .FindExtension<MySqlOptionsExtension>()!;

        // Act
        var sameProvider = initial.Info.ShouldUseSameServiceProvider(changed.Info);

        // Assert
        Assert.False(sameProvider);
    }

    /// <summary>Equivalent options retain the existing provider and query caches.</summary>
    [Fact]
    public void Equivalent_defaults_reuse_service_provider_identity()
    {
        // Arrange
        var first = CreateOptions("utf8mb4", "utf8mb4_unicode_ci")
            .Options
            .FindExtension<MySqlOptionsExtension>()!;

        var second = CreateOptions("utf8mb4", "utf8mb4_unicode_ci")
            .Options
            .FindExtension<MySqlOptionsExtension>()!;

        // Act
        var sameProvider = first.Info.ShouldUseSameServiceProvider(second.Info);

        // Assert
        Assert.True(sameProvider);
        Assert.Equal(first.Info.GetServiceProviderHashCode(), second.Info.GetServiceProviderHashCode());
    }

    /// <summary>Final options reject a mismatched pair even if model overrides could hide it.</summary>
    [Fact]
    public void Incompatible_option_pair_is_rejected_before_model_use()
    {
        // Arrange
        var builder = CreateOptions("latin1", "utf8mb4_unicode_ci");
        var extension = builder.Options.FindExtension<MySqlOptionsExtension>()!;

        // Act
        var exception = Record.Exception(() => extension.Validate(builder.Options));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>Explicit internal-provider reuse cannot retain another context's defaults.</summary>
    [Fact]
    public void Singleton_snapshot_rejects_changed_text_defaults()
    {
        // Arrange
        var original = CreateOptions("utf8mb4", "utf8mb4_unicode_ci")
            .Options;

        var changed = CreateOptions("utf8mb4", "utf8mb4_bin")
            .Options;

        var singleton = new MySqlSingletonOptions();
        singleton.Initialize(original);

        // Act
        var exception = Record.Exception(() => singleton.Validate(changed));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    private static DbContextOptionsBuilder CreateOptions(
        string? charSet,
        string? collation
    )
    {
        var builder = new DbContextOptionsBuilder();
        builder.UseMySql(ConnectionString,
            MySqlServerVersion.MariaDb(new Version(12, 3, 0)),
            options => options
                .DefaultCharSet(charSet)
                .DefaultCollation(collation));

        return builder;
    }
}
