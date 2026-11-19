namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies that direct-write columns are resolved by finalized property identity.</summary>
public sealed class MySqlDirectWriteTargetTests
{
    /// <summary>Renamed CLR, shadow and field-only properties retain their actual physical columns.</summary>
    /// <param name="propertyName">The mapped EF property name.</param>
    /// <param name="columnName">The explicitly renamed physical column.</param>
    /// <param name="shadow">Whether the mapping has no CLR member.</param>
    /// <param name="fieldOnly">Whether the mapping uses a field without a CLR property.</param>
    [Theory]
    [InlineData(nameof(MappedRow.Value), "stored_value", false, false)]
    [InlineData("ShadowValue", "shadow_value", true, false)]
    [InlineData(nameof(MappedRow.FieldValue), "field_value", false, true)]
    public void Mapped_property_identity_resolves_its_actual_column(
        string propertyName,
        string columnName,
        bool shadow,
        bool fieldOnly
    )
    {
        // Arrange
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(MappedRow))!;
        var property = entity.FindProperty(propertyName)!;
        var target = MySqlDirectWriteTarget.Resolve(entity);

        // Act
        var column = target.RequireColumn(property);

        // Assert
        Assert.Equal(columnName, column.Name);
        Assert.NotEqual(property.Name, column.Name);
        Assert.Same(target.Table.FindColumn(columnName), column);
        Assert.Same(target.Table, column.Table);
        Assert.Contains(column.PropertyMappings, mapping => ReferenceEquals(mapping.Property, property));
        Assert.Equal(shadow, property.IsShadowProperty());
        Assert.Equal(fieldOnly, property.PropertyInfo is null && property.FieldInfo is not null);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Matching logical and store names cannot substitute a foreign or view-only property identity.</summary>
    /// <param name="foreignType">The entity whose property is not mapped to the target table.</param>
    [Theory]
    [InlineData(typeof(ForeignRow))]
    [InlineData(typeof(ViewRow))]
    public void Property_outside_the_target_table_rejects_despite_matching_names(
        Type foreignType
    )
    {
        // Arrange
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(MappedRow))!;
        var foreignEntity = context.Model.FindEntityType(foreignType)!;
        var property = foreignEntity.FindProperty(nameof(MappedRow.Value))!;
        var target = MySqlDirectWriteTarget.Resolve(entity);

        // Act
        var error = Record.Exception(() => target.RequireColumn(property));

