namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests for <c>MySqlOptionsExtensionInfo</c>: covers the ServerVersion-null LogFragment arm,
/// the multi-field GetServiceProviderHashCode + ShouldUseSameServiceProvider chain, and
/// PopulateDebugInfo's structured-info contract.
/// </summary>
public sealed class MySqlOptionsExtensionInfoTests
{
    // -- IsDatabaseProvider --

    /// <summary>
    /// Verifies <c>IsDatabaseProvider</c> is true.
    /// </summary>
    [Fact]
    public void IsDatabaseProvider_is_true()
    {
        // Arrange
        var info = new MySqlOptionsExtension().Info;

        // Act
        var actual = info.IsDatabaseProvider;

        // Assert
        Assert.True(actual);
    }

    // -- LogFragment branches --

    /// <summary>
    /// Verifies <c>LogFragment</c> without server version is unparameterized.
    /// </summary>
    [Fact]
    public void LogFragment_without_server_version_is_unparameterized()
    {
        // Arrange
        var info = new MySqlOptionsExtension().Info;

        // Act
        var actual = info.LogFragment;

        // Assert
        Assert.Equal("using Doka MySql ", actual);
    }

    /// <summary>
    /// Verifies <c>LogFragment</c> with server version includes version.
    /// </summary>
    [Fact]
    public void LogFragment_with_server_version_includes_version()
    {
        // Arrange
        var extension = new MySqlOptionsExtension()
            .WithServerVersion(MySqlServerVersion.MySql(new Version(8, 4, 0)));

        var info = extension.Info;

        // Act
        var actual = info.LogFragment;

        // Assert
        Assert.Contains("MySQL", actual, StringComparison.Ordinal);
        Assert.Contains("8.4.0", actual, StringComparison.Ordinal);
    }

    // -- PopulateDebugInfo --

    /// <summary>
    /// Verifies <c>PopulateDebugInfo</c> writes doka mysql entry.
    /// </summary>
    [Fact]
    public void PopulateDebugInfo_writes_doka_mysql_entry()
    {
        // Arrange
        var info = new MySqlOptionsExtension().Info;
        var dict = new Dictionary<string, string>();

        // Act
        info.PopulateDebugInfo(dict);

        // Assert
        Assert.True(dict.ContainsKey("DokaMySql"));
    }

    // -- ShouldUseSameServiceProvider identity branches --

    /// <summary>
    /// Verifies <c>ShouldUseSameServiceProvider</c> for equivalent extensions is true.
    /// </summary>
    [Fact]
    public void ShouldUseSameServiceProvider_for_equivalent_extensions_is_true()
    {
        // Arrange
        var infoA = InfoFor(WithVersion(new MySqlOptionsExtension()));
        var infoB = InfoFor(WithVersion(new MySqlOptionsExtension()));

        // Act
        var actual = infoA.ShouldUseSameServiceProvider(infoB);

        // Assert
        Assert.True(actual);
    }

    /// <summary>
    /// Verifies <c>ShouldUseSameServiceProvider</c> for different extension info type is false.
    /// </summary>
    [Fact]
    public void ShouldUseSameServiceProvider_for_different_extension_info_type_is_false()
    {
        // Arrange
        var info = InfoFor(WithVersion(new MySqlOptionsExtension()));
        var otherInfo = new OtherExtensionInfo();

        // Act
        var actual = info.ShouldUseSameServiceProvider(otherInfo);

        // Assert
        Assert.False(actual);
    }

    /// <summary>
    /// Verifies <c>ShouldUseSameServiceProvider</c> for different server version is false.
    /// </summary>
    [Fact]
    public void ShouldUseSameServiceProvider_for_different_server_version_is_false()
    {
        // Arrange
        var infoA = InfoFor(new MySqlOptionsExtension()
            .WithServerVersion(MySqlServerVersion.MySql(new Version(8, 0, 0))));

        var infoB = InfoFor(new MySqlOptionsExtension()
            .WithServerVersion(MySqlServerVersion.MySql(new Version(8, 4, 0))));

        // Act
        var actual = infoA.ShouldUseSameServiceProvider(infoB);

        // Assert
        Assert.False(actual);
    }

    /// <summary>
    /// Verifies <c>ShouldUseSameServiceProvider</c> for different retry options is false.
    /// </summary>
    [Fact]
    public void ShouldUseSameServiceProvider_for_different_retry_options_is_false()
    {
        // Arrange
        var infoA = InfoFor(WithVersion(new MySqlOptionsExtension())
            .WithRetryOptions(new MySqlRetryOptions(3, TimeSpan.FromSeconds(1))));

        var infoB = InfoFor(WithVersion(new MySqlOptionsExtension())
            .WithRetryOptions(new MySqlRetryOptions(5, TimeSpan.FromSeconds(2))));

        // Act
        var actual = infoA.ShouldUseSameServiceProvider(infoB);

        // Assert
        Assert.False(actual);
    }

