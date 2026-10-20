using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

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
