using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies provider-specific migrations snapshot and designer model round trips.
/// </summary>
public sealed class MySqlDesignTimeRoundTripTests
{
    private const string GeneratedNamespace = "Doka.GeneratedTemporalMigrations";
    private static readonly DateTime s_providerRowVersion =
        new(2026, 8, 31, 12, 34, 56, DateTimeKind.Utc);

    /// <summary>
    /// System-time metadata, including convention-inherited owned mapping, survives
    /// both generated design-time model surfaces without creating a pending migration.
    /// </summary>
    [Fact]
    public void System_time_snapshot_and_designer_roundtrip_without_pending_operations()
    {
        using var context = new SystemTimeDesignContext(
            CreateOptions<SystemTimeDesignContext>(MySqlServerVersion.MySql(new Version(8, 4, 0))));

        var generated = GenerateAndCompile(context);

        Assert.Contains(".IsTemporal(temporalTableBuilder =>", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(
            ".UseHistoryTable(\"TemporalRecordsHistory\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(".HasPeriodStart(\"ValidFrom\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasColumnName(\"valid_from\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".IsTemporal(temporalTableBuilder =>", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.IsTemporal, generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.IsTemporal, generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
        AssertSinglePhysicalTemporalTableCreation(context);
    }

    /// <summary>
    /// Application-time and system-time metadata share one generated table closure and
    /// both survive snapshot and designer compilation for a bitemporal MariaDB model.
    /// </summary>
    [Fact]
    public void Bitemporal_snapshot_and_designer_roundtrip_without_pending_operations()
    {
        using var context = new BitemporalDesignContext(
            CreateOptions<BitemporalDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        var generated = GenerateAndCompile(context);

        Assert.Contains(".IsTemporal(temporalTableBuilder =>", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(
            ".HasApplicationTimePeriod(applicationTimeTableBuilder =>",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(".HasPeriodName(\"BusinessValidity\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".UseWithoutOverlaps()", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(
            ".HasApplicationTimePeriod(applicationTimeTableBuilder =>",
            generated.DesignerCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.IsApplicationTime, generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.IsApplicationTime, generated.DesignerCode, StringComparison.Ordinal);
        AssertExactPropertySet(
            context.GetService<IDesignTimeModel>().Model,
            typeof(BitemporalDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id",
            "RecordedFrom",
            "RecordedTo");
        AssertExactPropertySet(
            generated.SnapshotModel,
            typeof(BitemporalDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id",
            "RecordedFrom",
            "RecordedTo");
        AssertExactPropertySet(
            generated.DesignerModel,
            typeof(BitemporalDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id",
            "RecordedFrom",
            "RecordedTo");
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
        AssertSinglePhysicalBitemporalTableCreation(context);
    }

    /// <summary>
    /// Typed application-time endpoints remain the complete property set through
    /// both generated design-time surfaces and the initial migration.
    /// </summary>
    [Fact]
    public void Application_time_snapshot_and_designer_roundtrip_without_default_properties()
    {
        using var context = new ApplicationTimeDesignContext(
            CreateOptions<ApplicationTimeDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        var generated = GenerateAndCompile(context);

        Assert.Contains(
            ".HasApplicationTimePeriod(applicationTimeTableBuilder =>",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(".HasPeriodStart(\"BusinessFrom\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasPeriodEnd(\"BusinessTo\")", generated.SnapshotCode, StringComparison.Ordinal);
        AssertExactPropertySet(
            context.GetService<IDesignTimeModel>().Model,
            typeof(ApplicationTimeDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id");
        AssertExactPropertySet(
            generated.SnapshotModel,
            typeof(ApplicationTimeDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id");
        AssertExactPropertySet(
            generated.DesignerModel,
            typeof(ApplicationTimeDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id");
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
        AssertSinglePhysicalApplicationTimeTableCreation(context);
    }

    /// <summary>
    /// Provider-native Char36 keys and foreign keys retain their Guid model CLR type
    /// through both generated design-time model surfaces.
    /// </summary>
    [Fact]
    public void Char36_snapshot_and_designer_roundtrip_guid_relationship_without_pending_operations()
    {
        using var context = new Char36DesignContext(
            CreateOptions<Char36DesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        var generated = GenerateAndCompile(context);

        Assert.Contains(".Property<System.Guid>(\"Id\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"DocumentId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(
            ".HasMySqlGuidFormat(MySqlGuidFormat.Char36)",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"Id\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"DocumentId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"Id\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"DocumentId\")", generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
        AssertChar36OperationMetadata(
            context,
            context.GetService<IDesignTimeModel>().Model);
        AssertChar36OperationMetadata(context, generated.SnapshotModel);
        AssertChar36OperationMetadata(context, generated.DesignerModel);
    }

    /// <summary>
    /// A context-level Char36 default remains a Guid model contract when one
    /// property participates in multiple relationship chains.
    /// </summary>
    [Fact]
    public void Default_char36_branching_relationships_roundtrip_without_string_model_drift()
    {
        using var context = new DefaultChar36RelationshipDesignContext(
            CreateOptions<DefaultChar36RelationshipDesignContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Char36));

        var generated = GenerateAndCompile(context);

        Assert.Contains(".Property<System.Guid>(\"ReferenceId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid?>(\"OptionalReferenceId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"ReferenceId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"OptionalReferenceId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".HasColumnType(\"varchar(36)\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"ReferenceId\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid?>(\"OptionalReferenceId\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"ReferenceId\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"OptionalReferenceId\")", generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
    }

    /// <summary>
    /// A context-level Char36 default and an explicit Binary16 override retain
    /// Guid model types and byte-order annotations through generated models.
    /// </summary>
    [Fact]
    public void Default_char36_and_binary16_override_roundtrip_through_generated_models()
    {
        using var context = new MixedGuidDesignContext(
            CreateOptions<MixedGuidDesignContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Char36));

        var generated = GenerateAndCompile(context);

        Assert.Contains(".Property<System.Guid>(\"Id\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"BinaryReference\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasMySqlGuidFormat(MySqlGuidFormat.Char36)", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasMySqlGuidFormat(MySqlGuidFormat.Binary16)", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<byte[]>(\"BinaryReference\")", generated.SnapshotCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
        AssertMixedGuidOperationMetadata(
            context,
            context.GetService<IDesignTimeModel>().Model);
        AssertMixedGuidOperationMetadata(context, generated.SnapshotModel);
        AssertMixedGuidOperationMetadata(context, generated.DesignerModel);

        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                null,
                context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        var createTable = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "MixedGuidDesignRecords");

        var binaryReference = Assert.Single(
            createTable.Columns,
            column => column.Name == nameof(MixedGuidDesignRecord.BinaryReference));

        Assert.Equal(typeof(Guid), binaryReference.ClrType);
        Assert.Equal("binary(16)", binaryReference.ColumnType);
        Assert.Equal(
            MySqlGuidFormat.Binary16,
            binaryReference.FindAnnotation(MySqlAnnotationNames.GuidFormat)?.Value);

        var migrationCode = GenerateMigrationCode(operations);

        Assert.Contains("table.Column<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("table.Column<byte[]>", migrationCode, StringComparison.Ordinal);
        _ = Compile(migrationCode);
    }

    /// <summary>
    /// Provider-owned Guid seed values retain their model CLR type through
    /// snapshots and designer models, including nullable and mixed-format values.
    /// </summary>
    [Fact]
    public void Provider_owned_guid_seed_data_roundtrips_as_model_values()
    {
        using var context = new GuidSeedDesignContext(
            CreateOptions<GuidSeedDesignContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Char36));

        var generated = GenerateAndCompile(context);

        Assert.Contains("Id = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("ExplicitChar36 = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("Binary16 = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("OptionalChar36 = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("PrincipalId = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("OptionalPrincipalId = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("Id = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains("ExplicitChar36 = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains("Binary16 = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains("OptionalChar36 = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains("PrincipalId = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains("OptionalPrincipalId = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".HasDefaultValue(new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasDefaultValue(new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(
            ".HasDefaultValue(new Guid(\"00000000-0000-0000-0000-000000000000\"))",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".HasDefaultValue(new Guid(\"00000000-0000-0000-0000-000000000000\"))",
            generated.DesignerCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".HasDefaultValue(\"27caab1e-a588-4dcc-bace-fef7cf47e1fd\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".HasDefaultValue(\"27caab1e-a588-4dcc-bace-fef7cf47e1fd\")",
            generated.DesignerCode,
            StringComparison.Ordinal);
        AssertProviderGuidDefaultModelValues(context.GetService<IDesignTimeModel>().Model);
        AssertProviderGuidDefaultModelValues(generated.SnapshotModel);
        AssertProviderGuidDefaultModelValues(generated.DesignerModel);
        Assert.DoesNotContain(
            "Id = \"bf1da273-beed-4197-ab57-4cf8395244d4\"",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ExplicitChar36 = \"5153b4e4-0158-4641-b387-433631824557\"",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
        AssertGuidSeedDataOperations(
            context,
            context.GetService<IDesignTimeModel>().Model);
        AssertGuidSeedDataOperations(context, generated.SnapshotModel);
        AssertGuidSeedDataOperations(context, generated.DesignerModel);
    }

    /// <summary>
    /// A model containing only converted Char36 values still imports the model
    /// namespace required by the Guid literals introduced during generation.
    /// </summary>
    [Fact]
    public void Provider_owned_char36_seed_includes_its_model_namespace()
    {
        using var context = new Char36SeedOnlyDesignContext(
            CreateOptions<Char36SeedOnlyDesignContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Char36));

        var generated = GenerateAndCompile(context);

        Assert.Contains("using System;", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("Id = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("using System;", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains("Id = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
    }

    /// <summary>
    /// Provider-owned conversions nested in complex properties contribute their
    /// model namespaces independently of the containing entity's scalar properties.
    /// </summary>
    [Fact]
    public void Provider_owned_complex_property_includes_its_model_namespace()
    {
        using var context = new Char36ComplexOnlyDesignContext(
            CreateOptions<Char36ComplexOnlyDesignContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Char36));

        var generated = GenerateAndCompile(context);

        Assert.Contains("using System;", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasDefaultValue(new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<string>(\"ApplicationId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("using System;", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".HasDefaultValue(new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".Property<string>(\"ApplicationId\")", generated.DesignerCode, StringComparison.Ordinal);
        AssertComplexGuidMetadata(context.GetService<IDesignTimeModel>().Model);
        AssertComplexGuidMetadata(generated.SnapshotModel);
        AssertComplexGuidMetadata(generated.DesignerModel);
    }

    /// <summary>
    /// Application-owned Guid converters retain EF Core's provider-shaped snapshot
    /// contract instead of being rewritten as provider-owned Guid mappings.
    /// </summary>
    [Fact]
    public void Application_owned_guid_seed_converter_retains_provider_values()
    {
        using var context = new ApplicationGuidSeedDesignContext(
            CreateOptions<ApplicationGuidSeedDesignContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Char36));

        var generated = GenerateAndCompile(context);

        Assert.Contains(".Property<string>(\"Id\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(
            "Id = \"89a78261-ea26-494e-a520-b518f51ed3d1\"",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Id = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<string>(\"Id\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(
            "Id = \"89a78261-ea26-494e-a520-b518f51ed3d1\"",
            generated.DesignerCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Id = new Guid(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(
            ".HasDefaultValue(\"89a78261-ea26-494e-a520-b518f51ed3d1\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".HasDefaultValue(\"89a78261-ea26-494e-a520-b518f51ed3d1\")",
            generated.DesignerCode,
            StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
    }

    /// <summary>
    /// Provider-owned JSON and row-version converters retain their model CLR
    /// types instead of leaking their provider CLR types into generated models.
    /// </summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public void Provider_owned_converters_roundtrip_model_types_without_pending_operations(
        MySqlGuidFormat defaultGuidFormat
    )
    {
        using var context = new ProviderConverterDesignContext(
            CreateOptions<ProviderConverterDesignContext>(
                MySqlServerVersion.MySql(new Version(8, 4, 0)),
                defaultGuidFormat));

        var generated = GenerateAndCompile(context);

        Assert.Contains(
            ".Property<System.Text.Json.JsonElement>(\"Element\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Property<System.Text.Json.JsonDocument>(\"Document\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Property<System.Text.Json.Nodes.JsonNode>(\"Node\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Property<System.Text.Json.Nodes.JsonObject>(\"ObjectValue\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Property<System.Text.Json.Nodes.JsonArray>(\"Array\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(".Property<byte[]>(\"Version\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"Element\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<System.DateTime>(\"Version\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("JsonElement.Parse(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("JsonDocument.Parse(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("JsonNode.Parse(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Element = \"{", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Document = \"{", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Node = \"{", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ObjectValue = \"{", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Array = \"[", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Version = new DateTime(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Version = new DateTime(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".HasDefaultValue(\"{\\\"kind\\\":\\\"default",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".HasDefaultValue(\"{\\\"kind\\\":\\\"default",
            generated.DesignerCode,
            StringComparison.Ordinal);
        AssertProviderJsonSeedModelValues(generated.SnapshotModel);
        AssertProviderJsonSeedModelValues(generated.DesignerModel);
        AssertProviderJsonDefaultModelValue(generated.SnapshotModel);
        AssertProviderJsonDefaultModelValue(generated.DesignerModel);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);

        using var empty = new EmptyTemporalDesignContext(
            CreateOptions<EmptyTemporalDesignContext>(MySqlServerVersion.MySql(new Version(8, 4, 0))));

        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                empty.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
                context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        var columns = Assert
            .Single(operations.OfType<CreateTableOperation>())
            .Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);

        Assert.Equal(typeof(System.Text.Json.JsonElement), columns["Element"].ClrType);
        Assert.Equal(typeof(System.Text.Json.JsonDocument), columns["Document"].ClrType);
        Assert.Equal(typeof(System.Text.Json.Nodes.JsonNode), columns["Node"].ClrType);
        Assert.Equal(typeof(System.Text.Json.Nodes.JsonObject), columns["ObjectValue"].ClrType);
        Assert.Equal(typeof(System.Text.Json.Nodes.JsonArray), columns["Array"].ClrType);
        Assert.Equal(typeof(byte[]), columns["Version"].ClrType);
        AssertProviderJsonDefaultOperationValues(columns);
        AssertProviderJsonSeedOperationValues(operations);

        var migrationCode = GenerateMigrationCode(operations);

        Assert.Contains("table.Column<JsonElement>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("table.Column<JsonDocument>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("table.Column<JsonNode>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("table.Column<JsonObject>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("table.Column<JsonArray>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("table.Column<byte[]>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("table.Column<DateTime>", migrationCode, StringComparison.Ordinal);
        _ = Compile(migrationCode);
    }

    /// <summary>
    /// Explicit invisible and visible column dispositions survive both generated
    /// design-time model surfaces without falling back to raw annotations.
    /// </summary>
    [Fact]
    public void Invisible_column_snapshot_and_designer_roundtrip_without_pending_operations()
    {
        using var context = new InvisibleColumnDesignContext(
            CreateOptions<InvisibleColumnDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        var generated = GenerateAndCompile(context);

        Assert.Contains(".IsInvisible()", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".IsInvisible(false)", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".IsInvisible()", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".IsInvisible(false)", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.Invisible, generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.Invisible, generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
    }

    /// <summary>
    /// Spatial SRID metadata survives generated snapshots on both the native MySQL
    /// route and the MariaDB CHECK-emulation route without a pending migration.
    /// </summary>
    [Fact]
    public void Spatial_srid_snapshot_and_designer_roundtrip_without_pending_operations()
    {
        var serverVersions = new[]
        {
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
        };

        foreach (var serverVersion in serverVersions)
        {
            using var context = new SpatialSridDesignContext(CreateSpatialOptions(serverVersion));
            var generated = GenerateAndCompile(context);

            Assert.Contains(".HasSrid(4326)", generated.SnapshotCode, StringComparison.Ordinal);
            Assert.Contains(".HasSrid(4326)", generated.DesignerCode, StringComparison.Ordinal);
            Assert.Contains(
                ".Property<NetTopologySuite.Geometries.Point>(\"Location\")",
                generated.SnapshotCode,
                StringComparison.Ordinal);
            Assert.Contains(
                ".Property<NetTopologySuite.Geometries.Point>(\"Location\")",
                generated.DesignerCode,
                StringComparison.Ordinal);
            Assert.DoesNotContain(".Property<MySqlGeometry>(\"Location\")", generated.SnapshotCode, StringComparison.Ordinal);
            Assert.DoesNotContain(".Property<MySqlGeometry>(\"Location\")", generated.DesignerCode, StringComparison.Ordinal);
            Assert.DoesNotContain(
                MySqlAnnotationNames.SpatialReferenceSystemId,
                generated.SnapshotCode,
                StringComparison.Ordinal);
            AssertSpatialSeedModelValue(generated.SnapshotModel);
            AssertSpatialSeedModelValue(generated.DesignerModel);
            AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
            AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
            AssertSpatialSeedDataOperations(context, context.GetService<IDesignTimeModel>().Model);
            AssertSpatialSeedDataOperations(context, generated.SnapshotModel);
            AssertSpatialSeedDataOperations(context, generated.DesignerModel);
        }
    }

    /// <summary>
    /// Entity-splitting snapshots retain one generated principal key without
    /// turning the secondary shared primary/foreign key into a generator.
    /// </summary>
    [Fact]
    public void Entity_splitting_snapshot_and_designer_preserve_principal_generation_ownership()
    {
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        using var context = new EntitySplitDesignContext(CreateOptions<EntitySplitDesignContext>(serverVersion));
        var generated = GenerateAndCompile(context);

        Assert.Contains(".SplitToTable(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".SplitToTable(", generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
        AssertEntitySplitGenerationOwnership(context, context.GetService<IDesignTimeModel>().Model, serverVersion);
        AssertEntitySplitGenerationOwnership(context, generated.SnapshotModel, serverVersion);
        AssertEntitySplitGenerationOwnership(context, generated.DesignerModel, serverVersion);
    }

    /// <summary>
    /// Incomplete discriminator metadata survives both generated design-time model
    /// surfaces while the default complete mapping remains implicit.
    /// </summary>
    [Fact]
    public void Discriminator_completeness_snapshot_and_designer_preserve_explicit_and_default_values()
    {
        using var context = new DiscriminatorCompletenessDesignContext(
            CreateOptions<DiscriminatorCompletenessDesignContext>(
                MySqlServerVersion.MySql(new Version(8, 4, 0))));
        var sourceModel = context.GetService<IDesignTimeModel>().Model;

        AssertConvertedDiscriminatorModel(sourceModel);
        _ = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, sourceModel.GetRelationalModel());
        AssertConvertedDiscriminatorModel(sourceModel);

        var generated = GenerateAndCompile(context);

        Assert.Contains(".IsComplete(false)", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".IsComplete(false)", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".IsComplete(true)", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".IsComplete(true)", generated.DesignerCode, StringComparison.Ordinal);
        AssertDiscriminatorCompleteness(context.GetService<IDesignTimeModel>().Model);
        AssertDiscriminatorCompleteness(generated.SnapshotModel);
        AssertDiscriminatorCompleteness(generated.DesignerModel);
        AssertConvertedDiscriminatorSnapshot(generated.SnapshotModel);
        AssertConvertedDiscriminatorSnapshot(generated.DesignerModel);
    }

    /// <summary>
    /// Native integer and enum discriminators using EF's default property name retain
    /// their provider type and values in both generated design-time model surfaces.
    /// </summary>
    [Fact]
    public void Non_string_discriminator_snapshot_and_designer_preserve_type_and_values()
    {
        using var context = new DiscriminatorCompletenessDesignContext(
            CreateOptions<DiscriminatorCompletenessDesignContext>(
                MySqlServerVersion.MySql(new Version(8, 4, 0))));
        var generated = GenerateAndCompile(context);

        Assert.Equal(
            2,
            generated.SnapshotCode.Split(
                ".HasDiscriminator<int>(\"Discriminator\")",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            2,
            generated.DesignerCode.Split(
                ".HasDiscriminator<int>(\"Discriminator\")",
                StringSplitOptions.None).Length - 1);
        AssertNonStringDiscriminatorSnapshot(generated.SnapshotModel);
        AssertNonStringDiscriminatorSnapshot(generated.DesignerModel);
    }

    private static GeneratedTemporalModels GenerateAndCompile(
        DbContext context
    )
    {
        var sourceModel = context.GetService<IDesignTimeModel>().Model;
        var suffix = Guid
            .NewGuid()
            .ToString("N", CultureInfo.InvariantCulture);

        var snapshotName = $"TemporalSnapshot{suffix}";
        var migrationName = $"TemporalMigration{suffix}";
        var migrationId = $"20260818000000_{migrationName}";
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddEntityFrameworkDokaMySqlDesignTime();
        services.AddEntityFrameworkDokaMySqlNetTopologySuite();

        using var serviceProvider = services.BuildServiceProvider();
        var generator = serviceProvider
            .GetRequiredService<IMigrationsCodeGeneratorSelector>()
            .Select("C#");

        var snapshotCode = generator.GenerateSnapshot(GeneratedNamespace, context.GetType(), snapshotName, sourceModel);
        var migrationCode = generator.GenerateMigration(GeneratedNamespace, migrationName, [], []);
        var designerCode = generator.GenerateMetadata(
            GeneratedNamespace,
            context.GetType(),
            migrationName,
            migrationId,
            sourceModel);

        var assembly = Compile(snapshotCode, migrationCode, designerCode);
        var snapshot = (ModelSnapshot)Activator.CreateInstance(
            assembly.GetType($"{GeneratedNamespace}.{snapshotName}", throwOnError: true)!)!;

        var migration = (Migration)Activator.CreateInstance(
            assembly.GetType($"{GeneratedNamespace}.{migrationName}", throwOnError: true)!)!;

        return new GeneratedTemporalModels(snapshotCode, designerCode, snapshot.Model, migration.TargetModel);
    }

    private static void AssertDiscriminatorCompleteness(
        IModel model
    )
    {
        var incomplete = model.FindEntityType(typeof(IncompleteDiscriminatorDesignRecord));
        var complete = model.FindEntityType(typeof(CompleteDiscriminatorDesignRecord));
        var converted = model.FindEntityType(typeof(ConvertedDiscriminatorDesignRecord));
        var childConfiguredConverted = model.FindEntityType(
            typeof(ChildConfiguredConvertedDiscriminatorDesignRecord));

        Assert.NotNull(incomplete);
        Assert.NotNull(complete);
        Assert.NotNull(converted);
        Assert.NotNull(childConfiguredConverted);
        Assert.False(incomplete.GetIsDiscriminatorMappingComplete());
        Assert.True(complete.GetIsDiscriminatorMappingComplete());
        Assert.False(converted.GetIsDiscriminatorMappingComplete());
        Assert.False(childConfiguredConverted.GetIsDiscriminatorMappingComplete());
    }

    private static void AssertConvertedDiscriminatorSnapshot(
        IModel model
    )
    {
        var root = model.FindEntityType(typeof(ConvertedDiscriminatorDesignRecord));
        var known = model.FindEntityType(typeof(KnownConvertedDiscriminatorDesignRecord));
        var childConfiguredRoot = model.FindEntityType(typeof(ChildConfiguredConvertedDiscriminatorDesignRecord));
        var childConfiguredKnown = model.FindEntityType(
            typeof(KnownChildConfiguredConvertedDiscriminatorDesignRecord));

        Assert.NotNull(root);
        Assert.NotNull(known);
        Assert.NotNull(childConfiguredRoot);
        Assert.NotNull(childConfiguredKnown);
        Assert.Equal(typeof(string), root.FindDiscriminatorProperty()!.ClrType);
        Assert.Equal("K", known.GetDiscriminatorValue());
        Assert.Equal(typeof(string), childConfiguredRoot.FindDiscriminatorProperty()!.ClrType);
        Assert.Equal("K", childConfiguredKnown.GetDiscriminatorValue());
    }

    private static void AssertConvertedDiscriminatorModel(
        IModel model
    )
    {
        var root = model.FindEntityType(typeof(ConvertedDiscriminatorDesignRecord));
        var known = model.FindEntityType(typeof(KnownConvertedDiscriminatorDesignRecord));
        var childConfiguredRoot = model.FindEntityType(typeof(ChildConfiguredConvertedDiscriminatorDesignRecord));
        var childConfiguredKnown = model.FindEntityType(
            typeof(KnownChildConfiguredConvertedDiscriminatorDesignRecord));

        Assert.NotNull(root);
        Assert.NotNull(known);
        Assert.NotNull(childConfiguredRoot);
        Assert.NotNull(childConfiguredKnown);
        Assert.Equal(typeof(DesignDiscriminator), root.FindDiscriminatorProperty()!.ClrType);
        Assert.Equal(DesignDiscriminator.Known, known.GetDiscriminatorValue());
        Assert.Equal(typeof(DesignDiscriminator), childConfiguredRoot.FindDiscriminatorProperty()!.ClrType);
        Assert.Equal(DesignDiscriminator.Known, childConfiguredKnown.GetDiscriminatorValue());
    }

    private static void AssertNonStringDiscriminatorSnapshot(
        IModel model
    )
    {
        var intRoot = model.FindEntityType(typeof(IntDiscriminatorDesignRecord));
        var knownInt = model.FindEntityType(typeof(KnownIntDiscriminatorDesignRecord));
        var enumRoot = model.FindEntityType(typeof(EnumDiscriminatorDesignRecord));
        var knownEnum = model.FindEntityType(typeof(KnownEnumDiscriminatorDesignRecord));

        Assert.NotNull(intRoot);
        Assert.NotNull(knownInt);
        Assert.NotNull(enumRoot);
        Assert.NotNull(knownEnum);
        Assert.Equal(typeof(int), intRoot.FindDiscriminatorProperty()!.ClrType);
        Assert.Equal(7, knownInt.GetDiscriminatorValue());
        Assert.Equal(typeof(int), enumRoot.FindDiscriminatorProperty()!.ClrType);
        Assert.Equal(7, knownEnum.GetDiscriminatorValue());
    }

    private static string GenerateMigrationCode(
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddEntityFrameworkDokaMySqlDesignTime();
        services.AddEntityFrameworkDokaMySqlNetTopologySuite();

        using var serviceProvider = services.BuildServiceProvider();
        var generator = serviceProvider
            .GetRequiredService<IMigrationsCodeGeneratorSelector>()
            .Select("C#");

        return generator.GenerateMigration(
            GeneratedNamespace,
            $"ProviderMigration{Guid.NewGuid():N}",
            operations,
            []);
    }

    private static void AssertMixedGuidOperationMetadata(
        DbContext context,
        IModel model
    )
    {
        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel());
        var createTable = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "MixedGuidDesignRecords");
        var id = Assert.Single(
            createTable.Columns,
            column => column.Name == nameof(MixedGuidDesignRecord.Id));
        var binaryReference = Assert.Single(
            createTable.Columns,
            column => column.Name == nameof(MixedGuidDesignRecord.BinaryReference));
        var index = Assert.Single(
            operations.OfType<CreateIndexOperation>(),
            operation => operation.Name == "IX_MixedGuidDesignRecords_Name_BinaryReference");

        Assert.Equal(MySqlGuidFormat.Char36, id.GetMySqlMigrationMetadata().GuidFormat);
        Assert.Equal(MySqlGuidFormat.Binary16, binaryReference.GetMySqlMigrationMetadata().GuidFormat);
        Assert.Equal([16, 0], index.GetMySqlMigrationMetadata().IndexPrefixLengths);
    }

    private static void AssertChar36OperationMetadata(
        DbContext context,
        IModel model
    )
    {
        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel());
        var tables = operations
            .OfType<CreateTableOperation>()
            .ToDictionary(operation => operation.Name, StringComparer.Ordinal);
        var id = Assert.Single(
            tables["Char36DesignDocuments"].Columns,
            column => column.Name == nameof(Char36DesignDocument.Id));
        var documentId = Assert.Single(
            tables["Char36DesignRevisions"].Columns,
            column => column.Name == nameof(Char36DesignRevision.DocumentId));

        Assert.Equal(MySqlGuidFormat.Char36, id.GetMySqlMigrationMetadata().GuidFormat);
        Assert.Equal(
            MySqlValueGenerationStrategy.ClientGuid,
            id.GetMySqlMigrationMetadata().ValueGenerationStrategy);
        Assert.Equal(MySqlGuidFormat.Char36, documentId.GetMySqlMigrationMetadata().GuidFormat);
        Assert.Equal(
            MySqlValueGenerationStrategy.None,
            documentId.GetMySqlMigrationMetadata().ValueGenerationStrategy);
    }

    private static Assembly Compile(
        params string[] sources
    )
    {
        var syntaxTrees = sources
            .Select(source => CSharpSyntaxTree.ParseText(source))
            .ToArray();

        var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries)
            ?? [];

        var references = trustedPlatformAssemblies
            .Concat(
                AppDomain
                    .CurrentDomain
                    .GetAssemblies()
                    .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                    .Select(assembly => assembly.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            $"Doka.GeneratedTemporalMigrations.{Guid.NewGuid():N}",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        var errors = result
            .Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();

        Assert.True(result.Success, string.Join(Environment.NewLine, errors));

        return Assembly.Load(stream.ToArray());
    }

    private static void AssertRoundTripsWithoutOperations(
        DbContext context,
        IModel generatedModel
    )
    {
        var sourceModel = context.GetService<IDesignTimeModel>().Model;
        var initializedGeneratedModel = context
            .GetService<IModelRuntimeInitializer>()
            .Initialize(
                generatedModel,
                designTime: true,
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Model.Validation>>());

        var generatedRelationalModel = initializedGeneratedModel.GetRelationalModel();
        var sourceRelationalModel = sourceModel.GetRelationalModel();
        var modelDiffer = context.GetService<IMigrationsModelDiffer>();

        Assert.False(modelDiffer.HasDifferences(generatedRelationalModel, sourceRelationalModel));

        Assert.Empty(modelDiffer.GetDifferences(generatedRelationalModel, sourceRelationalModel));
    }

    private static void AssertGuidSeedDataOperations(
        GuidSeedDesignContext context,
        IModel model
    )
    {
        var initializedModel = context
            .GetService<IModelRuntimeInitializer>()
            .Initialize(
                model,
                designTime: true,
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Model.Validation>>());
        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, initializedModel.GetRelationalModel());
        var inserts = operations
            .OfType<InsertDataOperation>()
            .ToLookup(operation => operation.Table, StringComparer.Ordinal);

        var principals = inserts["GuidSeedDesignRecords"];
        Assert.Equal(2, principals.Sum(operation => operation.Values.GetLength(0)));
        Assert.All(GetColumnValues(principals, nameof(GuidSeedDesignRecord.Id)), value => Assert.IsType<string>(value));
        Assert.All(
            GetColumnValues(principals, nameof(GuidSeedDesignRecord.ExplicitChar36)),
            value => Assert.IsType<string>(value));
        Assert.All(
            GetColumnValues(principals, nameof(GuidSeedDesignRecord.Binary16)),
            value => Assert.IsType<Guid>(value));
        AssertNullableProviderGuidValues(
            GetColumnValues(principals, nameof(GuidSeedDesignRecord.OptionalChar36)));

        var principalTable = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "GuidSeedDesignRecords");
        Assert.Equal(
            new Guid("27caab1e-a588-4dcc-bace-fef7cf47e1fd"),
            Assert.Single(
                    principalTable.Columns,
                    column => column.Name == nameof(GuidSeedDesignRecord.DefaultChar36))
                .DefaultValue);
        Assert.Equal(
            new Guid("5c855603-622d-4bee-b232-f74f63026caf"),
            Assert.Single(
                    principalTable.Columns,
                    column => column.Name == nameof(GuidSeedDesignRecord.DefaultBinary16))
                .DefaultValue);
        Assert.Equal(
            new Guid("19bbd2b4-6a65-4469-bfaa-6ed3d4c19d81"),
            Assert.Single(
                    principalTable.Columns,
                    column => column.Name == nameof(GuidSeedDesignRecord.OptionalDefaultChar36))
                .DefaultValue);
        Assert.Equal(
            Guid.Empty,
            Assert.Single(
                    principalTable.Columns,
                    column => column.Name == nameof(GuidSeedDesignRecord.EmptyDefaultChar36))
                .DefaultValue);

        var dependents = inserts["GuidSeedDependentRecords"];
        Assert.Equal(2, dependents.Sum(operation => operation.Values.GetLength(0)));
        Assert.All(
            GetColumnValues(dependents, nameof(GuidSeedDependentRecord.PrincipalId)),
            value => Assert.IsType<string>(value));
        AssertNullableProviderGuidValues(
            GetColumnValues(dependents, nameof(GuidSeedDependentRecord.OptionalPrincipalId)));

        var migrationCode = GenerateMigrationCode(operations);

        Assert.Contains("migrationBuilder.InsertData(", migrationCode, StringComparison.Ordinal);
        Assert.Contains("table: \"GuidSeedDependentRecords\"", migrationCode, StringComparison.Ordinal);
        Assert.Contains(
            "defaultValue: new Guid(\"27caab1e-a588-4dcc-bace-fef7cf47e1fd\")",
            migrationCode,
            StringComparison.Ordinal);
        _ = Compile(migrationCode);
    }

    private static void AssertProviderGuidDefaultModelValues(
        IModel model
    )
    {
        var entityType = model.FindEntityType(typeof(GuidSeedDesignRecord));

        Assert.Equal(
            new Guid("27caab1e-a588-4dcc-bace-fef7cf47e1fd"),
            entityType?.FindProperty(nameof(GuidSeedDesignRecord.DefaultChar36))?.GetDefaultValue());
        Assert.Equal(
            new Guid("5c855603-622d-4bee-b232-f74f63026caf"),
            entityType?.FindProperty(nameof(GuidSeedDesignRecord.DefaultBinary16))?.GetDefaultValue());
        Assert.Equal(
            new Guid("19bbd2b4-6a65-4469-bfaa-6ed3d4c19d81"),
            entityType?.FindProperty(nameof(GuidSeedDesignRecord.OptionalDefaultChar36))?.GetDefaultValue());
        Assert.Equal(
            Guid.Empty,
            entityType?.FindProperty(nameof(GuidSeedDesignRecord.EmptyDefaultChar36))?.GetDefaultValue());
    }

    private static object?[] GetColumnValues(
        InsertDataOperation operation,
        string column
    )
    {
        var columnIndex = Array.IndexOf(operation.Columns, column);

        Assert.True(columnIndex >= 0, $"Seed operation for '{operation.Table}' has no '{column}' column.");

        return Enumerable
            .Range(0, operation.Values.GetLength(0))
            .Select(row => operation.Values[row, columnIndex])
            .ToArray();
    }

    private static object?[] GetColumnValues(
        IEnumerable<InsertDataOperation> operations,
        string column
    ) => operations
        .Where(operation => Array.IndexOf(operation.Columns, column) >= 0)
        .SelectMany(operation => GetColumnValues(operation, column))
        .ToArray();

    private static void AssertNullableProviderGuidValues(
        object?[] values
    )
    {
        Assert.Single(values, value => value is null);
        Assert.Single(values, value => value is string);
    }

    private static void AssertSpatialSeedModelValue(
        IModel model
    )
    {
        var entityType = model.FindEntityType(typeof(SpatialSridDesignRecord));
        var seed = Assert.Single(entityType!.GetSeedData());
        var location = Assert.IsType<Point>(seed[nameof(SpatialSridDesignRecord.Location)]);

        Assert.Equal(13.4050, location.X);
        Assert.Equal(52.5200, location.Y);
        Assert.Equal(4326, location.SRID);
    }

    private static void AssertSpatialSeedDataOperations(
        SpatialSridDesignContext context,
        IModel model
    )
    {
        var initializedModel = context
            .GetService<IModelRuntimeInitializer>()
            .Initialize(
                model,
                designTime: true,
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Model.Validation>>());
        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, initializedModel.GetRelationalModel());
        var table = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "SpatialSridDesignRecord");
        var locationColumn = Assert.Single(
            table.Columns,
            column => column.Name == nameof(SpatialSridDesignRecord.Location));
        var insert = Assert.Single(operations.OfType<InsertDataOperation>());

        Assert.Equal(typeof(Point), locationColumn.ClrType);
        var location = Assert.IsType<Point>(
            Assert.Single(GetColumnValues(insert, nameof(SpatialSridDesignRecord.Location))));

        Assert.Equal(13.4050, location.X);
        Assert.Equal(52.5200, location.Y);
        Assert.Equal(4326, location.SRID);
        _ = Compile(GenerateMigrationCode(operations));
    }

    private static void AssertProviderJsonSeedModelValues(
        IModel model
    )
    {
        var entityType = model.FindEntityType(typeof(ProviderConverterDesignRecord));
        var seed = Assert.Single(entityType!.GetSeedData());

        Assert.Equal(
            """{"kind":"element","value":1}""",
            Assert.IsType<JsonElement>(seed[nameof(ProviderConverterDesignRecord.Element)]).GetRawText());
        Assert.Equal(
            """{"kind":"document","value":2}""",
            Assert.IsType<JsonDocument>(seed[nameof(ProviderConverterDesignRecord.Document)]).RootElement.GetRawText());
        Assert.Equal(
            """{"kind":"node","value":3}""",
            Assert.IsAssignableFrom<JsonNode>(seed[nameof(ProviderConverterDesignRecord.Node)]).ToJsonString());
        Assert.Equal(
            """{"kind":"object","value":4}""",
            Assert.IsType<JsonObject>(seed[nameof(ProviderConverterDesignRecord.ObjectValue)]).ToJsonString());
        Assert.Equal(
            """["array",5,true]""",
            Assert.IsType<JsonArray>(seed[nameof(ProviderConverterDesignRecord.Array)]).ToJsonString());
        Assert.Equal(
            MySqlBytesToDateTimeConverter.ToBytes(s_providerRowVersion),
            Assert.IsType<byte[]>(seed[nameof(ProviderConverterDesignRecord.Version)]));
    }

    private static void AssertProviderJsonSeedOperationValues(
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var insert = Assert.Single(operations.OfType<InsertDataOperation>());

        Assert.Equal(
            """{"kind":"element","value":1}""",
            Assert.IsType<string>(GetColumnValues(insert, nameof(ProviderConverterDesignRecord.Element)).Single()));
        Assert.Equal(
            """{"kind":"document","value":2}""",
            Assert.IsType<string>(GetColumnValues(insert, nameof(ProviderConverterDesignRecord.Document)).Single()));
        Assert.Equal(
            """{"kind":"node","value":3}""",
            Assert.IsType<string>(GetColumnValues(insert, nameof(ProviderConverterDesignRecord.Node)).Single()));
        Assert.Equal(
            """{"kind":"object","value":4}""",
            Assert.IsType<string>(GetColumnValues(insert, nameof(ProviderConverterDesignRecord.ObjectValue)).Single()));
        Assert.Equal(
            """["array",5,true]""",
            Assert.IsType<string>(GetColumnValues(insert, nameof(ProviderConverterDesignRecord.Array)).Single()));
        Assert.DoesNotContain(nameof(ProviderConverterDesignRecord.Version), insert.Columns);
    }

    private static void AssertComplexGuidMetadata(
        IModel model
    )
    {
        var entityType = model.FindEntityType(typeof(Char36ComplexOnlyDesignRecord))
            ?? model.FindEntityType(typeof(Char36ComplexOnlyDesignRecord).FullName!);
        var complexType = entityType
            ?.FindComplexProperty(nameof(Char36ComplexOnlyDesignRecord.Details))
            ?.ComplexType;
        var externalId = complexType?.FindProperty(nameof(Char36ComplexOnlyDesignDetails.ExternalId));
        var applicationId = complexType?.FindProperty(nameof(Char36ComplexOnlyDesignDetails.ApplicationId));

        Assert.Equal(typeof(Guid), externalId?.ClrType);
        Assert.Equal(MySqlGuidFormat.Char36, externalId?.GetMySqlGuidFormat());
        Assert.Equal(
            new Guid("0517e651-62e5-4a93-845d-fd87b14e4363"),
            Assert.IsType<Guid>(externalId?.GetDefaultValue()));
        Assert.Equal(
            typeof(string),
            applicationId?.ClrType == typeof(string)
                ? applicationId.ClrType
                : applicationId?.GetProviderClrType() ?? applicationId?.GetValueConverter()?.ProviderClrType);
        Assert.Null(applicationId?.GetMySqlGuidFormat());
    }

    private static void AssertProviderJsonDefaultModelValue(
        IModel model
    )
    {
        var entityType = model.FindEntityType(typeof(ProviderConverterDesignRecord));

        Assert.Equal(
            """{"kind":"default-element"}""",
            Assert.IsType<JsonElement>(
                    entityType?.FindProperty(nameof(ProviderConverterDesignRecord.Element))?.GetDefaultValue())
                .GetRawText());
        Assert.Equal(
            """{"kind":"default-document"}""",
            Assert.IsType<JsonDocument>(
                    entityType?.FindProperty(nameof(ProviderConverterDesignRecord.Document))?.GetDefaultValue())
                .RootElement.GetRawText());
        Assert.Equal(
            """{"kind":"default-node"}""",
            Assert.IsAssignableFrom<JsonNode>(
                    entityType?.FindProperty(nameof(ProviderConverterDesignRecord.Node))?.GetDefaultValue())
                .ToJsonString());
        Assert.Equal(
            """{"kind":"default-object"}""",
            Assert.IsType<JsonObject>(
                    entityType?.FindProperty(nameof(ProviderConverterDesignRecord.ObjectValue))?.GetDefaultValue())
                .ToJsonString());
        Assert.Equal(
            """["default-array"]""",
            Assert.IsType<JsonArray>(
                    entityType?.FindProperty(nameof(ProviderConverterDesignRecord.Array))?.GetDefaultValue())
                .ToJsonString());
    }

    private static void AssertProviderJsonDefaultOperationValues(
        Dictionary<string, AddColumnOperation> columns
    )
    {
        Assert.Equal(
            """{"kind":"default-element"}""",
            Assert.IsType<string>(columns[nameof(ProviderConverterDesignRecord.Element)].DefaultValue));
        Assert.Equal(
            """{"kind":"default-document"}""",
            Assert.IsType<string>(columns[nameof(ProviderConverterDesignRecord.Document)].DefaultValue));
        Assert.Equal(
            """{"kind":"default-node"}""",
            Assert.IsType<string>(columns[nameof(ProviderConverterDesignRecord.Node)].DefaultValue));
        Assert.Equal(
            """{"kind":"default-object"}""",
            Assert.IsType<string>(columns[nameof(ProviderConverterDesignRecord.ObjectValue)].DefaultValue));
        Assert.Equal(
            """["default-array"]""",
            Assert.IsType<string>(columns[nameof(ProviderConverterDesignRecord.Array)].DefaultValue));
    }

    private static void AssertSinglePhysicalTemporalTableCreation(
        SystemTimeDesignContext targetContext
    )
    {
        using var sourceContext = new EmptyTemporalDesignContext(
            CreateOptions<EmptyTemporalDesignContext>(MySqlServerVersion.MySql(new Version(8, 4, 0))));

        var operations = targetContext
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                sourceContext
                    .GetService<IDesignTimeModel>()
                    .Model
                    .GetRelationalModel(),
                targetContext
                    .GetService<IDesignTimeModel>()
                    .Model
                    .GetRelationalModel());

        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());

        Assert.Equal("TemporalRecords", createTable.Name);
        Assert.True(
            createTable.FindAnnotation(MySqlAnnotationNames.IsTemporal)
                ?.Value as bool?);
    }

    private static void AssertSinglePhysicalBitemporalTableCreation(
        BitemporalDesignContext targetContext
    )
    {
        using var sourceContext = new EmptyTemporalDesignContext(
            CreateOptions<EmptyTemporalDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        var operations = targetContext
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                sourceContext
                    .GetService<IDesignTimeModel>()
                    .Model
                    .GetRelationalModel(),
                targetContext
                    .GetService<IDesignTimeModel>()
                    .Model
                    .GetRelationalModel());

        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());
        var columnNames = createTable
            .Columns.Select(column => column.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Id", "business_from", "business_to", "recorded_from", "recorded_to"],
            columnNames);
        Assert.DoesNotContain(MySqlApplicationTimeMetadata.DefaultPeriodStartPropertyName, columnNames);
        Assert.DoesNotContain(MySqlApplicationTimeMetadata.DefaultPeriodEndPropertyName, columnNames);
    }

    private static void AssertSinglePhysicalApplicationTimeTableCreation(
        ApplicationTimeDesignContext targetContext
    )
    {
        using var sourceContext = new EmptyTemporalDesignContext(
            CreateOptions<EmptyTemporalDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        var operations = targetContext
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                sourceContext.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
                targetContext.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());
        var columnNames = createTable
            .Columns.Select(column => column.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Id", "business_from", "business_to"], columnNames);
        Assert.DoesNotContain(MySqlApplicationTimeMetadata.DefaultPeriodStartPropertyName, columnNames);
        Assert.DoesNotContain(MySqlApplicationTimeMetadata.DefaultPeriodEndPropertyName, columnNames);
    }

    private static void AssertExactPropertySet(
        IModel model,
        Type entityType,
        params string[] expectedProperties
    )
    {
        var properties = model
            .FindEntityType(entityType)!
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedProperties, properties);
    }

    private static void AssertEntitySplitGenerationOwnership(
        DbContext context,
        IModel model,
        MySqlServerVersion serverVersion
    )
    {
        using var source = new EmptyTemporalDesignContext(CreateOptions<EmptyTemporalDesignContext>(serverVersion));
        var initializedModel = context
            .GetService<IModelRuntimeInitializer>()
            .Initialize(
                model,
                designTime: true,
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Model.Validation>>());

        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                source
                    .GetService<IDesignTimeModel>()
                    .Model
                    .GetRelationalModel(),
                initializedModel.GetRelationalModel());

        var tables = operations
            .OfType<CreateTableOperation>()
            .ToDictionary(operation => operation.Name, StringComparer.Ordinal);

        var principalId = Assert.Single(tables["EntitySplitDesignRecords"].Columns, column => column.Name == "Id");

        var secondaryId = Assert.Single(tables["EntitySplitDesignDetails"].Columns, column => column.Name == "Id");

        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            principalId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)?.Value);
        Assert.Null(secondaryId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy));
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlServerVersion serverVersion,
        MySqlGuidFormat defaultGuidFormat = MySqlGuidFormat.Binary16
    )
        where TContext : DbContext => MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>().UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => options.DefaultGuidFormat(defaultGuidFormat))
        .Options;

    private static DbContextOptions<SpatialSridDesignContext> CreateSpatialOptions(
        MySqlServerVersion serverVersion
    ) => MySqlFunctionalTestOptions
        .CreateTransientBuilder<SpatialSridDesignContext>()
        .UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => options.UseNetTopologySuite())
        .Options;

    private sealed record GeneratedTemporalModels(
        string SnapshotCode,
        string DesignerCode,
        IModel SnapshotModel,
        IModel DesignerModel
    );
}

/// <summary>
/// Test context for generated spatial SRID migration models.
/// </summary>
public sealed class SpatialSridDesignContext : DbContext
{
    /// <summary>
    /// Creates the spatial design-time context.
    /// </summary>
    public SpatialSridDesignContext(
        DbContextOptions<SpatialSridDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<SpatialSridDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.Location)
                .HasSrid(4326);
            entity.HasData(
                new SpatialSridDesignRecord
                {
                    Id = 1,
                    Location = new Point(13.4050, 52.5200)
                    {
                        SRID = 4326,
                    },
                });
        });
    }
}

/// <summary>
/// Entity used by the generated spatial SRID migration model.
/// </summary>
public sealed class SpatialSridDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the SRID-constrained location.
    /// </summary>
    public Point Location { get; set; } = null!;
}

/// <summary>
/// Test context for generated system-time migration models.
/// </summary>
public sealed class SystemTimeDesignContext : DbContext
{
    /// <summary>
    /// Creates the test context.
    /// </summary>
    public SystemTimeDesignContext(
        DbContextOptions<SystemTimeDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<SystemTimeDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.ToTable(
                "TemporalRecords",
                table => table.IsTemporal(temporal =>
                {
                    temporal.UseHistoryTable("TemporalRecordsHistory");
                    temporal
                        .HasPeriodStart("ValidFrom")
                        .HasColumnName("valid_from");
                    temporal
                        .HasPeriodEnd("ValidTo")
                        .HasColumnName("valid_to");
                }));
            entity.OwnsOne(record => record.Details);
        });
    }
}

/// <summary>
/// Empty model used to verify initial temporal migration operations.
/// </summary>
public sealed class EmptyTemporalDesignContext : DbContext
{
    /// <summary>
    /// Creates the empty test context.
    /// </summary>
    public EmptyTemporalDesignContext(
        DbContextOptions<EmptyTemporalDesignContext> options
    ) : base(options) { }
}

/// <summary>
/// Entity used by the generated system-time migration model.
/// </summary>
public sealed class SystemTimeDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the table-split details.
    /// </summary>
    public SystemTimeDesignDetails Details { get; set; } = new();
}

/// <summary>
/// Owned details used by the generated system-time migration model.
/// </summary>
public sealed class SystemTimeDesignDetails
{
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description { get; set; } = null!;
}

/// <summary>
/// Test context for generated application-time migration models.
/// </summary>
public sealed class ApplicationTimeDesignContext : DbContext
{
    /// <summary>
    /// Creates the test context.
    /// </summary>
    public ApplicationTimeDesignContext(
        DbContextOptions<ApplicationTimeDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<ApplicationTimeDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.ToTable(
                "ApplicationTimeRecords",
                table => table.HasApplicationTimePeriod<ApplicationTimeDesignRecord>(applicationTime =>
                {
                    applicationTime.HasPeriodName("BusinessValidity");
                    applicationTime
                        .HasPeriodStart(record => record.BusinessFrom)
                        .HasColumnName("business_from");
                    applicationTime
                        .HasPeriodEnd(record => record.BusinessTo)
                        .HasColumnName("business_to");
                }));
        });
    }
}

/// <summary>
/// Entity used by the generated application-time migration model.
/// </summary>
public sealed class ApplicationTimeDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the inclusive application-time boundary.
    /// </summary>
    public DateTime BusinessFrom { get; set; }

    /// <summary>
    /// Gets or sets the exclusive application-time boundary.
    /// </summary>
    public DateTime BusinessTo { get; set; }
}

/// <summary>
/// Test context for generated bitemporal migration models.
/// </summary>
public sealed class BitemporalDesignContext : DbContext
{
    /// <summary>
    /// Creates the test context.
    /// </summary>
    public BitemporalDesignContext(
        DbContextOptions<BitemporalDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<BitemporalDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.ToTable(
                "BitemporalRecords",
                table =>
                {
                    table.IsTemporal(temporal =>
                    {
                        temporal
                            .HasPeriodStart("RecordedFrom")
                            .HasColumnName("recorded_from");
                        temporal
                            .HasPeriodEnd("RecordedTo")
                            .HasColumnName("recorded_to");
                    });
                    table.HasApplicationTimePeriod(applicationTime =>
                    {
                        applicationTime.HasPeriodName("BusinessValidity");
                        applicationTime
                            .HasPeriodStart("BusinessFrom")
                            .HasColumnName("business_from");
                        applicationTime
                            .HasPeriodEnd("BusinessTo")
                            .HasColumnName("business_to");
                        applicationTime.UseWithoutOverlaps();
                    });
                });
        });
    }
}

/// <summary>
/// Entity used by the generated bitemporal migration model.
/// </summary>
public sealed class BitemporalDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }
}

/// <summary>
/// Test context for generated provider-native Char36 migration models.
/// </summary>
public sealed class Char36DesignContext : DbContext
{
    /// <summary>
    /// Creates the test context.
    /// </summary>
    public Char36DesignContext(
        DbContextOptions<Char36DesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<Char36DesignDocument>(entity =>
        {
            entity.ToTable("Char36DesignDocuments");
            entity.HasKey(document => document.Id);
            entity
                .Property(document => document.Id)
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36)
                .UseMySqlClientGuidValueGeneration();
        });

        modelBuilder.Entity<Char36DesignRevision>(entity =>
        {
            entity.ToTable("Char36DesignRevisions");
            entity.HasKey(revision => revision.Id);
            entity
                .Property(revision => revision.DocumentId)
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
            entity
                .HasOne(revision => revision.Document)
                .WithMany(document => document.Revisions)
                .HasForeignKey(revision => revision.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>
/// Principal entity used by the generated Char36 migration model.
/// </summary>
public sealed class Char36DesignDocument
{
    /// <summary>
    /// Gets or sets the client-generated key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets the dependent revisions.
    /// </summary>
    public ICollection<Char36DesignRevision> Revisions { get; } = [];
}

/// <summary>
/// Dependent entity used by the generated Char36 migration model.
/// </summary>
public sealed class Char36DesignRevision
{
    /// <summary>
    /// Gets or sets the revision key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the principal Guid key.
    /// </summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// Gets or sets the principal navigation.
    /// </summary>
    public Char36DesignDocument Document { get; set; } = null!;
}

/// <summary>
/// Test context for generated relationship models using a context-level Char36 default.
/// </summary>
public sealed class DefaultChar36RelationshipDesignContext : DbContext
{
    /// <summary>
    /// Creates the relationship design context.
    /// </summary>
    public DefaultChar36RelationshipDesignContext(
        DbContextOptions<DefaultChar36RelationshipDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<DefaultChar36RelationshipARoot>(entity => entity.HasKey(item => item.Id));

        modelBuilder.Entity<DefaultChar36RelationshipBLeft>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity
                .HasOne(item => item.Root)
                .WithOne()
                .HasForeignKey<DefaultChar36RelationshipBLeft>(item => item.Id);
        });

        modelBuilder.Entity<DefaultChar36RelationshipCRight>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity
                .HasOne(item => item.Root)
                .WithOne()
                .HasForeignKey<DefaultChar36RelationshipCRight>(item => item.Id);
        });

        modelBuilder.Entity<DefaultChar36RelationshipZLeaf>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity
                .HasOne(item => item.Left)
                .WithMany()
                .HasForeignKey(item => item.ReferenceId);
            entity
                .HasOne(item => item.Right)
                .WithMany()
                .HasForeignKey(item => item.ReferenceId);
        });

        modelBuilder.Entity<DefaultChar36RelationshipZNullableLeaf>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity
                .HasOne(item => item.Left)
                .WithMany()
                .HasForeignKey(item => item.OptionalReferenceId);
            entity
                .HasOne(item => item.Right)
                .WithMany()
                .HasForeignKey(item => item.OptionalReferenceId);
        });
    }
}

/// <summary>
/// Root entity for the generated context-level Char36 relationship model.
/// </summary>
public sealed class DefaultChar36RelationshipARoot
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public Guid Id { get; set; }
}

/// <summary>
/// Left relationship branch for the generated context-level Char36 model.
/// </summary>
public sealed class DefaultChar36RelationshipBLeft
{
    /// <summary>
    /// Gets or sets the shared primary and foreign key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the root navigation.
    /// </summary>
    public DefaultChar36RelationshipARoot Root { get; set; } = null!;
}

/// <summary>
/// Right relationship branch for the generated context-level Char36 model.
/// </summary>
public sealed class DefaultChar36RelationshipCRight
{
    /// <summary>
    /// Gets or sets the shared primary and foreign key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the root navigation.
    /// </summary>
    public DefaultChar36RelationshipARoot Root { get; set; } = null!;
}

/// <summary>
/// Leaf entity whose GUID participates in both relationship branches.
/// </summary>
public sealed class DefaultChar36RelationshipZLeaf
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the GUID shared by both foreign keys.
    /// </summary>
    public Guid ReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the left branch navigation.
    /// </summary>
    public DefaultChar36RelationshipBLeft Left { get; set; } = null!;

    /// <summary>
    /// Gets or sets the right branch navigation.
    /// </summary>
    public DefaultChar36RelationshipCRight Right { get; set; } = null!;
}

/// <summary>
/// Optional leaf entity whose nullable GUID participates in both relationship branches.
/// </summary>
public sealed class DefaultChar36RelationshipZNullableLeaf
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the optional GUID shared by both foreign keys.
    /// </summary>
    public Guid? OptionalReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the optional left branch navigation.
    /// </summary>
    public DefaultChar36RelationshipBLeft? Left { get; set; }

    /// <summary>
    /// Gets or sets the optional right branch navigation.
    /// </summary>
    public DefaultChar36RelationshipCRight? Right { get; set; }
}

/// <summary>
/// Test context for mixed default and property-level Guid formats.
/// </summary>
public sealed class MixedGuidDesignContext : DbContext
{
    /// <summary>
    /// Creates the mixed Guid design context.
    /// </summary>
    public MixedGuidDesignContext(
        DbContextOptions<MixedGuidDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<MixedGuidDesignRecord>(entity =>
        {
            entity.ToTable("MixedGuidDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.BinaryReference)
                .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
            entity
                .HasIndex(record => new
                {
                    record.Name,
                    record.BinaryReference,
                })
                .HasDatabaseName("IX_MixedGuidDesignRecords_Name_BinaryReference")
                .HasPrefixLength(16, 0);
        });
    }
}

/// <summary>
/// Entity used by the mixed Guid design model.
/// </summary>
public sealed class MixedGuidDesignRecord
{
    /// <summary>
    /// Gets or sets the Char36 default key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the explicitly Binary16 value.
    /// </summary>
    public Guid BinaryReference { get; set; }

    /// <summary>
    /// Gets or sets the indexed text value.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Test context for provider-owned Guid seed values across supported storage formats.
/// </summary>
public sealed class GuidSeedDesignContext : DbContext
{
    /// <summary>
    /// Creates the Guid seed design context.
    /// </summary>
    public GuidSeedDesignContext(
        DbContextOptions<GuidSeedDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<GuidSeedDesignRecord>(entity =>
        {
            entity.ToTable("GuidSeedDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.ExplicitChar36)
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
            entity
                .Property(record => record.Binary16)
                .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
            entity
                .Property(record => record.DefaultChar36)
                .HasDefaultValue(new Guid("27caab1e-a588-4dcc-bace-fef7cf47e1fd"));
            entity
                .Property(record => record.DefaultBinary16)
                .HasMySqlGuidFormat(MySqlGuidFormat.Binary16)
                .HasDefaultValue(new Guid("5c855603-622d-4bee-b232-f74f63026caf"));
            entity
                .Property(record => record.OptionalDefaultChar36)
                .HasDefaultValue(new Guid("19bbd2b4-6a65-4469-bfaa-6ed3d4c19d81"));
            entity
                .Property(record => record.EmptyDefaultChar36)
                .HasDefaultValue(Guid.Empty);
            entity.HasData(
                new GuidSeedDesignRecord
                {
                    Id = new Guid("bf1da273-beed-4197-ab57-4cf8395244d4"),
                    ExplicitChar36 = new Guid("5153b4e4-0158-4641-b387-433631824557"),
                    Binary16 = new Guid("89a78261-ea26-494e-a520-b518f51ed3d1"),
                    OptionalChar36 = new Guid("1e15bed0-86cb-408e-b1cf-08952340a095"),
                    DefaultChar36 = new Guid("e120f559-9af1-427a-bb5a-ded4877796ac"),
                    DefaultBinary16 = new Guid("9baf398b-0c24-46fe-b841-d50037246dfc"),
                    OptionalDefaultChar36 = new Guid("3f5f76c1-8dd0-4462-af89-9ca8ee53706e"),
                    EmptyDefaultChar36 = new Guid("4e888833-56a7-40de-83e9-21ad53fb6d1a"),
                    Name = "seeded",
                },
                new GuidSeedDesignRecord
                {
                    Id = new Guid("a5e91c65-450d-47fa-9683-b6471d3df651"),
                    ExplicitChar36 = new Guid("1e326748-be2c-4856-bf8d-cffc79c192cf"),
                    Binary16 = new Guid("79cf7ff0-1bb0-4007-8ef4-b345386a6f41"),
                    OptionalChar36 = null,
                    DefaultChar36 = new Guid("bd45b993-2736-4592-905b-c6e8e3913177"),
                    DefaultBinary16 = new Guid("7af4be5f-2999-4c3e-a246-edb247ec8a7e"),
                    OptionalDefaultChar36 = null,
                    EmptyDefaultChar36 = Guid.Empty,
                    Name = "nullable",
                });
        });

        modelBuilder.Entity<GuidSeedDependentRecord>(entity =>
        {
            entity.ToTable("GuidSeedDependentRecords");
            entity.HasKey(record => record.Id);
            entity
                .HasOne(record => record.Principal)
                .WithMany(record => record.RequiredDependents)
                .HasForeignKey(record => record.PrincipalId)
                .OnDelete(DeleteBehavior.Cascade);
            entity
                .HasOne(record => record.OptionalPrincipal)
                .WithMany(record => record.OptionalDependents)
                .HasForeignKey(record => record.OptionalPrincipalId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasData(
                new GuidSeedDependentRecord
                {
                    Id = 1,
                    PrincipalId = new Guid("bf1da273-beed-4197-ab57-4cf8395244d4"),
                    OptionalPrincipalId = new Guid("a5e91c65-450d-47fa-9683-b6471d3df651"),
                    Name = "related",
                },
                new GuidSeedDependentRecord
                {
                    Id = 2,
                    PrincipalId = new Guid("a5e91c65-450d-47fa-9683-b6471d3df651"),
                    OptionalPrincipalId = null,
                    Name = "nullable-related",
                });
        });
    }
}

/// <summary>
/// Entity used by the provider-owned Guid seed design model.
/// </summary>
public sealed class GuidSeedDesignRecord
{
    /// <summary>
    /// Gets or sets the context-default Char36 key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets an explicitly configured Char36 value.
    /// </summary>
    public Guid ExplicitChar36 { get; set; }

    /// <summary>
    /// Gets or sets an explicitly configured Binary16 value.
    /// </summary>
    public Guid Binary16 { get; set; }

    /// <summary>
    /// Gets or sets an optional context-default Char36 value.
    /// </summary>
    public Guid? OptionalChar36 { get; set; }

    /// <summary>
    /// Gets or sets a value with a provider-owned Char36 default.
    /// </summary>
    public Guid DefaultChar36 { get; set; }

    /// <summary>
    /// Gets or sets a value with a provider-owned Binary16 default.
    /// </summary>
    public Guid DefaultBinary16 { get; set; }

    /// <summary>
    /// Gets or sets an optional value with a provider-owned Char36 default.
    /// </summary>
    public Guid? OptionalDefaultChar36 { get; set; }

    /// <summary>
    /// Gets or sets a value with an empty provider-owned Char36 default.
    /// </summary>
    public Guid EmptyDefaultChar36 { get; set; }

    /// <summary>
    /// Gets or sets the seed label.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets the dependents using the required foreign key.
    /// </summary>
    public ICollection<GuidSeedDependentRecord> RequiredDependents { get; } = [];

    /// <summary>
    /// Gets the dependents using the optional foreign key.
    /// </summary>
    public ICollection<GuidSeedDependentRecord> OptionalDependents { get; } = [];
}

/// <summary>
/// Minimal converted-Char36 design context without an unconverted Guid mapping
/// that could add the required model namespace as a side effect.
/// </summary>
public sealed class Char36SeedOnlyDesignContext : DbContext
{
    /// <summary>
    /// Creates the converted-Char36-only design context.
    /// </summary>
    public Char36SeedOnlyDesignContext(
        DbContextOptions<Char36SeedOnlyDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<Char36SeedOnlyDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.HasData(
                new Char36SeedOnlyDesignRecord
                {
                    Id = new Guid("ef538fc8-d6f4-46d1-8b4e-6c8b26ab8137"),
                    Name = "seeded",
                });
        });
    }
}

/// <summary>
/// Entity for the converted-Char36-only namespace regression.
/// </summary>
public sealed class Char36SeedOnlyDesignRecord
{
    /// <summary>
    /// Gets or sets the converted Char36 key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the seed label.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Design context whose only converted Guid is nested in a complex property.
/// </summary>
public sealed class Char36ComplexOnlyDesignContext : DbContext
{
    /// <summary>
    /// Creates the converted-Char36 complex-property design context.
    /// </summary>
    public Char36ComplexOnlyDesignContext(
        DbContextOptions<Char36ComplexOnlyDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<Char36ComplexOnlyDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.ComplexProperty(
                record => record.Details,
                complex =>
                {
                    complex
                        .Property(details => details.ExternalId)
                        .HasDefaultValue(new Guid("0517e651-62e5-4a93-845d-fd87b14e4363"));
                    complex
                        .Property(details => details.ApplicationId)
                        .HasConversion<string>()
                        .HasColumnType("varchar(36)");
                });
        });
    }
}

/// <summary>
/// Entity for the converted-Char36 complex-property namespace regression.
/// </summary>
public sealed class Char36ComplexOnlyDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the complex details.
    /// </summary>
    public Char36ComplexOnlyDesignDetails Details { get; set; } = new();
}

/// <summary>
/// Complex details containing the only converted Guid in the model.
/// </summary>
public sealed class Char36ComplexOnlyDesignDetails
{
    /// <summary>
    /// Gets or sets the converted identifier.
    /// </summary>
    public Guid ExternalId { get; set; }

    /// <summary>
    /// Gets or sets an application-converted negative control.
    /// </summary>
    public Guid ApplicationId { get; set; }
}

/// <summary>
/// Seeded dependent used to verify provider-owned Guid foreign keys.
/// </summary>
public sealed class GuidSeedDependentRecord
{
    /// <summary>
    /// Gets or sets the dependent key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the required provider-owned Char36 foreign key.
    /// </summary>
    public Guid PrincipalId { get; set; }

    /// <summary>
    /// Gets or sets the optional provider-owned Char36 foreign key.
    /// </summary>
    public Guid? OptionalPrincipalId { get; set; }

    /// <summary>
    /// Gets or sets the seed label.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the required principal navigation.
    /// </summary>
    public GuidSeedDesignRecord Principal { get; set; } = null!;

    /// <summary>
    /// Gets or sets the optional principal navigation.
    /// </summary>
    public GuidSeedDesignRecord? OptionalPrincipal { get; set; }
}

/// <summary>
/// Test context for an application-owned Guid-to-string seed converter.
/// </summary>
public sealed class ApplicationGuidSeedDesignContext : DbContext
{
    /// <summary>
    /// Creates the application-converter seed design context.
    /// </summary>
    public ApplicationGuidSeedDesignContext(
        DbContextOptions<ApplicationGuidSeedDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<ApplicationGuidSeedDesignRecord>(entity =>
        {
            entity.ToTable("ApplicationGuidSeedDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.Id)
                .HasConversion<string>()
                .HasDefaultValue(new Guid("89a78261-ea26-494e-a520-b518f51ed3d1"));
            entity.HasData(
                new ApplicationGuidSeedDesignRecord
                {
                    Id = new Guid("89a78261-ea26-494e-a520-b518f51ed3d1"),
                    Name = "application-converted",
                });
        });
    }
}

/// <summary>
/// Entity used by the application-owned Guid converter seed model.
/// </summary>
public sealed class ApplicationGuidSeedDesignRecord
{
    /// <summary>
    /// Gets or sets the application-converted key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the seed label.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Test context for provider-owned converter design-time models.
/// </summary>
public sealed class ProviderConverterDesignContext : DbContext
{
    /// <summary>
    /// Creates the provider-owned converter design context.
    /// </summary>
    public ProviderConverterDesignContext(
        DbContextOptions<ProviderConverterDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<ProviderConverterDesignRecord>(entity =>
        {
            entity.ToTable("ProviderConverterDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.Element)
                .HasDefaultValue(JsonElement.Parse("""{"kind":"default-element"}"""));
            entity
                .Property(record => record.Document)
                .IsRequired()
                .HasDefaultValue(JsonDocument.Parse("""{"kind":"default-document"}"""));
            entity
                .Property(record => record.Node)
                .IsRequired()
                .HasDefaultValue(JsonNode.Parse("""{"kind":"default-node"}"""));
            entity
                .Property(record => record.ObjectValue)
                .IsRequired()
                .HasDefaultValue((JsonObject)JsonNode.Parse("""{"kind":"default-object"}""")!);
            entity
                .Property(record => record.Array)
                .IsRequired()
                .HasDefaultValue((JsonArray)JsonNode.Parse("""["default-array"]""")!);
            entity.Property(record => record.Version).IsRowVersion();
            entity.HasData(
                new
                {
                    Id = 1,
                    Element = JsonElement.Parse("""{"kind":"element","value":1}"""),
                    Document = JsonDocument.Parse("""{"kind":"document","value":2}"""),
                    Node = JsonNode.Parse("""{"kind":"node","value":3}"""),
                    ObjectValue = (JsonObject)JsonNode.Parse("""{"kind":"object","value":4}""")!,
                    Array = (JsonArray)JsonNode.Parse("""["array",5,true]""")!,
                    Version = MySqlBytesToDateTimeConverter.ToBytes(
                        new DateTime(2026, 8, 31, 12, 34, 56, DateTimeKind.Utc)),
                });
        });
    }
}

/// <summary>
/// Entity used by the provider-owned converter design model.
/// </summary>
public sealed class ProviderConverterDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the JSON element.
    /// </summary>
    public System.Text.Json.JsonElement Element { get; set; }

    /// <summary>
    /// Gets or sets the JSON document.
    /// </summary>
    public System.Text.Json.JsonDocument? Document { get; set; }

    /// <summary>
    /// Gets or sets the JSON node.
    /// </summary>
    public System.Text.Json.Nodes.JsonNode? Node { get; set; }

    /// <summary>
    /// Gets or sets the JSON object.
    /// </summary>
    public System.Text.Json.Nodes.JsonObject? ObjectValue { get; set; }

    /// <summary>
    /// Gets or sets the JSON array.
    /// </summary>
    public System.Text.Json.Nodes.JsonArray? Array { get; set; }

    /// <summary>
    /// Gets or sets the row version.
    /// </summary>
    public byte[] Version { get; set; } = [];
}

/// <summary>
/// Test context for generated MySQL-family invisible-column migration models.
/// </summary>
public sealed class InvisibleColumnDesignContext : DbContext
{
    /// <summary>
    /// Creates the test context.
    /// </summary>
    public InvisibleColumnDesignContext(
        DbContextOptions<InvisibleColumnDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<InvisibleColumnDesignRecord>(entity =>
        {
            entity.ToTable("InvisibleColumnDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.HiddenValue)
                .HasDefaultValue(string.Empty)
                .IsInvisible();
            entity
                .Property(record => record.VisibleValue)
                .IsInvisible(false);
        });
    }
}

/// <summary>
/// Entity used by the generated invisible-column migration model.
/// </summary>
public sealed class InvisibleColumnDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the hidden value.
    /// </summary>
    public string HiddenValue { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the explicitly visible value.
    /// </summary>
    public string VisibleValue { get; set; } = string.Empty;
}

/// <summary>
/// Test context for generated entity-splitting migration models.
/// </summary>
public sealed class EntitySplitDesignContext : DbContext
{
    /// <summary>
    /// Creates the entity-splitting design context.
    /// </summary>
    public EntitySplitDesignContext(
        DbContextOptions<EntitySplitDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<EntitySplitDesignRecord>(entity =>
        {
            entity.ToTable("EntitySplitDesignRecords");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Id).UseMySqlAutoIncrementColumn();
            entity.SplitToTable(
                "EntitySplitDesignDetails",
                split => split.Property(record => record.Description));
        });
    }
}

/// <summary>
/// Entity used by the generated entity-splitting migration model.
/// </summary>
public sealed class EntitySplitDesignRecord
{
    /// <summary>
    /// Gets or sets the generated key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the property held by the principal table.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the property held by the secondary table.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Test context for discriminator-completeness design-time models.
/// </summary>
public sealed class DiscriminatorCompletenessDesignContext : DbContext
{
    /// <summary>
    /// Creates the discriminator-completeness design context.
    /// </summary>
    public DiscriminatorCompletenessDesignContext(
        DbContextOptions<DiscriminatorCompletenessDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<IncompleteDiscriminatorDesignRecord>(entity =>
        {
            entity.ToTable("IncompleteDiscriminatorDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .HasDiscriminator<string>("Discriminator")
                .HasValue<IncompleteKnownDiscriminatorDesignRecord>("Known")
                .IsComplete(false);
        });

        modelBuilder.Entity<CompleteDiscriminatorDesignRecord>(entity =>
        {
            entity.ToTable("CompleteDiscriminatorDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .HasDiscriminator<string>("Discriminator")
                .HasValue<CompleteKnownDiscriminatorDesignRecord>("Known");
        });

        modelBuilder.Entity<ConvertedDiscriminatorDesignRecord>(entity =>
        {
            entity.ToTable("ConvertedDiscriminatorDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .HasDiscriminator<DesignDiscriminator>("Discriminator")
                .HasValue<KnownConvertedDiscriminatorDesignRecord>(DesignDiscriminator.Known)
                .IsComplete(false);
            entity
                .Property<DesignDiscriminator>("Discriminator")
                .HasConversion<DesignEnumValueConverter<DesignDiscriminator>>()
                .HasMaxLength(1)
                .IsFixedLength();
        });

        modelBuilder.Entity<KnownChildConfiguredConvertedDiscriminatorDesignRecord>(entity =>
        {
            entity.HasBaseType<ChildConfiguredConvertedDiscriminatorDesignRecord>();
            entity
                .HasDiscriminator<DesignDiscriminator>(
                    nameof(ChildConfiguredConvertedDiscriminatorDesignRecord.Discriminator))
                .HasValue<KnownChildConfiguredConvertedDiscriminatorDesignRecord>(DesignDiscriminator.Known);
        });

        modelBuilder.Entity<ChildConfiguredConvertedDiscriminatorDesignRecord>(entity =>
        {
            entity.ToTable("ChildConfiguredConvertedDiscriminatorDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.Discriminator)
                .HasConversion<DesignEnumValueConverter<DesignDiscriminator>>()
                .HasMaxLength(1)
                .IsFixedLength();
            entity.Metadata.SetDiscriminatorMappingComplete(false);
        });

        modelBuilder.Entity<IntDiscriminatorDesignRecord>(entity =>
        {
            entity.ToTable("IntDiscriminatorDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .HasDiscriminator<int>("Discriminator")
                .HasValue<KnownIntDiscriminatorDesignRecord>(7);
        });

        modelBuilder.Entity<EnumDiscriminatorDesignRecord>(entity =>
        {
            entity.ToTable("EnumDiscriminatorDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .HasDiscriminator<NativeDesignDiscriminator>("Discriminator")
                .HasValue<KnownEnumDiscriminatorDesignRecord>(NativeDesignDiscriminator.Known);
        });
    }
}

/// <summary>
/// Base entity for the incomplete discriminator design model.
/// </summary>
public abstract class IncompleteDiscriminatorDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }
}

/// <summary>
/// Known entity in the incomplete discriminator design model.
/// </summary>
public sealed class IncompleteKnownDiscriminatorDesignRecord : IncompleteDiscriminatorDesignRecord;

/// <summary>
/// Base entity for the complete discriminator design model.
/// </summary>
public abstract class CompleteDiscriminatorDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }
}

/// <summary>
/// Known entity in the complete discriminator design model.
/// </summary>
public sealed class CompleteKnownDiscriminatorDesignRecord : CompleteDiscriminatorDesignRecord;

/// <summary>
/// Base entity for the converted discriminator design model.
/// </summary>
public abstract class ConvertedDiscriminatorDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }
}

/// <summary>
/// Known entity in the converted discriminator design model.
/// </summary>
public sealed class KnownConvertedDiscriminatorDesignRecord : ConvertedDiscriminatorDesignRecord;

/// <summary>
/// Base entity whose converted discriminator value is configured from a derived entity builder.
/// </summary>
public abstract class ChildConfiguredConvertedDiscriminatorDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the discriminator value stored through an application converter.
    /// </summary>
    public DesignDiscriminator Discriminator { get; set; }
}

/// <summary>
/// Known entity whose discriminator value is configured from its own entity builder.
/// </summary>
public sealed class KnownChildConfiguredConvertedDiscriminatorDesignRecord
    : ChildConfiguredConvertedDiscriminatorDesignRecord;

/// <summary>
/// Model-side values for the converted discriminator design model.
/// </summary>
public enum DesignDiscriminator
{
    /// <summary>
    /// Identifies the mapped derived type.
    /// </summary>
    [JsonStringEnumMemberName("K")]
    Known,

    /// <summary>
    /// Represents a discriminator value unknown to the model hierarchy.
    /// </summary>
    [EnumMember(Value = "F")]
    Future,
}

internal sealed class DesignEnumValueConverter<TEnum> : ValueConverter<TEnum, string>
    where TEnum : struct, Enum
{
    public DesignEnumValueConverter()
        : base(
            value => GetName(value),
            value => GetValue(value)) { }

    private static string GetName(
        TEnum value
    )
    {
        var member = typeof(TEnum)
            .GetMember(value.ToString())
            .Single();
        var jsonName = member.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name;
        var enumMemberName = member.GetCustomAttribute<EnumMemberAttribute>()?.Value;

        return jsonName ?? enumMemberName ?? value.ToString();
    }

    private static TEnum GetValue(
        string value
    ) => Enum
        .GetValues<TEnum>()
        .Single(candidate => string.Equals(GetName(candidate), value, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Base entity for the native integer discriminator design model.
/// </summary>
public abstract class IntDiscriminatorDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }
}

/// <summary>
/// Known entity in the native integer discriminator design model.
/// </summary>
public sealed class KnownIntDiscriminatorDesignRecord : IntDiscriminatorDesignRecord;

/// <summary>
/// Base entity for the native enum discriminator design model.
/// </summary>
public abstract class EnumDiscriminatorDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }
}

/// <summary>
/// Known entity in the native enum discriminator design model.
/// </summary>
public sealed class KnownEnumDiscriminatorDesignRecord : EnumDiscriminatorDesignRecord;

/// <summary>
/// Values for the native enum discriminator design model.
/// </summary>
public enum NativeDesignDiscriminator
{
    /// <summary>
    /// Identifies the mapped derived type.
    /// </summary>
    Known = 7,
}
