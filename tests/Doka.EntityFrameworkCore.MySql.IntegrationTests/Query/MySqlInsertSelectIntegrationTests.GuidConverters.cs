namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectIntegrationTests
{
    /// <summary>Reports each genuinely compatible custom GUID store representation independently.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> CustomGuidCases => SelectedTargets.SelectMany(target
        => new[]
        {
            new TheoryDataRow<string, string>(target.Data, "char32"),
            new TheoryDataRow<string, string>(target.Data, "little-endian"),
        });

    /// <summary>Matching custom GUID converters copy their actual store values without applying another CLR conversion.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CustomGuidCases))]
    public async Task Equivalent_custom_guid_converters_preserve_the_exact_store_representation(
        string target,
        string format
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await using var setup = store.CreateContext();
        setup.Add(new CustomGuidSource
        {
            Id = 1,
            N = s_firstGuid,
            LittleEndian = s_secondGuid,
        });

        await setup.SaveChangesAsync(safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var count = format == "char32"
            ? await context
                .Set<CustomGuidSource>()
                .ExecuteInsertFromQueryAsync(context.Set<NFormatGuidTarget>(),
                    setters => setters
                        .SetProperty(row => row.Value, row => row.N),
                    safety.Token)
            : await context
                .Set<CustomGuidSource>()
                .ExecuteInsertFromQueryAsync(context.Set<LittleEndianGuidTarget>(),
                    setters => setters
                        .SetProperty(row => row.Value, row => row.LittleEndian),
                    safety.Token);

        var persisted = await ReadNativeAsync(store.Observer,
            format == "char32"
                ? "SELECT `Value` FROM InsertGuidN;"
                : "SELECT HEX(`Value`) FROM InsertGuidLittle;",
            safety.Token);

        await using var readback = store.CreateContext();
        var actual = format == "char32"
            ? await readback
                .Set<NFormatGuidTarget>()
                .AsNoTracking()
                .Select(row => row.Value)
                .SingleAsync(safety.Token)
            : await readback
                .Set<LittleEndianGuidTarget>()
                .AsNoTracking()
                .Select(row => row.Value)
                .SingleAsync(safety.Token);

        // Assert
        Assert.Equal(1, count);
        Assert.Equal(format == "char32"
                ? s_firstGuid
                : s_secondGuid,
            actual);

        Assert.Equal([
                format == "char32"
                    ? s_firstGuid.ToString("N", CultureInfo.InvariantCulture)
                    : Convert.ToHexString(s_secondGuid.ToByteArray()),
            ],
            persisted);

        Assert.Single(commands.Statements);
        Assert.Equal(0, commands.Readers);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Identical CLR GUIDs with a different byte-order converter cannot silently become another persisted identity.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Incompatible_custom_guid_byte_order_is_rejected_before_write(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await using var setup = store.CreateContext();
        setup.Add(new CustomGuidSource
        {
            Id = 1,
            N = s_firstGuid,
            LittleEndian = s_secondGuid,
        });

        await setup.SaveChangesAsync(safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => context
            .Set<CustomGuidSource>()
            .ExecuteInsertFromQueryAsync(context.Set<CanonicalGuidTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.LittleEndian),
                safety.Token));

        var persisted =
            await ReadNativeAsync(store.Observer, "SELECT COUNT(*) FROM InsertGuidCanonical;", safety.Token);

        // Assert
        Assert.Contains("GUID conversion requires", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["0"], persisted);
        Assert.Empty(commands.Statements);
        Assert.Equal(0, commands.Readers);
    }

    /// <summary>Configures deliberate noncanonical formats so accidental canonicalization is observable in the persisted bytes.</summary>
    private static void ConfigureCustomGuidModel(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<CustomGuidSource>(entity =>
        {
            entity
                .ToTable("InsertCustomGuidSource")
                .UseStorageEngine("InnoDB");

            entity
                .Property(row => row.Id)
                .ValueGeneratedNever();

            ConfigureNFormat(entity.Property(row => row.N));
            ConfigureLittleEndian(entity.Property(row => row.LittleEndian));
        });

        modelBuilder.Entity<NFormatGuidTarget>(entity =>
        {
            entity
                .ToTable("InsertGuidN")
                .UseStorageEngine("InnoDB");

            ConfigureNFormat(entity.Property(row => row.Value));
        });

        modelBuilder.Entity<LittleEndianGuidTarget>(entity =>
        {
            entity
                .ToTable("InsertGuidLittle")
                .UseStorageEngine("InnoDB");

            ConfigureLittleEndian(entity.Property(row => row.Value));
        });

        modelBuilder.Entity<CanonicalGuidTarget>(entity =>
        {
            entity
                .ToTable("InsertGuidCanonical")
                .UseStorageEngine("InnoDB");

            entity
                .Property(row => row.Value)
                .HasColumnType("binary(16)");
        });
    }

    /// <summary>Uses one expression pair on source and target to qualify an actual compatible converter.</summary>
    private static void ConfigureNFormat(
        PropertyBuilder<Guid> property
    ) => property
        .HasColumnType("char(32)")
        .HasConversion(value => value.ToString("N", CultureInfo.InvariantCulture),
            value => Guid.ParseExact(value, "N"));

    /// <summary>Preserves the custom CLR byte-array layout rather than assuming RFC network-order binary storage.</summary>
    private static void ConfigureLittleEndian(
        PropertyBuilder<Guid> property
    ) => property
        .HasColumnType("binary(16)")
        .HasConversion(value => value.ToByteArray(), value => new Guid(value));

    private sealed class CustomGuidSource
    {
        public int Id { get; set; }

        public Guid N { get; set; }

        public Guid LittleEndian { get; set; }
    }

    private sealed class NFormatGuidTarget
    {
        public int Id { get; set; }

        public Guid Value { get; set; }
    }

    private sealed class LittleEndianGuidTarget
    {
        public int Id { get; set; }

        public Guid Value { get; set; }
    }

    private sealed class CanonicalGuidTarget
    {
        public int Id { get; set; }

        public Guid Value { get; set; }
    }
}
