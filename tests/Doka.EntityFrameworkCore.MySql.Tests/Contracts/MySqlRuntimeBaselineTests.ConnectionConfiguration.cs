using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlRuntimeBaselineTests
{
    /// <summary>
    /// Verifies that EF resolves a named token before Doka parses and
    /// normalizes the resulting provider connection string.
    /// </summary>
    [Fact]
    public void Named_connection_string_is_resolved_before_provider_normalization()
    {
        // Arrange
        const string token = "Name=ConnectionStrings:Main";
        const string resolved =
            "Server=resolved;Database=doka;User ID=provider;Password=secret;";
        var resolver = new RecordingNamedConnectionStringResolver(token, resolved);
        var services = new ServiceCollection();
        services.AddEntityFrameworkDokaMySql();
        #pragma warning disable EF1001 // Replaces EF's internal named-string seam to pin provider ordering.
        services.Replace(ServiceDescriptor.Singleton<
            Microsoft.EntityFrameworkCore.Storage.Internal.INamedConnectionStringResolver>(resolver));
        #pragma warning restore EF1001

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var builder = new DbContextOptionsBuilder<RuntimeBaselineContext>();
        builder.UseInternalServiceProvider(serviceProvider);
        builder.UseMySql(
            token,
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.RequireUserVariables());

        using var context = new RuntimeBaselineContext(builder.Options);

        // Act
        var extension = builder.Options.FindExtension<MySqlOptionsExtension>();
        var physical = new MySqlConnectionStringBuilder(
            context.Database.GetDbConnection().ConnectionString);

        // Assert
        Assert.NotNull(extension);
        Assert.Equal(token, extension.ConnectionString);
        Assert.Equal(token, resolver.LastInput);
        Assert.Equal("resolved", physical.Server);
        Assert.Equal(MySqlConnector.MySqlGuidFormat.Binary16, physical.GuidFormat);
        Assert.True(physical.AllowUserVariables);
    }

    /// <summary>
    /// Verifies that named-string resolution cannot bypass any provider-owned
    /// connection invariant.
    /// </summary>
    [Theory]
    [InlineData(
        "UseAffectedRows=true",
        nameof(MySqlConfigurationFailureReason.ChangedRowSemanticsUnsupported))]
    [InlineData(
        "GuidFormat=Char36",
        nameof(MySqlConfigurationFailureReason.GuidTransportIncompatible))]
    [InlineData(
        "AllowUserVariables=false",
        nameof(MySqlConfigurationFailureReason.UserVariablesUnavailable))]
    public void Named_connection_string_rejects_incompatible_resolved_configuration(
        string incompatibleOption,
        string expectedReason
    )
    {
        // Arrange
        const string token = "Name=ConnectionStrings:Main";
        var resolved =
            $"Server=resolved;Database=doka;Password=secret-canary;{incompatibleOption};";
        var resolver = new RecordingNamedConnectionStringResolver(token, resolved);
        var services = new ServiceCollection();
        services.AddEntityFrameworkDokaMySql();
        #pragma warning disable EF1001 // Replaces EF's internal named-string seam to pin provider ordering.
        services.Replace(ServiceDescriptor.Singleton<
            Microsoft.EntityFrameworkCore.Storage.Internal.INamedConnectionStringResolver>(resolver));
        #pragma warning restore EF1001

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var builder = new DbContextOptionsBuilder<RuntimeBaselineContext>();
        builder.UseInternalServiceProvider(serviceProvider);
        builder.UseMySql(
            token,
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.RequireUserVariables());
        using var context = new RuntimeBaselineContext(builder.Options);
        Action act = () =>
            _ = context.Database.GetDbConnection();

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlConnectionContractException>(recordedException);
        Assert.Equal(
            Enum.Parse<MySqlConfigurationFailureReason>(expectedReason),
            exception.Reason);
        Assert.Equal(token, resolver.LastInput);
        Assert.DoesNotContain("secret-canary", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that provider defaults do not erase driver-owned TLS, pool, or
    /// multi-host settings from a provider-created physical connection.
    /// </summary>
    [Fact]
    public void Relational_connection_preserves_advanced_connector_options()
    {
        // Arrange
        var configured = new MySqlConnectionStringBuilder
        {
            Server = "primary,secondary",
            Port = 3307,
            Database = "doka",
            UserID = "provider",
            Password = "secret",
            ApplicationName = "custom-pool",
            SslMode = MySqlSslMode.VerifyFull,
            SslCa = "/certificates/ca.pem",
            SslCert = "/certificates/client.pem",
            SslKey = "/certificates/client-key.pem",
            LoadBalance = MySqlLoadBalance.FailOver,
            Pooling = true,
            MinimumPoolSize = 2,
            MaximumPoolSize = 16,
            ConnectionReset = true,
            ConnectionTimeout = 9,
            AllowUserVariables = true,
        };

        var optionsBuilder = new DbContextOptionsBuilder<RuntimeBaselineContext>();
        optionsBuilder.UseMySql(
            configured.ConnectionString,
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        using var context = new RuntimeBaselineContext(optionsBuilder.Options);

        // Act
        var actual = new MySqlConnectionStringBuilder(
            context.GetService<IRelationalConnection>().DbConnection.ConnectionString);

        // Assert
        Assert.Equal(configured.Server, actual.Server);
        Assert.Equal(configured.Port, actual.Port);
        Assert.Equal(configured.ApplicationName, actual.ApplicationName);
        Assert.Equal(configured.SslMode, actual.SslMode);
        Assert.Equal(configured.SslCa, actual.SslCa);
        Assert.Equal(configured.SslCert, actual.SslCert);
        Assert.Equal(configured.SslKey, actual.SslKey);
        Assert.Equal(configured.LoadBalance, actual.LoadBalance);
        Assert.Equal(configured.MinimumPoolSize, actual.MinimumPoolSize);
        Assert.Equal(configured.MaximumPoolSize, actual.MaximumPoolSize);
        Assert.Equal(configured.ConnectionReset, actual.ConnectionReset);
        Assert.Equal(configured.ConnectionTimeout, actual.ConnectionTimeout);
        Assert.Equal(configured.AllowUserVariables, actual.AllowUserVariables);
    }
}
