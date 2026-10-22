using System.Data;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the opt-in native MariaDB UUID model and transport contracts without a database.
/// </summary>
public sealed class MySqlMariaDbUuidTests
{
    private const string ConnectionString = "Server=localhost;Database=doka;User ID=root;Password=password;";

    private static readonly Guid s_guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    /// <summary>
    /// Native UUID uses a textual connector value while retaining Guid in the EF model.
    /// </summary>
    [Fact]
    public void Native_uuid_default_has_unsized_store_type_and_text_parameter()
    {
        // Arrange
        using var context = new DefaultUuidContext(
            CreateOptions<DefaultUuidContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Uuid));

        // Act
        var property = context.Model.FindEntityType(typeof(UuidRecord))!.FindProperty(nameof(UuidRecord.Id))!;
        var mapping = property.GetRelationalTypeMapping();
        using var command = new MySqlCommand();
        var parameter = mapping.CreateParameter(command, "id", s_guid);
        var literal = mapping.GenerateSqlLiteral(s_guid);

        // Assert
        Assert.Equal(typeof(Guid), property.ClrType);
        Assert.Equal(MySqlGuidFormat.Uuid, property.GetMySqlGuidFormat());
        Assert.Equal("uuid", property.GetColumnType());
        Assert.Null(property.GetMaxLength());
        Assert.Null(property.IsFixedLength());
        Assert.Equal(typeof(string), mapping.Converter?.ProviderClrType);
        Assert.Equal("'00112233-4455-6677-8899-aabbccddeeff'", literal);
        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("00112233-4455-6677-8899-aabbccddeeff", parameter.Value);
    }

    /// <summary>
    /// A property opt-in does not change the provider's Binary16 default for its neighbors.
    /// </summary>
    [Fact]
    public void Native_uuid_property_opt_in_preserves_binary16_default()
    {
        // Arrange
        using var context = new PropertyUuidContext(
            CreateOptions<PropertyUuidContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        // Act
        var entityType = context.Model.FindEntityType(typeof(UuidRecord))!;
        var native = entityType.FindProperty(nameof(UuidRecord.Id))!;
        var binary = entityType.FindProperty(nameof(UuidRecord.OtherId))!;

        // Assert
        Assert.Equal("uuid", native.GetColumnType());
        Assert.Equal("binary(16)", binary.GetColumnType());
        Assert.Equal(MySqlGuidFormat.Uuid, native.GetMySqlGuidFormat());
        Assert.Equal(MySqlGuidFormat.Binary16, binary.GetMySqlGuidFormat());
    }

    /// <summary>
    /// The complex-property overload must retain the native format through model finalization.
    /// </summary>
    [Fact]
    public void Native_uuid_complex_property_opt_in_preserves_binary16_default()
    {
        // Arrange
        using var context = new ComplexUuidContext(
            CreateOptions<ComplexUuidContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        // Act
        var entityType = context.Model.FindEntityType(typeof(ComplexUuidRecord))!;
        var complexType = entityType.FindComplexProperty(nameof(ComplexUuidRecord.Details))!.ComplexType;
        var native = complexType.FindProperty(nameof(ComplexUuidDetails.ExternalId))!;
        var binary = entityType.FindProperty(nameof(ComplexUuidRecord.OtherId))!;

        // Assert
        Assert.Equal("uuid", native.GetColumnType());
        Assert.Null(native.GetMaxLength());
        Assert.Equal(MySqlGuidFormat.Uuid, native.GetMySqlGuidFormat());
        Assert.Equal("binary(16)", binary.GetColumnType());
        Assert.Equal(MySqlGuidFormat.Binary16, binary.GetMySqlGuidFormat());
    }

    /// <summary>
    /// An explicit native store type is recognized without requiring duplicate fluent metadata.
    /// </summary>
    [Fact]
    public void Explicit_uuid_column_type_selects_native_guid_mapping()
    {
        // Arrange
        using var context = new ColumnTypeUuidContext(
            CreateOptions<ColumnTypeUuidContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        // Act
        var property = context.Model.FindEntityType(typeof(UuidRecord))!.FindProperty(nameof(UuidRecord.Id))!;

        // Assert
        Assert.Equal(MySqlGuidFormat.Uuid, property.GetMySqlGuidFormat());
        Assert.Equal("uuid", property.GetRelationalTypeMapping().StoreType);
    }

    /// <summary>
    /// A native UUID store type must not masquerade as an unrelated CLR scalar mapping.
    /// </summary>
    [Fact]
    public void Native_uuid_mapping_rejects_unrelated_clr_type()
    {
        // Arrange
        using var context = new DefaultUuidContext(
            CreateOptions<DefaultUuidContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Uuid));

        var mappingSource = context.GetService<IRelationalTypeMappingSource>();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            mappingSource.FindMapping(typeof(int), "uuid"));

        // Assert
        Assert.Contains("Guid or string", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The connector's Binary16 wire setting applies even when model GUIDs use
    /// textual or native UUID storage; application byte converters must avoid it.
    /// </summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Char36)]
    [InlineData(MySqlGuidFormat.Uuid)]
    public void Nonbinary_guid_default_keeps_application_byte_converter_on_byte_reader(
        MySqlGuidFormat defaultFormat
    )
    {
        // Arrange
        using var context = new ConvertedBytesContext(
            CreateOptions<ConvertedBytesContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                defaultFormat));

        // Act
        var property = context.Model.FindEntityType(typeof(ConvertedBytesRecord))!
            .FindProperty(nameof(ConvertedBytesRecord.Id))!;

        // Assert
        Assert.Equal(typeof(byte[]), property.GetProviderClrType());
        Assert.Equal("varbinary(17)", property.GetColumnType());
        Assert.Null(property.GetMySqlGuidFormat());
    }

    /// <summary>
    /// Existing application-owned 16-byte columns must surface the new implicit
    /// width as an explicit migration rather than silently claiming native Guid storage.
    /// </summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Char36)]
    [InlineData(MySqlGuidFormat.Uuid)]
    public void Application_byte_converter_width_change_is_visible_in_migration(
        MySqlGuidFormat defaultFormat
    )
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        using var source = new LegacyConvertedBytesContext(
            CreateOptions<LegacyConvertedBytesContext>(serverVersion, defaultFormat));

        using var target = new ConvertedBytesContext(
            CreateOptions<ConvertedBytesContext>(serverVersion, defaultFormat));

        var sourceModel = source.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var targetModel = target.GetService<IDesignTimeModel>().Model;

        // Act
        var operations = target.GetService<IMigrationsModelDiffer>()
            .GetDifferences(sourceModel, targetModel.GetRelationalModel());

        var alter = Assert.Single(operations.OfType<AlterColumnOperation>());
        var commands = target.GetService<IMigrationsSqlGenerator>()
            .Generate(operations, targetModel);

        // Assert
        Assert.Equal("varbinary(16)", alter.OldColumn.ColumnType);
        Assert.Equal("varbinary(17)", alter.ColumnType);
        Assert.Equal(typeof(byte[]), alter.OldColumn.ClrType);
        Assert.Equal(typeof(byte[]), alter.ClrType);
        Assert.Null(alter.OldColumn.FindAnnotation(MySqlAnnotationNames.GuidFormat));
        Assert.Null(alter.FindAnnotation(MySqlAnnotationNames.GuidFormat));
        Assert.Contains(commands, command => command.CommandText.Contains("varbinary(17)", StringComparison.Ordinal));
    }

    /// <summary>
    /// MySQL must reject a native UUID context default before emitting unsupported DDL.
    /// </summary>
    [Fact]
    public void MySql_rejects_native_uuid_default()
    {
        // Arrange
        var options = CreateOptions<DefaultUuidContext>(
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            MySqlGuidFormat.Uuid);

        // Act
        var exception = Assert.Throws<NotSupportedException>(() =>
        {
            using var context = new DefaultUuidContext(options);
            _ = context.Model;
        });

        // Assert
        Assert.Contains("MariaDB 10.7", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// MySQL must also reject a property-only native UUID opt-in.
    /// </summary>
    [Fact]
    public void MySql_rejects_native_uuid_property()
    {
        // Arrange
        var options = CreateOptions<PropertyUuidContext>(MySqlServerVersion.MySql(new Version(8, 4, 0)));

        // Act
        var exception = Assert.Throws<NotSupportedException>(() =>
        {
            using var context = new PropertyUuidContext(options);
            _ = context.Model;
        });

        // Assert
        Assert.Contains("MariaDB 10.7", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The compatibility escape hatch does not enable a server type absent before MariaDB 10.7.
    /// </summary>
    [Fact]
    public void Older_mariadb_rejects_native_uuid_even_in_compatibility_mode()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MariaDb(
            new Version(10, 6, 20),
            MySqlServerVersionCompatibilityMode.AllowUnsupported);

        var options = CreateOptions<DefaultUuidContext>(serverVersion, MySqlGuidFormat.Uuid);

        // Act
        var exception = Assert.Throws<NotSupportedException>(() =>
        {
            using var context = new DefaultUuidContext(options);
            _ = context.Model;
        });

        // Assert
        Assert.Contains("MariaDB 10.7", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The first MariaDB release with UUID supports it when its otherwise
    /// unsupported release line is selected explicitly.
    /// </summary>
    [Fact]
    public void MariaDb107_accepts_native_uuid_with_compatibility_opt_in()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MariaDb(
            new Version(10, 7, 0),
            MySqlServerVersionCompatibilityMode.AllowUnsupported);

        using var context = new DefaultUuidContext(
            CreateOptions<DefaultUuidContext>(serverVersion, MySqlGuidFormat.Uuid));

        // Act
        var property = context.Model.FindEntityType(typeof(UuidRecord))!
            .FindProperty(nameof(UuidRecord.Id))!;

        // Assert
        Assert.Equal("uuid", property.GetColumnType());
    }

    /// <summary>
    /// Hand-authored UUID DDL cannot bypass the model-level engine guard.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MySql_rejects_native_uuid_migration_column(
        bool annotateFormat
    )
    {
        // Arrange
        using var context = new DefaultUuidContext(
            CreateOptions<DefaultUuidContext>(MySqlServerVersion.MySql(new Version(8, 4, 0))));

        var operation = new AddColumnOperation
        {
            Table = "UuidRecords",
            Name = "NativeId",
            ClrType = typeof(Guid),
            ColumnType = "uuid",
            IsNullable = true,
        };

        if (annotateFormat)
        {
            operation[MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Uuid;
        }

        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var exception = Assert.Throws<NotSupportedException>(() => generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains("MariaDB 10.7", exception.Message, StringComparison.Ordinal);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlServerVersion serverVersion,
        MySqlGuidFormat? defaultFormat = null
    ) where TContext : DbContext => MySqlFunctionalTestOptions
        .CreateTransientBuilder<TContext>()
        .UseMySql(
            ConnectionString,
            serverVersion,
            options =>
            {
                if (defaultFormat is { } format)
                {
                    options.DefaultGuidFormat(format);
                }
            })
        .Options;

    private sealed class UuidRecord
    {
        public Guid Id { get; set; }

        public Guid OtherId { get; set; }
    }

    private sealed class DefaultUuidContext(DbContextOptions<DefaultUuidContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<UuidRecord>();
    }

    private sealed class PropertyUuidContext(DbContextOptions<PropertyUuidContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<UuidRecord>().Property(record => record.Id)
                .HasMySqlGuidFormat(MySqlGuidFormat.Uuid);
        }
    }

    private sealed class ColumnTypeUuidContext(DbContextOptions<ColumnTypeUuidContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<UuidRecord>().Property(record => record.Id)
                .HasColumnType("uuid");
        }
    }

    private sealed class ConvertedBytesRecord
    {
        public Guid Id { get; set; }
    }

    private sealed class ComplexUuidRecord
    {
        public int Id { get; set; }

        public Guid OtherId { get; set; }

        public ComplexUuidDetails Details { get; set; } = new();
    }

    private sealed class ComplexUuidDetails
    {
        public Guid ExternalId { get; set; }
    }

    private sealed class ComplexUuidContext(DbContextOptions<ComplexUuidContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ComplexUuidRecord>().ComplexProperty(
                record => record.Details,
                complex => complex.Property(details => details.ExternalId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Uuid));
        }
    }

    private sealed class ConvertedBytesContext(DbContextOptions<ConvertedBytesContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ConvertedBytesRecord>().Property(record => record.Id)
                .HasConversion<byte[]>();
        }
    }

    private sealed class LegacyConvertedBytesContext(
        DbContextOptions<LegacyConvertedBytesContext> options
    ) : DbContext(options)
    {
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ConvertedBytesRecord>().Property(record => record.Id)
                .HasConversion<byte[]>()
                .HasColumnType("varbinary(16)");
        }
    }
}
