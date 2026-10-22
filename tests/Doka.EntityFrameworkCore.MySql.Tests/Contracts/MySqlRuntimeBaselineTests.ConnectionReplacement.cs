namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlRuntimeBaselineTests
{
    /// <summary>
    /// Verifies that provider-created connections use one connector transport
    /// format independently of the provider's column-level GUID default.
    /// </summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public void Relational_connection_uses_binary16_connector_transport(
        MySqlGuidFormat providerFormat
    )
    {
        // Arrange
        var optionsBuilder = new DbContextOptionsBuilder<RuntimeBaselineContext>();
        optionsBuilder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.DefaultGuidFormat(providerFormat));
        using var context = new RuntimeBaselineContext(optionsBuilder.Options);
        var relationalConnection = context.GetService<IRelationalConnection>();

        // Act
        var builder = new MySqlConnectionStringBuilder(relationalConnection.DbConnection.ConnectionString);

        // Assert
        Assert.Equal(MySqlConnector.MySqlGuidFormat.Binary16, builder.GuidFormat);
    }

    /// <summary>
    /// Verifies that EF runtime string replacement retains EF's raw public
    /// value while the physical provider connection receives every invariant.
    /// </summary>
    [Fact]
    public void Runtime_connection_string_replacement_is_normalized_before_connection_use()
    {
        // Arrange
        const string replacement =
            "Server=replacement;Database=runtime;User ID=provider;Password=secret;";
        using var context = new RuntimeBaselineContext(
            CreateOptions(
                "Server=initial;Database=doka;User ID=provider;Password=secret;",
                options => options.RequireUserVariables()));

        // Act
        context.Database.SetConnectionString(replacement);
        var physical = new MySqlConnectionStringBuilder(
            context.Database.GetDbConnection().ConnectionString);

        var actual = context.Database.GetConnectionString();

        // Assert
        Assert.Equal(replacement, actual);
        Assert.Equal("replacement", physical.Server);
        Assert.Equal(MySqlConnector.MySqlGuidFormat.Binary16, physical.GuidFormat);
        Assert.False(physical.UseAffectedRows);
        Assert.True(physical.AllowUserVariables);
    }

    /// <summary>
    /// Verifies that a rejected runtime string leaves the active connection
    /// configuration unchanged.
    /// </summary>
    [Theory]
    [InlineData("UseAffectedRows=true")]
    [InlineData("GuidFormat=Char36")]
    [InlineData("AllowUserVariables=false")]
    public void Runtime_connection_string_replacement_rejects_incompatible_configuration_atomically(
        string incompatibleOption
    )
    {
        // Arrange
        using var context = new RuntimeBaselineContext(
            CreateOptions(
                "Server=initial;Database=doka;User ID=provider;Password=secret;",
                options => options.RequireUserVariables()));

        var originalConnection = context.Database.GetDbConnection();
        var originalConnectionString = originalConnection.ConnectionString;
        Action act = () =>
            context.Database.SetConnectionString(
                $"Server=replacement;Database=runtime;Password=secret-canary;{incompatibleOption};");

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<MySqlConnectionContractException>(recordedException);
        Assert.Same(originalConnection, context.Database.GetDbConnection());
        Assert.Equal(originalConnectionString, originalConnection.ConnectionString);
    }

    /// <summary>
    /// Verifies that runtime borrowed connections are validated before EF
    /// replaces the active provider-owned connection.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Runtime_db_connection_replacement_accepts_the_exact_compatible_instance(
        bool open,
        bool contextOwnsConnection
    )
    {
        // Arrange
        using var context = new RuntimeBaselineContext(
            CreateOptions(
                "Server=initial;Database=doka;User ID=provider;Password=secret;",
                options => options.RequireUserVariables()));

        using var replacement = new RecordingDbConnection
        {
            ConnectionString =
                "Server=replacement;Database=doka;GuidFormat=Binary16;AllowUserVariables=true;",
        };

        if (open)
        {
            replacement.Open();
        }

        // Act
        context.Database.SetDbConnection(replacement, contextOwnsConnection);
        var actual = context.Database.GetDbConnection();

        // Assert
        Assert.Same(replacement, actual);
        Assert.Equal(
            open ? ConnectionState.Open : ConnectionState.Closed,
            replacement.State);
    }

    /// <summary>
    /// Verifies that a successful transition to a borrowed connection prevents
    /// a later string replacement from mutating that object indirectly.
    /// </summary>
    [Fact]
    public void Runtime_db_connection_transition_marks_the_active_path_as_borrowed()
    {
        // Arrange
        using var context = new RuntimeBaselineContext(
            CreateOptions("Server=initial;Database=doka;User ID=provider;Password=secret;"));

        using var replacement = new RecordingDbConnection();
        context.Database.SetDbConnection(replacement, contextOwnsConnection: false);
        Action act = () =>
            context.Database.SetConnectionString(
                "Server=second;Database=runtime;GuidFormat=Binary16;");

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Same(replacement, context.Database.GetDbConnection());
        Assert.Equal(
            "Server=localhost;Database=doka;GuidFormat=Binary16;",
            replacement.ConnectionString);
        Assert.Equal(ConnectionState.Closed, replacement.State);
        Assert.False(replacement.IsDisposed);
    }

    /// <summary>
    /// Verifies that an incompatible runtime borrowed connection is rejected
    /// before it replaces or changes the current connection.
    /// </summary>
    [Theory]
    [InlineData("GuidFormat=Default;")]
    [InlineData("GuidFormat=Char36;")]
    [InlineData("GuidFormat=LittleEndianBinary16;")]
    [InlineData("GuidFormat=TimeSwapBinary16;")]
    [InlineData("GuidFormat=None;")]
    [InlineData("GuidFormat=Binary16;UseAffectedRows=true;")]
    [InlineData("GuidFormat=Binary16;AllowUserVariables=false;")]
    public void Runtime_db_connection_replacement_rejects_incompatible_input_atomically(
        string options
    )
    {
        // Arrange
        using var context = new RuntimeBaselineContext(
            CreateOptions(
                "Server=initial;Database=doka;User ID=provider;Password=secret;",
                providerOptions => providerOptions.RequireUserVariables()));

        var originalConnection = context.Database.GetDbConnection();
        using var replacement = new RecordingDbConnection
        {
            ConnectionString =
                $"Server=replacement;Database=doka;Password=secret-canary;{options}",
        };

        Action act = () =>
            context.Database.SetDbConnection(replacement, contextOwnsConnection: false);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<MySqlConnectionContractException>(recordedException);
        Assert.Same(originalConnection, context.Database.GetDbConnection());
        Assert.Equal(ConnectionState.Closed, replacement.State);
        Assert.False(replacement.IsDisposed);
    }

    /// <summary>
    /// Verifies that validation failure is independent of connection state and
    /// EF disposal ownership and leaves the supplied object untouched.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Runtime_db_connection_rejection_preserves_state_and_ownership(
        bool open,
        bool contextOwnsConnection
    )
    {
        // Arrange
        using var context = new RuntimeBaselineContext(
            CreateOptions("Server=initial;Database=doka;User ID=provider;Password=secret;"));

        using var replacement = new RecordingDbConnection
        {
            ConnectionString =
                "Server=replacement;Database=doka;GuidFormat=Char36;",
        };

        if (open)
        {
            replacement.Open();
        }

        var originalConnectionString = replacement.ConnectionString;
        var originalConnection = context.Database.GetDbConnection();
        Action act = () =>
            context.Database.SetDbConnection(replacement, contextOwnsConnection);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<MySqlConnectionContractException>(recordedException);
        Assert.Same(originalConnection, context.Database.GetDbConnection());
        Assert.Equal(originalConnectionString, replacement.ConnectionString);
        Assert.Equal(
            open ? ConnectionState.Open : ConnectionState.Closed,
            replacement.State);
        Assert.False(replacement.IsDisposed);
    }

    /// <summary>
    /// Verifies that EF string replacement cannot mutate a borrowed connection
    /// path behind its owner's back.
    /// </summary>
    [Fact]
    public void Runtime_connection_string_replacement_is_rejected_for_borrowed_connection()
    {
        // Arrange
        using var connection = new RecordingDbConnection();
        var originalConnectionString = connection.ConnectionString;
        using var context = new RuntimeBaselineContext(CreateOptions(connection));
        Action act = () =>
            context.Database.SetConnectionString(
                "Server=replacement;Database=runtime;GuidFormat=Binary16;");

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Equal(
            "Database.SetConnectionString cannot replace caller-owned MySQL connection configuration.",
            exception.Message);
        Assert.Same(connection, context.Database.GetDbConnection());
        Assert.Equal(originalConnectionString, connection.ConnectionString);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>
    /// Verifies that EF string replacement cannot bypass a caller-owned data
    /// source or reconstruct it from serializable options.
    /// </summary>
    [Fact]
    public void Runtime_connection_string_replacement_is_rejected_for_borrowed_data_source()
    {
        // Arrange
        using var dataSource = new MySqlDataSourceBuilder(
            "Server=localhost;Database=doka;GuidFormat=Binary16;").Build();

        var builder = new DbContextOptionsBuilder<RuntimeBaselineContext>();
        builder.UseMySql(
            dataSource,
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        using var context = new RuntimeBaselineContext(builder.Options);
        var originalConnection = context.Database.GetDbConnection();
        Action act = () =>
            context.Database.SetConnectionString(
                "Server=replacement;Database=runtime;GuidFormat=Binary16;");

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Same(originalConnection, context.Database.GetDbConnection());
        Assert.Equal(ConnectionState.Closed, originalConnection.State);
        Assert.Equal(
            MySqlConnector.MySqlGuidFormat.Binary16,
            new MySqlConnectionStringBuilder(dataSource.ConnectionString).GuidFormat);
    }
}