    /// <summary>
    /// Verifies <c>ShouldUseSameServiceProvider</c> for different guid format is false.
    /// </summary>
    [Fact]
    public void ShouldUseSameServiceProvider_for_different_guid_format_is_false()
    {
        // Arrange
        var infoA = InfoFor(WithVersion(new MySqlOptionsExtension())
            .WithDefaultGuidFormat(MySqlGuidFormat.Binary16));

        var infoB = InfoFor(WithVersion(new MySqlOptionsExtension())
            .WithDefaultGuidFormat(MySqlGuidFormat.Char36));

        // Act
        var actual = infoA.ShouldUseSameServiceProvider(infoB);

        // Assert
        Assert.False(actual);
    }

    /// <summary>
    /// Verifies parameterized collection mode separates internal service providers.
    /// </summary>
    [Fact]
    public void Parameterized_collection_mode_separates_internal_service_providers()
    {
        // Arrange
        var parameter = WithVersion(new MySqlOptionsExtension())
            .WithUseParameterizedCollectionMode(ParameterTranslationMode.Parameter);

        var multiple = WithVersion(new MySqlOptionsExtension())
            .WithUseParameterizedCollectionMode(ParameterTranslationMode.MultipleParameters);

        // Act
        var actual = multiple.Info.GetServiceProviderHashCode();

        // Assert
        Assert.NotEqual(
            parameter.Info.GetServiceProviderHashCode(),
            actual);
        Assert.False(parameter.Info.ShouldUseSameServiceProvider(multiple.Info));
        Assert.False(multiple.Info.ShouldUseSameServiceProvider(parameter.Info));
    }

    /// <summary>
    /// Verifies <c>ShouldUseSameServiceProvider</c> for different connection presence is false.
    /// </summary>
    [Fact]
    public void ShouldUseSameServiceProvider_for_different_connection_presence_is_false()
    {
        // Arrange
        var infoA = InfoFor(WithVersion(new MySqlOptionsExtension()));
        var infoB = InfoFor(WithVersion(new MySqlOptionsExtension())
            .WithConnection(new MySqlConnection("Server=localhost;GuidFormat=Binary16")));

        // Act
        var actual = infoA.ShouldUseSameServiceProvider(infoB);

        // Assert
        Assert.False(actual);
    }

    /// <summary>
    /// Verifies <c>ShouldUseSameServiceProvider</c> for different data source presence is false.
    /// </summary>
    [Fact]
    public void ShouldUseSameServiceProvider_for_different_data_source_presence_is_false()
    {
        // Arrange
        var infoA = InfoFor(WithVersion(new MySqlOptionsExtension()));
        var infoB = InfoFor(WithVersion(new MySqlOptionsExtension())
            .WithDataSource(new MySqlDataSourceBuilder(
                "Server=localhost;GuidFormat=Binary16").Build()));

        // Act
        var actual = infoA.ShouldUseSameServiceProvider(infoB);

        // Assert
        Assert.False(actual);
    }

    /// <summary>
    /// Verifies user variable requirement does not fragment the internal service provider.
    /// </summary>
    [Fact]
    public void User_variable_requirement_does_not_fragment_the_internal_service_provider()
    {
        // Arrange
        var optional = WithVersion(new MySqlOptionsExtension());
        var required = optional.WithUserVariablesRequired();

        // Act
        var actual = required.Info.GetServiceProviderHashCode();

        // Assert
        Assert.Equal(
            optional.Info.GetServiceProviderHashCode(),
            actual);
        Assert.True(optional.Info.ShouldUseSameServiceProvider(required.Info));
        Assert.True(required.Info.ShouldUseSameServiceProvider(optional.Info));
    }

    // -- GetServiceProviderHashCode is stable + sensitive to fields --

    /// <summary>
    /// Verifies <c>GetServiceProviderHashCode</c> is stable for equivalent extensions.
    /// </summary>
    [Fact]
    public void GetServiceProviderHashCode_is_stable_for_equivalent_extensions()
    {
        // Arrange
        var hashA = InfoFor(WithVersion(new MySqlOptionsExtension())).GetServiceProviderHashCode();

        // Act
        var hashB = InfoFor(WithVersion(new MySqlOptionsExtension())).GetServiceProviderHashCode();

        // Assert
        Assert.Equal(hashA, hashB);
    }

    /// <summary>
    /// Verifies <c>GetServiceProviderHashCode</c> differs when server version changes.
    /// </summary>
    [Fact]
    public void GetServiceProviderHashCode_differs_when_server_version_changes()
    {
        // Arrange
        var hashA = InfoFor(new MySqlOptionsExtension()
                .WithServerVersion(MySqlServerVersion.MySql(new Version(8, 0, 0))))
            .GetServiceProviderHashCode();

        // Act
        var hashB = InfoFor(new MySqlOptionsExtension()
                .WithServerVersion(MySqlServerVersion.MySql(new Version(8, 4, 0))))
            .GetServiceProviderHashCode();

        // Assert
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