        // Assert
        Assert.Contains("not mapped to the direct-write target",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(nameof(MappedRow.Value), property.Name);
        Assert.NotNull(target.Table.FindColumn(property.Name));
        Assert.NotNull(target.Table.FindColumn("stored_value"));
        Assert.NotSame(entity, foreignEntity);

        if (foreignType == typeof(ViewRow))
        {
            Assert.Empty(property.GetTableColumnMappings());
            Assert.Equal("stored_value",
                Assert.Single(property.GetViewColumnMappings())
                    .Column.Name);
        }
        else
        {
            var foreignColumn = Assert.Single(property.GetTableColumnMappings())
                .Column;

            Assert.Equal("stored_value", foreignColumn.Name);
            Assert.NotSame(target.Table, foreignColumn.Table);
        }

        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Independent models cannot exchange properties even when CLR, table and column names match.</summary>
    [Fact]
    public void Property_from_an_independent_model_rejects_despite_identical_mapping_names()
    {
        // Arrange
        using var context = CreateContext();
        using var foreignContext = CreateContext();
        var entity = context.Model.FindEntityType(typeof(MappedRow))!;
        var foreignEntity = foreignContext.Model.FindEntityType(typeof(MappedRow))!;
        var property = foreignEntity.FindProperty(nameof(MappedRow.Value))!;
        var foreignColumn = Assert.Single(property.GetTableColumnMappings())
            .Column;

        var target = MySqlDirectWriteTarget.Resolve(entity);

        // Act
        var error = Record.Exception(() => target.RequireColumn(property));

        // Assert
        Assert.Contains("not mapped to the direct-write target",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.NotSame(context.Model, foreignContext.Model);
        Assert.NotSame(entity, foreignEntity);
        Assert.Same(entity.ClrType, foreignEntity.ClrType);
        Assert.Equal(target.Table.Name, foreignColumn.Table.Name);
        Assert.Equal("stored_value", foreignColumn.Name);
        Assert.NotNull(target.Table.FindColumn(property.Name));
        Assert.NotNull(target.Table.FindColumn(foreignColumn.Name));
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);

        Assert.Equal(ConnectionState.Closed,
            foreignContext.Database.GetDbConnection()
                .State);
    }

    /// <summary>Null property input retains its precise argument contract before metadata lookup.</summary>
    [Fact]
    public void Null_property_rejects_before_column_lookup()
    {
        // Arrange
        using var context = CreateContext();
        var target = MySqlDirectWriteTarget.Resolve(context.Model.FindEntityType(typeof(MappedRow))!);

        // Act
        var error = Record.Exception(() => target.RequireColumn(null!));

        // Assert
        Assert.Equal("property",
            Assert.IsType<ArgumentNullException>(error)
                .ParamName);

        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Creates independent metadata with immediate failure on accidental database access.</summary>
    private static MappingContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MappingContext>()
            .UseMySql("Server=127.0.0.1;Port=1;Database=direct_write_mapping;User ID=unused",
                MySqlServerVersion.MySql(new Version(8, 4, 13)))

            // WHY: Separate models expose accidental name-based lookup across otherwise identical metadata.
            .EnableServiceProviderCaching(false)
            .AddInterceptors(new RejectDatabaseIo())
            .Options;

        return new MappingContext(options);
    }

    /// <summary>Declares renamed columns and deliberate logical/store-name collisions.</summary>
    private sealed class MappingContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public MappingContext(
            DbContextOptions<MappingContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<MappedRow>(entity =>
            {
                entity.ToTable("MappedRows");
                entity
                    .Property(row => row.Value)
                    .HasColumnName("stored_value");

                entity
                    .Property<string>("ShadowValue")
                    .HasColumnName("shadow_value");

                entity
                    .Property<string>(nameof(MappedRow.FieldValue))
                    .HasField(nameof(MappedRow.FieldValue))
                    .UsePropertyAccessMode(PropertyAccessMode.Field)
                    .HasColumnName("field_value");

                // WHY: Both a property's logical name and its foreign store name must collide
                // with real target columns, so either kind of name-only fallback would be exposed.
                entity
                    .Property(row => row.NameEcho)
                    .HasColumnName(nameof(MappedRow.Value));
            });

            modelBuilder.Entity<ForeignRow>(entity =>
            {
                entity.ToTable("ForeignRows");
                entity
                    .Property(row => row.Value)
                    .HasColumnName("stored_value");
            });

            modelBuilder.Entity<ViewRow>(entity =>
            {
                entity
                    .HasNoKey()
                    .ToView("ViewRows");

                entity
                    .Property(row => row.Value)
                    .HasColumnName("stored_value");
            });
        }
    }

    /// <summary>Provides one ordinary, one shadow and one field-only property mapping.</summary>
    private sealed class MappedRow
    {
        /// <summary>Provides field-only storage without a CLR property.</summary>
        public string FieldValue = string.Empty;

        /// <summary>Gets or sets the mapped key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the renamed CLR property.</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>Gets or sets the property whose store name collides with another logical name.</summary>
        public string NameEcho { get; set; } = string.Empty;
    }

    /// <summary>Provides matching names on a different physical table.</summary>
    private sealed class ForeignRow
    {
        /// <summary>Gets or sets the mapped key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the colliding property.</summary>
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>Provides view metadata with no physical table-column mapping.</summary>
    private sealed class ViewRow
    {
        /// <summary>Gets or sets the colliding view property.</summary>
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>Rejects accidental I/O immediately instead of relying on connection timeouts.</summary>
    private sealed class RejectDatabaseIo : DbConnectionInterceptor
    {
        /// <inheritdoc />
        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result
        ) => throw new InvalidOperationException("Direct-write mapping tests must not open a database connection.");

        /// <inheritdoc />
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Direct-write mapping tests must not open a database connection.");
    }
}
