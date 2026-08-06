using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers provider identity-column classification at the update SQL boundary.
/// </summary>
public sealed class MySqlUpdateSqlGeneratorTests
{
    /// <summary>
    /// Verifies every key, read, write, property, and value-generation branch
    /// that decides whether generated values use LAST_INSERT_ID().
    /// </summary>
    [Fact]
    public void Identity_classification_requires_the_complete_generated_key_shape()
    {
        using var context = new IdentityContext(CreateOptions());
        var entityType = context.Model.FindEntityType(typeof(IdentityEntity))!;
        var autoIncrementProperty = entityType.FindProperty(nameof(IdentityEntity.Id))!;
        var neverGeneratedProperty = entityType.FindProperty(nameof(IdentityEntity.ExplicitId))!;
        var computedProperty = entityType.FindProperty(nameof(IdentityEntity.Computed))!;

        Assert.False(IsIdentity(autoIncrementProperty, isKey: false, isRead: true, isWrite: false));
        Assert.False(IsIdentity(autoIncrementProperty, isKey: true, isRead: false, isWrite: false));
        Assert.False(IsIdentity(autoIncrementProperty, isKey: true, isRead: true, isWrite: true));
        Assert.True(IsIdentity(property: null, isKey: true, isRead: true, isWrite: false));
        Assert.True(IsIdentity(autoIncrementProperty, isKey: true, isRead: true, isWrite: false));
        Assert.True(IsIdentity(neverGeneratedProperty, isKey: true, isRead: true, isWrite: false));
        Assert.False(IsIdentity(computedProperty, isKey: true, isRead: true, isWrite: false));
    }

    private static bool IsIdentity(
        IProperty? property,
        bool isKey,
        bool isRead,
        bool isWrite
    )
    {
        var typeMapping = property?.GetRelationalTypeMapping() ?? IntTypeMapping.Default;
        var parameters = new ColumnModificationParameters(
            columnName: property?.Name ?? "ShadowId",
            originalValue: null,
            value: null,
            property,
            columnType: typeMapping.StoreType,
            typeMapping,
            read: isRead,
            write: isWrite,
            key: isKey,
            condition: false,
            sensitiveLoggingEnabled: false,
            isNullable: false);

        return MySqlUpdateSqlGenerator.IsIdentityColumn(new ColumnModification(parameters));
    }

    private static DbContextOptions<IdentityContext> CreateOptions() => new DbContextOptionsBuilder<IdentityContext>()
        .UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)))
        .Options;

    private sealed class IdentityContext : DbContext
    {
        public IdentityContext(
            DbContextOptions<IdentityContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<IdentityEntity>(entity =>
            {
                entity.HasKey(candidate => candidate.Id);
                entity
                    .Property(candidate => candidate.ExplicitId)
                    .ValueGeneratedNever();
                entity
                    .Property(candidate => candidate.Computed)
                    .ValueGeneratedOnAddOrUpdate();
            });
        }
    }

    private sealed class IdentityEntity
    {
        public int Id { get; set; }

        public int ExplicitId { get; set; }

        public int Computed { get; set; }
    }
}
