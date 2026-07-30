namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests for <c>MySqlOptionsExtensionInfo</c>: covers the ServerVersion-null LogFragment arm,
/// the multi-field GetServiceProviderHashCode + ShouldUseSameServiceProvider chain (the latter
/// drives the 10-branch && conjunction), and PopulateDebugInfo's structured-info contract.
/// </summary>
public sealed class MySqlOptionsExtensionInfoTests
{
    // -- IsDatabaseProvider --

    [Fact]
    public void IsDatabaseProvider_is_true()
    {
        var info = new MySqlOptionsExtension().Info;
        Assert.True(info.IsDatabaseProvider);
    }

    // -- LogFragment branches --

    [Fact]
    public void LogFragment_without_server_version_is_unparameterized()
    {
        var info = new MySqlOptionsExtension().Info;
        Assert.Equal("using Doka MySql ", info.LogFragment);
    }

    [Fact]
    public void LogFragment_with_server_version_includes_version()
    {
        var extension = new MySqlOptionsExtension()
            .WithServerVersion(MySqlServerVersion.MySql(new Version(8, 4, 0)));
        var info = extension.Info;
        Assert.Contains("MySQL", info.LogFragment, StringComparison.Ordinal);
        Assert.Contains("8.4.0", info.LogFragment, StringComparison.Ordinal);
    }

    // -- PopulateDebugInfo --

    [Fact]
    public void PopulateDebugInfo_writes_doka_mysql_entry()
    {
        var info = new MySqlOptionsExtension().Info;
        var dict = new Dictionary<string, string>();
        info.PopulateDebugInfo(dict);
        Assert.True(dict.ContainsKey("DokaMySql"));
    }

    // -- ShouldUseSameServiceProvider 10-branch chain --

    [Fact]
    public void ShouldUseSameServiceProvider_for_equivalent_extensions_is_true()
    {
        var infoA = InfoFor(WithVersion(new MySqlOptionsExtension()));
        var infoB = InfoFor(WithVersion(new MySqlOptionsExtension()));
        Assert.True(infoA.ShouldUseSameServiceProvider(infoB));
    }

    [Fact]
    public void ShouldUseSameServiceProvider_for_different_extension_info_type_is_false()
    {
        var info = InfoFor(WithVersion(new MySqlOptionsExtension()));
        var otherInfo = new OtherExtensionInfo();
        Assert.False(info.ShouldUseSameServiceProvider(otherInfo));
    }

    [Fact]
    public void ShouldUseSameServiceProvider_for_different_server_version_is_false()
    {
        var infoA = InfoFor(new MySqlOptionsExtension()
            .WithServerVersion(MySqlServerVersion.MySql(new Version(8, 0, 0))));
        var infoB = InfoFor(new MySqlOptionsExtension()
            .WithServerVersion(MySqlServerVersion.MySql(new Version(8, 4, 0))));
        Assert.False(infoA.ShouldUseSameServiceProvider(infoB));
    }

    [Fact]
    public void ShouldUseSameServiceProvider_for_different_retry_options_is_false()
    {
        var infoA = InfoFor(WithVersion(new MySqlOptionsExtension())
            .WithRetryOptions(new MySqlRetryOptions(3, TimeSpan.FromSeconds(1))));
        var infoB = InfoFor(WithVersion(new MySqlOptionsExtension())
            .WithRetryOptions(new MySqlRetryOptions(5, TimeSpan.FromSeconds(2))));
        Assert.False(infoA.ShouldUseSameServiceProvider(infoB));
    }

    [Fact]
    public void ShouldUseSameServiceProvider_for_different_guid_format_is_false()
    {
        var infoA = InfoFor(WithVersion(new MySqlOptionsExtension())
            .WithDefaultGuidFormat(MySqlGuidFormat.Binary16));
        var infoB = InfoFor(WithVersion(new MySqlOptionsExtension())
            .WithDefaultGuidFormat(MySqlGuidFormat.Char36));
        Assert.False(infoA.ShouldUseSameServiceProvider(infoB));
    }

    [Fact]
    public void ShouldUseSameServiceProvider_for_different_connection_presence_is_false()
    {
        var infoA = InfoFor(WithVersion(new MySqlOptionsExtension()));
        var infoB = InfoFor(WithVersion(new MySqlOptionsExtension())
            .WithConnection(new MySqlConnection()));
        Assert.False(infoA.ShouldUseSameServiceProvider(infoB));
    }

    [Fact]
    public void ShouldUseSameServiceProvider_for_different_data_source_presence_is_false()
    {
        var infoA = InfoFor(WithVersion(new MySqlOptionsExtension()));
        var infoB = InfoFor(WithVersion(new MySqlOptionsExtension())
            .WithDataSource(new MySqlDataSourceBuilder("Server=localhost").Build()));
        Assert.False(infoA.ShouldUseSameServiceProvider(infoB));
    }

    // -- GetServiceProviderHashCode is stable + sensitive to fields --

    [Fact]
    public void GetServiceProviderHashCode_is_stable_for_equivalent_extensions()
    {
        var hashA = InfoFor(WithVersion(new MySqlOptionsExtension())).GetServiceProviderHashCode();
        var hashB = InfoFor(WithVersion(new MySqlOptionsExtension())).GetServiceProviderHashCode();
        Assert.Equal(hashA, hashB);
    }

    [Fact]
    public void GetServiceProviderHashCode_differs_when_server_version_changes()
    {
        var hashA = InfoFor(new MySqlOptionsExtension()
                .WithServerVersion(MySqlServerVersion.MySql(new Version(8, 0, 0))))
            .GetServiceProviderHashCode();
        var hashB = InfoFor(new MySqlOptionsExtension()
                .WithServerVersion(MySqlServerVersion.MySql(new Version(8, 4, 0))))
            .GetServiceProviderHashCode();
        Assert.NotEqual(hashA, hashB);
    }

    private static MySqlOptionsExtension WithVersion(MySqlOptionsExtension extension) =>
        extension.WithServerVersion(MySqlServerVersion.MySql(new Version(8, 4, 0)));

    private static DbContextOptionsExtensionInfo InfoFor(
        MySqlOptionsExtension extension
    ) => extension.Info;

    private sealed class OtherExtensionInfo : DbContextOptionsExtensionInfo
    {
        public OtherExtensionInfo() : base(new OtherExtension()) { }
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "other";
        public override int GetServiceProviderHashCode() => 0;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) { }
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => false;
    }

    private sealed class OtherExtension : IDbContextOptionsExtension
    {
        public DbContextOptionsExtensionInfo Info => null!;
        public void ApplyServices(IServiceCollection services) { }
        public void Validate(IDbContextOptions options) { }
    }
}
