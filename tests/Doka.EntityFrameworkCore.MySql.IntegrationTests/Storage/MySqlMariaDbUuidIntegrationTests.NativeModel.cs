namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMariaDbUuidIntegrationTests
{
    /// <summary>Verifies model-managed native UUID values and their seeded relationship.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(NativeTargetCases))]
    public async Task Native_uuid_seeded_graph_round_trips(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateNativeContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));

        var ids = s_seedMembership;
        var query = context.Principals.AsNoTracking().Include(principal => principal.Dependents);

        // Act
        var actual = await query.SingleAsync(
            principal => EF.Parameter(ids).Contains(principal.Id),
            CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.Equal(s_seededId, actual.Id);
        Assert.Equal(s_optionalId, actual.OptionalNativeId);
        Assert.Equal(s_convertedSeedId, actual.ConvertedId);
        Assert.Single(actual.Dependents);
        Assert.Equal(s_seededId, actual.Dependents.Single().PrincipalId);
    }

    /// <summary>Retains model creation, insert, nullable update, and related readback as one lifecycle.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(NativeTargetCases))]
    public async Task Native_uuid_runtime_insert_and_update_round_trip(
        string target
    )
    {
        // Arrange
        var engine = Enum.Parse<IntegrationDatabaseTarget>(target);
        await using var context = CreateNativeContext(engine, "Lifecycle");
        await context.Database.OpenConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        var connection = (MySqlConnection)context.Database.GetDbConnection();
        await DropModelTablesAsync(connection).ConfigureAwait(false);
        var runtime = new NativeUuidPrincipal
        {
            Id = s_runtimeId,
            OptionalNativeId = null,
            BinaryId = Guid.Parse("79cf7ff0-1bb0-4007-8ef4-b345386a6f41"),
            CharId = Guid.Parse("1458f03f-9acb-4902-bca8-280b613a7c92"),
            ConvertedId = s_convertedId,
            Name = "runtime",
        };

        try
        {
            // Act
            await CreateNativeModelAsync(context).ConfigureAwait(false);

            // WHY: Explicit runtime keys need no asynchronous value generation.
            // ReSharper disable once MethodHasAsyncOverload
            context.Principals.Add(runtime);

            // WHY: Explicit dependent keys need no asynchronous value generation.
            // ReSharper disable once MethodHasAsyncOverload
            context.Set<NativeUuidDependent>().Add(new NativeUuidDependent
            {
                Id = 2,
                PrincipalId = s_runtimeId,
                Name = "runtime-related",
            });

            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            runtime.OptionalNativeId = s_optionalId;
            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();
            var actual = await context.Principals.AsNoTracking()
                .Include(principal => principal.Dependents)
                .SingleAsync(principal => principal.Id == s_runtimeId, CancellationToken.None)
                .ConfigureAwait(false);

            // Assert
            Assert.Equal("runtime", actual.Name);
            Assert.Equal(s_optionalId, actual.OptionalNativeId);
            Assert.Equal(s_convertedId, actual.ConvertedId);
            Assert.Single(actual.Dependents);
            Assert.Equal(s_runtimeId, actual.Dependents.Single().PrincipalId);
        }
        finally
        {
            await DropModelTablesAsync(connection).ConfigureAwait(false);
        }
    }

    /// <summary>Verifies a missing native UUID never matches an existing row.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(NativeTargetCases))]
    public async Task Native_uuid_missing_value_does_not_match(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateNativeContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));


        // Act
        var actual = await context.Principals.AnyAsync(
            principal => principal.Id == Guid.Empty,
            CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.False(actual);
    }

    /// <summary>Verifies native UUID LIKE independently of equality and materialization.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(NativeTargetCases))]
    public async Task Native_uuid_like_matches_seeded_value(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateNativeContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));


        // Act
        var actual = await context.Principals.AnyAsync(
            principal => EF.Functions.Like(principal.Id, "bf1da273-%"),
            CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.True(actual);
    }

    /// <summary>Verifies canonical text formatting for native UUID storage.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(NativeTargetCases))]
    public async Task Native_uuid_projects_canonical_text(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateNativeContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));

        var query = context.Principals.Where(principal => principal.Id == s_seededId)
            .Select(principal => principal.Id.ToString());

        // Act
        var actual = await query.SingleAsync(CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.Equal(s_seededId.ToString("D", CultureInfo.InvariantCulture), actual);
    }

    /// <summary>Verifies server-generated native GUID values can be materialized.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(NativeTargetCases))]
    public async Task Native_uuid_generated_value_is_nonempty(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateNativeContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));

        var query = context.Principals.Select(_ => Guid.NewGuid());

        // Act
        var actual = await query.FirstAsync(CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.NotEqual(Guid.Empty, actual);
    }

    /// <summary>Verifies native UUID foreign keys reject a missing principal.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(NativeTargetCases))]
    public async Task Native_uuid_foreign_key_rejects_orphan(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateNativeContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));

        await context.Database.OpenConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        var connection = (MySqlConnection)context.Database.GetDbConnection();
        await using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO `{DependentTable}` "
            + "(`Id`, `PrincipalId`, `Name`) VALUES (3, @id, 'orphan');";
        insert.Parameters.AddWithValue("@id", Guid.Empty.ToString("D", CultureInfo.InvariantCulture));

        // Act
        var failure = await Record.ExceptionAsync(async () =>
            await insert.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false));

        // Assert
        Assert.Equal(1452, Assert.IsType<MySqlException>(failure).Number);
    }

    /// <summary>Verifies native UUID defaults retain explicit binary, text, and converter overrides.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(NativeTargetCases))]
    public async Task Native_uuid_model_preserves_physical_column_types(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateNativeContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));

        await context.Database.OpenConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        var connection = (MySqlConnection)context.Database.GetDbConnection();

        // Act
        var columnTypes = await ReadColumnTypesAsync(connection).ConfigureAwait(false);

        // Assert
        Assert.Equal("uuid", columnTypes["Id"]);
        Assert.Equal("uuid", columnTypes["OptionalNativeId"]);
        Assert.Equal("binary", columnTypes["BinaryId"]);
        Assert.Equal("char", columnTypes["CharId"]);
        Assert.Equal("varbinary", columnTypes["ConvertedId"]);
        Assert.Equal("uuid", columnTypes["PrincipalId"]);
    }
}
