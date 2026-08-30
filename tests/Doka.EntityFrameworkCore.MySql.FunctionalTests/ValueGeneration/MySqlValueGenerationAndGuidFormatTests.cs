using System.Data;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the value-generation and GUID-format baseline.
/// </summary>
public sealed class MySqlValueGenerationAndGuidFormatTests
{
    /// <summary>
    /// Verifies the conventional value-generation strategies for integer and GUID keys.
    /// </summary>
    [Fact]
    public void Model_defaults_integer_keys_to_autoincrement_and_guid_keys_to_client_generation()
    {
        using var context = new ValueGenerationContext(CreateOptions());
        var intKeyProperty = context.Model.FindEntityType(typeof(IntKeyEntity))!.FindProperty(nameof(IntKeyEntity.Id))!;
        var guidKeyProperty =
            context.Model.FindEntityType(typeof(GuidKeyEntity))!.FindProperty(nameof(GuidKeyEntity.Id))!;

        Assert.Equal(MySqlValueGenerationStrategy.AutoIncrement, intKeyProperty.GetMySqlValueGenerationStrategy());
        Assert.Equal(ValueGenerated.OnAdd, intKeyProperty.ValueGenerated);
        Assert.Equal(MySqlValueGenerationStrategy.ClientGuid, guidKeyProperty.GetMySqlValueGenerationStrategy());
        Assert.Equal(ValueGenerated.OnAdd, guidKeyProperty.ValueGenerated);
    }

    /// <summary>
    /// Verifies that the configured default GUID format flows into the model store type.
    /// </summary>
    [Fact]
    public void Default_guid_format_char36_applies_char36_store_type()
    {
        using var context = new ValueGenerationContext(CreateOptions(MySqlGuidFormat.Char36));
        var guidKeyProperty =
            context.Model.FindEntityType(typeof(GuidKeyEntity))!.FindProperty(nameof(GuidKeyEntity.Id))!;

        Assert.Equal(MySqlGuidFormat.Char36, guidKeyProperty.GetMySqlGuidFormat());
        Assert.Equal("char(36)", guidKeyProperty.GetColumnType());
        Assert.Equal(36, guidKeyProperty.GetMaxLength());
        Assert.True(guidKeyProperty.IsFixedLength());
    }

