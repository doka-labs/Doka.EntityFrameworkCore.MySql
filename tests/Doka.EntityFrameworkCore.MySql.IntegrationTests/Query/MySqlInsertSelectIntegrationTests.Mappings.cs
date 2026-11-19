namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectIntegrationTests
{
    /// <summary>Reports compatible physical GUID shapes and both explicit representation conversions.</summary>
    public static TheoryData<string, string> GuidCases
    {
        get
        {
            var data = new TheoryData<string, string>();

            foreach (var target in SelectedTargets)
            {
                if (target.StartsWith("MariaDb", StringComparison.Ordinal))
                {
                    data.Add(target, "same");
                    data.Add(target, "text-to-binary");
                    data.Add(target, "binary-to-text");
                    data.Add(target, "native");
                }
                else
                {
                    data.Add(target, "same");
                    data.Add(target, "text-to-binary");
                    data.Add(target, "binary-to-text");
                }
            }

            return data;
        }
    }

    /// <summary>Reports text UUID defaults on every profile and native UUID defaults only where the type exists.</summary>
    public static TheoryData<string, bool> GuidDefaultCases
    {
        get
        {
            var data = new TheoryData<string, bool>();

            foreach (var target in SelectedTargets)
            {
                if (target.StartsWith("MariaDb", StringComparison.Ordinal))
                {
                    data.Add(target, false);
                    data.Add(target, true);
                }
                else
                {
                    data.Add(target, false);
                }
            }

            return data;
        }
    }

    /// <summary>Store GUID representations are copied or explicitly converted without client byte-order roundtrips.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(GuidCases))]
    public async Task Compatible_guid_representations_preserve_the_exact_persisted_value(
        string target,
        string shape
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await using var setup = store.CreateContext();
        setup.Add(new GuidSource
        {
            Id = 1,
            Binary = s_firstGuid,
            Text = s_secondGuid,
            Native = s_firstGuid,
        });

        await setup.SaveChangesAsync(safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var count = await context
            .Set<GuidSource>()
            .ExecuteInsertFromQueryAsync(context.Set<GuidTarget>(),
                setters =>
                {
                    setters
                        .SetProperty(row => row.Binary,
                            row => shape == "text-to-binary"
                                ? row.Text
                                : row.Binary)
                        .SetProperty(row => row.Text,
                            row => shape == "binary-to-text"
                                ? row.Binary
                                : row.Text);

                    if (store.Version.IsMariaDb)
                    {
                        setters.SetProperty(row => row.Native,
                            row => shape == "native"
                                ? row.Text
                                : row.Native);
                    }
                },
                safety.Token);

        await using var readback = store.CreateContext();
        var actual = await readback
            .Set<GuidTarget>()
            .AsNoTracking()
            .SingleAsync(safety.Token);

        var nativeHex = await ReadNativeAsync(store.Observer,
            "SELECT HEX(`Binary`),LOWER(`Text`) FROM InsertGuidTarget;",
            safety.Token);

        // Assert
        var expectedBinary = shape == "text-to-binary"
            ? s_secondGuid
            : s_firstGuid;

        var expectedText = shape == "binary-to-text"
            ? s_firstGuid
            : s_secondGuid;

        Assert.Equal(1, count);
        Assert.Equal(expectedBinary, actual.Binary);
        Assert.Equal(expectedText, actual.Text);
        Assert.Equal([
                expectedBinary
                    .ToString("N", CultureInfo.InvariantCulture)
                    .ToUpperInvariant()
                + "|"
                + expectedText.ToString("D", CultureInfo.InvariantCulture),
            ],
            nativeHex);

        if (store.Version.IsMariaDb)
        {
            Assert.Equal(shape == "native"
                    ? s_secondGuid
                    : s_firstGuid,
                actual.Native);
        }

        Assert.Single(commands.Statements);
        Assert.Equal(0, commands.Readers);
    }

    /// <summary>UUID defaults generate distinct persisted values for every inserted row without generated-ID readback.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(GuidDefaultCases))]
    public async Task Uuid_server_default_runs_per_inserted_row_and_is_not_client_generated(
        string target,
        bool nativeUuid
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var count = nativeUuid
            ? await context
                .Set<InsertSource>()
                .ExecuteInsertFromQueryAsync(context.Set<NativeGuidDefaultTarget>(),
                    setters => setters
                        .SetProperty(row => row.Value, row => row.Value),
                    safety.Token)
            : await context
                .Set<InsertSource>()
                .ExecuteInsertFromQueryAsync(context.Set<GuidDefaultTarget>(),
                    setters => setters
                        .SetProperty(row => row.Value, row => row.Value),
                    safety.Token);

        var native = await ReadNativeAsync(store.Observer,
            $"SELECT `Id`,`Value` FROM `{(nativeUuid ? "InsertNativeGuidDefault" : "InsertGuidDefault")}` ORDER BY `Value`;",
            safety.Token);

        // Assert
        Assert.Equal(3, count);
        Assert.Equal(3, native.Length);
        var ids = native
            .Select(row => Guid.Parse(row
                .Split('|')[0]))
            .ToArray();

        Assert.All(ids, id => Assert.NotEqual(Guid.Empty, id));
        Assert.Equal(3,
            ids
                .Distinct()
                .Count());

        Assert.Equal(["10", "20", "30"],
            native.Select(row => row
                .Split('|')[1]));

        Assert.Single(commands.Statements);
        Assert.Equal(0, commands.Readers);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Shadow and field-only target selectors use actual mapped physical column identities.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(EmptyCases))]
    public async Task Shadow_and_field_only_assignments_write_mapped_columns(
        string target,
        bool field
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var source = context
            .Set<InsertSource>()
            .Where(row => row.Id <= 2);

        // Act
        var count = field
            ? await source.ExecuteInsertFromQueryAsync(context.Set<FieldTarget>(),
                setters => setters
                    .SetProperty(row => EF.Property<string>(row, "_payload"), row => row.Label),
                safety.Token)
            : await source.ExecuteInsertFromQueryAsync(context.Set<ShadowTarget>(),
                setters => setters
                    .SetProperty(row => EF.Property<string>(row, "Payload"), row => row.Label),
                safety.Token);

        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT `Payload` FROM `{(field ? "InsertField" : "InsertShadow")}` ORDER BY `Payload`;",
            safety.Token);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(["alpha", "beta"], actual);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>An explicitly writable key is assigned from SQL instead of generated or silently omitted.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Explicit_writable_key_uses_the_selected_physical_column(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();

        // Act
        var count = await context
            .Set<InsertSource>()
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Id, row => row.Id + 100)
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => row.Label),
                safety.Token);

        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT `Id`,`Value` FROM `{TargetTable}` ORDER BY `Id`;",
            safety.Token);

        // Assert
        Assert.Equal(3, count);
        Assert.Equal(["101|10", "102|20", "103|30"], actual);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A complete one-table derived target uses its model discriminator without losing values.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task One_table_tph_target_gets_its_actual_model_discriminator(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();

        // Act
        var count = await context
            .Set<InsertSource>()
            .Where(row => row.Id <= 2)
            .ExecuteInsertFromQueryAsync(context.Set<SpecialTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => row.Label),
                safety.Token);

        var native = await ReadNativeAsync(store.Observer,
            "SELECT `Kind`,`Value`,`Code` FROM InsertTph ORDER BY `Value`;",
            safety.Token);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(["special|10|alpha", "special|20|beta"], native);
    }

    /// <summary>Attempting to override a discriminator is rejected without writing the wrong entity kind.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Explicit_tph_discriminator_is_rejected_without_wrong_kind_rows(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => context
            .Set<InsertSource>()
            .ExecuteInsertFromQueryAsync(context.Set<SpecialTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => row.Label)
                    .SetProperty(row => EF.Property<string>(row, "Kind"), row => "base"),
                safety.Token));

        var rows = await ReadNativeAsync(store.Observer, "SELECT COUNT(*) FROM InsertTph;", safety.Token);

        // Assert
        Assert.Contains("discriminator", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["0"], rows);
        Assert.Empty(commands.Statements);
    }

    /// <summary>Copies converted store values exactly once along with nullable and precision-sensitive facets.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Enum_custom_converter_nullable_decimal_temporal_and_text_facets_roundtrip(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var moment = new DateTime(2026,
            11,
            17,
            12,
            34,
            56,
            DateTimeKind.Unspecified).AddTicks(1234560);

        await using var setup = store.CreateContext();
        setup.AddRange(new FacetSource
            {
                Id = 1,
                Status = InsertStatus.Approved,
                Code = new InsertCode(7),
                Amount = 1234.56m,
                Moment = moment,
                Optional = null,
                Text = "'\\\"\0\u00e9\ud83d\ude00",
            },
            new FacetSource
            {
                Id = 2,
                Status = InsertStatus.Rejected,
                Code = new InsertCode(-2),
                Amount = -0.25m,
                Moment = moment.AddDays(1),
                Optional = 42,
                Text = "second",
            });

        await setup.SaveChangesAsync(safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var count = await context
            .Set<FacetSource>()
            .ExecuteInsertFromQueryAsync(context.Set<FacetTarget>(),
                setters => setters
                    .SetProperty(row => row.Status, row => row.Status)
                    .SetProperty(row => row.Code, row => row.Code)
                    .SetProperty(row => row.Amount, row => row.Amount)
                    .SetProperty(row => row.Moment, row => row.Moment)
                    .SetProperty(row => row.Optional, row => row.Optional)
                    .SetProperty(row => row.Text, row => row.Text),
                safety.Token);

        var actual = await context
            .Set<FacetTarget>()
            .AsNoTracking()
            .OrderByDescending(row => row.Amount)
            .ToArrayAsync(safety.Token);

        var raw = await ReadNativeAsync(store.Observer,
            "SELECT `Status`,`Code` FROM InsertFacetTarget ORDER BY `Amount` DESC;",
            safety.Token);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(["Approved|107", "Rejected|98"], raw);
        Assert.Collection(actual,
            row =>
            {
                Assert.Equal(InsertStatus.Approved, row.Status);
                Assert.Equal(new InsertCode(7), row.Code);
                Assert.Equal(1234.56m, row.Amount);
                Assert.Equal(moment, row.Moment);
                Assert.Null(row.Optional);
                Assert.Equal("'\\\"\0\u00e9\ud83d\ude00", row.Text);
            },
            row =>
            {
                Assert.Equal(InsertStatus.Rejected, row.Status);
                Assert.Equal(new InsertCode(-2), row.Code);
                Assert.Equal(-0.25m, row.Amount);
                Assert.Equal(moment.AddDays(1), row.Moment);
                Assert.Equal(42, row.Optional);
                Assert.Equal("second", row.Text);
            });

        Assert.Single(commands.Statements);
    }

    /// <summary>Incompatible custom converter representations fail instead of silently shifting a valid CLR value.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Different_custom_converter_representation_is_rejected_before_write(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => context
            .Set<FacetSource>()
            .ExecuteInsertFromQueryAsync(context.Set<IncompatibleTarget>(),
                setters => setters
                    .SetProperty(row => row.Code, row => row.Code),
                safety.Token));

        var native = await ReadNativeAsync(store.Observer, "SELECT COUNT(*) FROM InsertIncompatible;", safety.Token);

        // Assert
        Assert.Contains("different CLR converter representations", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["0"], native);
        Assert.Empty(commands.Statements);
    }

    /// <summary>Uses the same converter expressions for compatible source/target mappings.</summary>
    private static void ConfigureFacetModel(
        ModelBuilder modelBuilder
    )
    {
        ConfigureFacet(modelBuilder
            .Entity<FacetSource>()
            .ToTable("InsertFacetSource"));

        ConfigureFacet(modelBuilder
            .Entity<FacetTarget>()
            .ToTable("InsertFacetTarget"));

        modelBuilder
            .Entity<FacetSource>()
            .Property(row => row.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<IncompatibleTarget>(entity =>
        {
            entity
                .ToTable("InsertIncompatible")
                .UseStorageEngine("InnoDB");

            entity
                .Property(row => row.Code)
                .HasConversion(value => value.Value + 200, value => new InsertCode(value - 200));
        });

        modelBuilder.Entity<TemporalRangeTarget>(entity =>
        {
            entity
                .ToTable("InsertTemporalRange")
                .UseStorageEngine("InnoDB");

            entity
                .Property(row => row.Id)
                .ValueGeneratedNever();

            entity
                .Property(row => row.Moment)
                .HasPrecision(6)
                .IsRequired();
        });
    }

    /// <summary>Configures only the shared scalar facets, without changing entity inheritance semantics.</summary>
    private static void ConfigureFacet<T>(
        EntityTypeBuilder<T> entity
    )
        where T : class, IFacetRow
    {
        entity.UseStorageEngine("InnoDB");
        entity
            .Property(row => row.Status)
            .HasConversion<string>()
            .HasMaxLength(24);

        entity
            .Property(row => row.Code)
            .HasConversion(value => value.Value + 100, value => new InsertCode(value - 100));

        entity
            .Property(row => row.Amount)
            .HasPrecision(12, 2);

        entity
            .Property(row => row.Moment)
            .HasPrecision(6);

        entity
            .Property(row => row.Text)
            .HasMaxLength(100);
    }

    private enum InsertStatus { Approved, Rejected }

    private readonly record struct InsertCode(int Value);

    private sealed class TemporalRangeTarget
    {
        public int Id { get; set; }

        public DateTime Moment { get; set; }
    }

    private interface IFacetRow
    {
        InsertStatus Status { get; set; }
        InsertCode Code { get; set; }
        decimal Amount { get; set; }
        DateTime Moment { get; set; }
        int? Optional { get; set; }
        string Text { get; set; }
    }

    private sealed class FacetSource : IFacetRow
    {
        public int Id { get; set; }

        public InsertStatus Status { get; set; }

        public InsertCode Code { get; set; }

        public decimal Amount { get; set; }

        public DateTime Moment { get; set; }

        public int? Optional { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    private sealed class FacetTarget : IFacetRow
    {
        public int Id { get; set; }

        public InsertStatus Status { get; set; }

        public InsertCode Code { get; set; }

        public decimal Amount { get; set; }

        public DateTime Moment { get; set; }

        public int? Optional { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    private sealed class IncompatibleTarget
    {
        public int Id { get; set; }

        public InsertCode Code { get; set; }
    }
}