    /// <summary>
    /// The Char36 mapping accepts both runtime shapes documented by MySqlConnector:
    /// a Guid when GuidFormat is Char36 and a string when automatic Guid conversion
    /// is disabled. Unsupported shapes remain a focused provider error.
    /// </summary>
    [Fact]
    public void Default_char36_reader_accepts_guid_and_string_but_rejects_binary_values()
    {
        using var context = new ValueGenerationContext(CreateOptions(MySqlGuidFormat.Char36));
        var property = context.Model
            .FindEntityType(typeof(GuidKeyEntity))!
            .FindProperty(nameof(GuidKeyEntity.Id))!;

        var mapping = property.GetRelationalTypeMapping();
        var value = Guid.Parse("9d54ead7-e69c-4939-9290-8e95586996de");

        Assert.Equal(value.ToString("D", CultureInfo.InvariantCulture), ReadProviderValue(mapping, value));
        Assert.Equal(value.ToString("D", CultureInfo.InvariantCulture), ReadProviderValue(
            mapping,
            value.ToString("D", CultureInfo.InvariantCulture)));

        var exception = Assert.Throws<InvalidOperationException>(
            () => ReadProviderValue(mapping, value.ToByteArray(bigEndian: true)));

        Assert.Contains("System.Byte[]", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(value.ToByteArray()), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_client_guid_generation_assigns_values_when_entities_are_tracked()
    {
        using var context = new ValueGenerationContext(CreateOptions());
        var generatedEntity = new GeneratedGuidEntity();
        var nonGeneratedEntity = new GuidKeyEntity();

        context.Add(generatedEntity);
        context.Add(nonGeneratedEntity);

        Assert.NotEqual(Guid.Empty, generatedEntity.Id);
        Assert.NotEqual(Guid.Empty, nonGeneratedEntity.Id);
        Assert.Equal(7, nonGeneratedEntity.Id.Version);
    }

    /// <summary>
    /// Explicit EF Core OnAdd configuration for a Guid key selects the
    /// provider's client Guid generator because neither target engine has a
    /// native AUTO_INCREMENT equivalent for Guid values.
    /// </summary>
    [Fact]
    public void Explicit_OnAdd_guid_generation_uses_client_generator()
    {
        using var context = new ValueGenerationContext(CreateOptions());
        var entity = new ExplicitGeneratedGuidEntity();
        var property = context.Model
            .FindEntityType(typeof(ExplicitGeneratedGuidEntity))!
            .FindProperty(nameof(ExplicitGeneratedGuidEntity.Id))!;

        context.Add(entity);

        Assert.Equal(
            MySqlValueGenerationStrategy.ClientGuid,
            property.GetMySqlValueGenerationStrategy());
        Assert.NotEqual(Guid.Empty, entity.Id);
    }

    /// <summary>
    /// Verifies that explicit property-level CHAR(36) compatibility configuration remains available.
    /// </summary>
    [Fact]
    public void Explicit_property_level_char36_configuration_remains_available()
    {
        using var context = new ValueGenerationContext(CreateOptions());
        var property =
            context.Model.FindEntityType(typeof(ExplicitChar36Entity))!.FindProperty(nameof(ExplicitChar36Entity.Id))!;

        Assert.Equal(MySqlGuidFormat.Char36, property.GetMySqlGuidFormat());
        Assert.Equal("char(36)", property.GetColumnType());
        Assert.Null(property.GetValueConverter());
        Assert.Null(property.GetProviderClrType());
        Assert.IsType<GuidToStringConverter>(property.GetRelationalTypeMapping().Converter);
        Assert.Equal(MySqlValueGenerationStrategy.ClientGuid, property.GetMySqlValueGenerationStrategy());
        Assert.Equal(ValueGenerated.OnAdd, property.ValueGenerated);
    }

    /// <summary>
    /// An explicit Binary16 property remains on the connector-native Guid path
    /// even when the context default selects Char36 columns.
    /// </summary>
    [Fact]
    public void Explicit_binary16_override_remains_native_and_converter_free()
    {
        using var context = new ValueGenerationContext(CreateOptions(MySqlGuidFormat.Char36));
        var property = context.Model
            .FindEntityType(typeof(ExplicitBinary16Entity))!
            .FindProperty(nameof(ExplicitBinary16Entity.Id))!;

        var value = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        Assert.Null(property.GetValueConverter());
        Assert.Null(property.GetProviderClrType());
        Assert.Equal("binary(16)", property.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value);
        Assert.Equal("binary(16)", property.GetColumnType());
        Assert.IsType<MySqlGuidBinaryTypeMapping>(property.GetRelationalTypeMapping());
        Assert.Equal(
            "X'00112233445566778899AABBCCDDEEFF'",
            property.GetRelationalTypeMapping().GenerateSqlLiteral(value));
    }

    /// <summary>
    /// The unannotated provider default retains the native Binary16 fast path.
    /// </summary>
    [Fact]
    public void Default_binary16_mapping_remains_native_and_converter_free()
    {
        using var context = new ValueGenerationContext(CreateOptions());
        var property = context.Model
            .FindEntityType(typeof(GuidKeyEntity))!
            .FindProperty(nameof(GuidKeyEntity.Id))!;

        Assert.Equal(MySqlGuidFormat.Binary16, property.GetMySqlGuidFormat());
        Assert.Equal("binary(16)", property.GetColumnType());
        Assert.Null(property.GetProviderClrType());
        Assert.Null(property.GetValueConverter());
        Assert.IsType<MySqlGuidBinaryTypeMapping>(property.GetRelationalTypeMapping());
    }

    /// <summary>
    /// An explicit Binary16 declaration that matches the context default remains
    /// on the connector-native Guid path instead of adding a redundant converter.
    /// </summary>
    [Fact]
    public void Explicit_binary16_matching_context_default_remains_native_and_converter_free()
    {
        using var context = new ValueGenerationContext(CreateOptions());
        var property = context.Model
            .FindEntityType(typeof(ExplicitBinary16Entity))!
            .FindProperty(nameof(ExplicitBinary16Entity.Id))!;

        Assert.Equal(MySqlGuidFormat.Binary16, property.GetMySqlGuidFormat());
        Assert.Equal("binary(16)", property.GetColumnType());
        Assert.Null(property.GetProviderClrType());
        Assert.Null(property.GetValueConverter());
        Assert.IsType<MySqlGuidBinaryTypeMapping>(property.GetRelationalTypeMapping());
    }

    /// <summary>
    /// Verifies that a user-configured Guid converter takes precedence over the
    /// provider's default binary Guid representation.
    /// </summary>
    [Fact]
    public void Explicit_guid_to_string_converter_is_preserved()
    {
        using var context = new ValueGenerationContext(CreateOptions());
        var property = context.Model
            .FindEntityType(typeof(ConvertedGuidEntity))!
            .FindProperty(nameof(ConvertedGuidEntity.Id))!;

        Assert.Equal(typeof(string), property.GetProviderClrType());
        Assert.Equal("varchar(36)", property.GetColumnType());
        Assert.Null(property.GetMySqlGuidFormat());
    }

    /// <summary>
    /// Verifies that an implicit Guid-to-byte converter avoids the driver's
    /// Binary16 Guid materialization path and remains readable as byte[].
    /// </summary>
    [Fact]
    public void Explicit_guid_to_bytes_converter_uses_driver_safe_store_type()
    {
        using var context = new ValueGenerationContext(CreateOptions());
        var property = context.Model
            .FindEntityType(typeof(ConvertedGuidBytesEntity))!
            .FindProperty(nameof(ConvertedGuidBytesEntity.Id))!;

        Assert.Equal(typeof(byte[]), property.GetProviderClrType());
        Assert.Equal("varbinary(17)", property.GetColumnType());
        Assert.Null(property.GetMySqlGuidFormat());
    }

    /// <summary>
    /// Verifies that unsupported GUID-format values fail with a format-specific diagnostic.
    /// </summary>
    [Fact]
    public void Invalid_guid_format_throws_with_the_format_parameter_name()
    {
        using var context = new InvalidGuidFormatContext(CreateOptions<InvalidGuidFormatContext>());

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => _ = context.Model);

        Assert.Equal("format", exception.ParamName);
        Assert.Contains("Unsupported MySqlGuidFormat value", exception.Message, StringComparison.Ordinal);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlGuidFormat defaultGuidFormat = MySqlGuidFormat.Binary16
    )
        where TContext : DbContext
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>();

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.DefaultGuidFormat(defaultGuidFormat));

        return builder.Options;
    }

    private static DbContextOptions<ValueGenerationContext> CreateOptions(
        MySqlGuidFormat defaultGuidFormat = MySqlGuidFormat.Binary16
    )
    {
        return CreateOptions<ValueGenerationContext>(defaultGuidFormat);
    }

    private static string ReadProviderValue(
        RelationalTypeMapping mapping,
        object value
    )
    {
        var table = new DataTable();
        table.Columns.Add("Value", value.GetType());
        table.Rows.Add(value);

        using var reader = table.CreateDataReader();

        Assert.True(reader.Read());

        var readerExpression = Expression.Parameter(typeof(DbDataReader), "reader");
        var ordinalExpression = Expression.Constant(0);
        var getStringExpression = Expression.Call(
            readerExpression,
            RelationalTypeMapping.GetDataReaderMethod(typeof(string)),
            ordinalExpression);

        var customizedExpression = mapping.CustomizeDataReaderExpression(getStringExpression);
        var materialize = Expression
            .Lambda<Func<DbDataReader, string>>(customizedExpression, readerExpression)
            .Compile();

        return materialize(reader);
    }

    private sealed class ValueGenerationContext : DbContext
    {
        public ValueGenerationContext(
            DbContextOptions<ValueGenerationContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<IntKeyEntity>(entity =>
            {
                entity.ToTable("Phase2IntKeyEntities");
                entity.HasKey(item => item.Id);
            });

            modelBuilder.Entity<GuidKeyEntity>(entity =>
            {
                entity.ToTable("Phase2GuidKeyEntities");
                entity.HasKey(item => item.Id);
            });

            modelBuilder.Entity<GeneratedGuidEntity>(entity =>
            {
                entity.ToTable("Phase2GeneratedGuidEntities");
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .UseMySqlClientGuidValueGeneration();
            });

            modelBuilder.Entity<ExplicitGeneratedGuidEntity>(entity =>
            {
                entity.ToTable("Phase2ExplicitGeneratedGuidEntities");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).ValueGeneratedOnAdd();
            });

            modelBuilder.Entity<ExplicitChar36Entity>(entity =>
            {
                entity.ToTable("Phase2ExplicitChar36Entities");
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36)
                    .UseMySqlClientGuidValueGeneration();
            });

            modelBuilder.Entity<ExplicitBinary16Entity>(entity =>
            {
                entity.ToTable("Phase2ExplicitBinary16Entities");
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16)
                    .UseMySqlClientGuidValueGeneration();
            });

            modelBuilder.Entity<ConvertedGuidEntity>(entity =>
            {
                entity.ToTable("Phase2ConvertedGuidEntities");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasConversion<string>();
            });

            modelBuilder.Entity<ConvertedGuidBytesEntity>(entity =>
            {
                entity.ToTable("Phase2ConvertedGuidBytesEntities");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasConversion<byte[]>();
            });
        }
    }

    private sealed class InvalidGuidFormatContext : DbContext
    {
        public InvalidGuidFormatContext(
            DbContextOptions<InvalidGuidFormatContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<InvalidGuidFormatEntity>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .Metadata.SetMySqlGuidFormat((MySqlGuidFormat)999);
            });
        }
    }

    private sealed class IntKeyEntity
    {
        public int Id { get; set; }
    }

    private sealed class GuidKeyEntity
    {
        public Guid Id { get; set; }
    }

    private sealed class GeneratedGuidEntity
    {
        public Guid Id { get; set; }
    }

    private sealed class ExplicitGeneratedGuidEntity
    {
        public Guid Id { get; set; }
    }

    private sealed class ExplicitChar36Entity
    {
        public Guid Id { get; set; }
    }

    private sealed class ExplicitBinary16Entity
    {
        public Guid Id { get; set; }
    }

    private sealed class ConvertedGuidEntity
    {
        public Guid Id { get; set; }
    }

    private sealed class ConvertedGuidBytesEntity
    {
        public Guid Id { get; set; }
    }

    private sealed class InvalidGuidFormatEntity
    {
        public Guid Id { get; set; }
    }
}
