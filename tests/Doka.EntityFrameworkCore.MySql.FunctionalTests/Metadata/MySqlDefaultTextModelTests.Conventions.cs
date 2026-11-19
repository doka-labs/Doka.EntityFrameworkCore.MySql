namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies explicit user conventions that run after provider model-finalizing conventions.</summary>
public sealed partial class MySqlDefaultTextModelTests
{
    /// <summary>A later compatible charset restores the fallback removed by a separate earlier convention.</summary>
    [Fact]
    public void Separate_late_charset_conventions_restore_a_now_compatible_collation_fallback()
    {
        // Arrange
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<RestoredCharsetContext>();
        builder.UseMySql(ConnectionString,
            MySqlServerVersion.MariaDb(new Version(12, 3, 0)),
            options => options
                .DefaultCharSet("utf8mb4")
                .DefaultCollation("utf8mb4_unicode_ci"));

        using var context = new RestoredCharsetContext(builder.Options);

        // Act
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        var sql = context.Database.GenerateCreateScript();

        // Assert
        Assert.Equal("utf8mb4", model.GetMySqlCharSet());
        Assert.Equal("utf8mb4_unicode_ci", model.GetCollation());
        Assert.Contains("utf8mb4_unicode_ci", sql, StringComparison.Ordinal);
    }

    /// <summary>A later compatible collation restores the fallback removed by a separate earlier convention.</summary>
    [Fact]
    public void Separate_late_collation_conventions_restore_a_now_compatible_charset_fallback()
    {
        // Arrange
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<RestoredCollationContext>();
        builder.UseMySql(ConnectionString,
            MySqlServerVersion.MariaDb(new Version(12, 3, 0)),
            options => options
                .DefaultCharSet("utf8mb4")
                .DefaultCollation("utf8mb4_unicode_ci"));

        using var context = new RestoredCollationContext(builder.Options);

        // Act
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        var sql = context.Database.GenerateCreateScript();

        // Assert
        Assert.Equal("utf8mb4", model.GetMySqlCharSet());
        Assert.Equal("utf8mb4_bin", model.GetCollation());
        Assert.Contains("CHARACTER SET = utf8mb4", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("utf8mb4_bin", sql, StringComparison.Ordinal);
    }

    /// <summary>A later explicit charset drops an incompatible weak collation fallback.</summary>
    [Fact]
    public void Later_explicit_charset_convention_does_not_keep_an_incompatible_fallback()
    {
        // Arrange
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<LateCharsetContext>();
        builder.UseMySql(ConnectionString,
            MySqlServerVersion.MariaDb(new Version(12, 3, 0)),
            options => options
                .DefaultCharSet("utf8mb4")
                .DefaultCollation("utf8mb4_unicode_ci"));

        using var context = new LateCharsetContext(builder.Options);

        // Act
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        var sql = context.Database.GenerateCreateScript();

        // Assert
        Assert.Equal("latin1", model.GetMySqlCharSet());
        Assert.Null(model.GetCollation());
        Assert.Null(model.FindAnnotation(RelationalAnnotationNames.Collation));
        Assert.Contains("latin1", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("utf8mb4_unicode_ci", sql, StringComparison.Ordinal);
    }

    /// <summary>A later explicit collation drops an incompatible weak charset fallback.</summary>
    [Fact]
    public void Later_explicit_collation_convention_does_not_keep_an_incompatible_fallback()
    {
        // Arrange
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<LateCollationContext>();
        builder.UseMySql(ConnectionString,
            MySqlServerVersion.MariaDb(new Version(12, 3, 0)),
            options => options
                .DefaultCharSet("utf8mb4")
                .DefaultCollation("utf8mb4_unicode_ci"));

        using var context = new LateCollationContext(builder.Options);

        // Act
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        var sql = context.Database.GenerateCreateScript();

        // Assert
        Assert.Null(model.GetMySqlCharSet());
        Assert.Equal("latin1_swedish_ci", model.GetCollation());
        Assert.Null(model.FindAnnotation(MySqlAnnotationNames.CharSet));
        Assert.Contains("latin1_swedish_ci", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CHARACTER SET utf8mb4", sql, StringComparison.Ordinal);
    }

    /// <summary>Conflicting stronger facets from a late user convention are never repaired by defaults.</summary>
    [Fact]
    public void Late_explicit_conflicting_pair_keeps_its_schema_validation_failure()
    {
        // Arrange
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<LateConflictingPairContext>();
        builder.UseMySql(ConnectionString,
            MySqlServerVersion.MariaDb(new Version(12, 3, 0)),
            options => options
                .DefaultCharSet("utf8mb4")
                .DefaultCollation("utf8mb4_unicode_ci"));

        using var context = new LateConflictingPairContext(builder.Options);

        // Act
        var exception = Record.Exception(context.Database.GenerateCreateScript);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>Missing runtime receipt metadata cannot be treated as a verified compatible model.</summary>
    /// <param name="runtime">Whether to supply the runtime representation instead of the design model.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Supplied_model_missing_required_text_facets_is_rejected(
        bool runtime
    )
    {
        // Arrange
        using var source = CreateContext(null, null);
        var model = runtime
            ? source.Model
            : source.GetService<IDesignTimeModel>()
                .Model;

        void act()
        {
            using var target = CreateContext("utf8mb4", "utf8mb4_unicode_ci", supplied: model);

            _ = target.Model;
        }

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    private sealed class LateCharsetContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public LateCharsetContext(
            DbContextOptions<LateCharsetContext> options
        ) : base(options) { }

        protected override void ConfigureConventions(
            ModelConfigurationBuilder configurationBuilder
        ) => configurationBuilder.Conventions.Add(_ => new LateTextConvention(charSet: true));
    }

    private sealed class LateCollationContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public LateCollationContext(
            DbContextOptions<LateCollationContext> options
        ) : base(options) { }

        protected override void ConfigureConventions(
            ModelConfigurationBuilder configurationBuilder
        ) => configurationBuilder.Conventions.Add(_ => new LateTextConvention(charSet: false));
    }

    private sealed class LateTextConvention : IModelFinalizingConvention
    {
        private readonly bool _charSet;

        /// <summary>Initializes the test helper with its supplied dependencies and values.</summary>
        public LateTextConvention(
            bool charSet
        )
        {
            _charSet = charSet;
        }

        public void ProcessModelFinalizing(
            IConventionModelBuilder modelBuilder,
            IConventionContext<IConventionModelBuilder> context
        )
        {
            if (_charSet)
            {
                modelBuilder.HasAnnotation(MySqlAnnotationNames.CharSet, "latin1", fromDataAnnotation: true);
            }
            else
            {
                modelBuilder.UseCollation("latin1_swedish_ci", fromDataAnnotation: true);
            }
        }
    }

    private sealed class LateConflictingPairContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public LateConflictingPairContext(
            DbContextOptions<LateConflictingPairContext> options
        ) : base(options) { }

        protected override void ConfigureConventions(
            ModelConfigurationBuilder configurationBuilder
        ) => configurationBuilder.Conventions.Add(_ => new LateConflictingPairConvention());
    }

    private sealed class LateConflictingPairConvention : IModelFinalizingConvention
    {
        public void ProcessModelFinalizing(
            IConventionModelBuilder modelBuilder,
            IConventionContext<IConventionModelBuilder> context
        )
        {
            modelBuilder.HasAnnotation(MySqlAnnotationNames.CharSet, "latin1", fromDataAnnotation: true);
            modelBuilder.UseCollation("utf8mb4_bin", fromDataAnnotation: true);
        }
    }

    private sealed class RestoredCharsetContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public RestoredCharsetContext(
            DbContextOptions<RestoredCharsetContext> options
        ) : base(options) { }

        protected override void ConfigureConventions(
            ModelConfigurationBuilder configurationBuilder
        )
        {
            configurationBuilder.Conventions.Add(_ => new RestoringTextConvention(charSet: true, "latin1"));
            configurationBuilder.Conventions.Add(_ => new RestoringTextConvention(charSet: true, "utf8mb4"));
        }
    }

    private sealed class RestoredCollationContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public RestoredCollationContext(
            DbContextOptions<RestoredCollationContext> options
        ) : base(options) { }

        protected override void ConfigureConventions(
            ModelConfigurationBuilder configurationBuilder
        )
        {
            configurationBuilder.Conventions.Add(_ => new RestoringTextConvention(charSet: false, "latin1_swedish_ci"));
            configurationBuilder.Conventions.Add(_ => new RestoringTextConvention(charSet: false, "utf8mb4_bin"));
        }
    }

    private sealed class RestoringTextConvention : IModelFinalizingConvention
    {
        private readonly bool _charSet;
        private readonly string _value;

        /// <summary>Initializes the test helper with its supplied dependencies and values.</summary>
        public RestoringTextConvention(
            bool charSet,
            string value
        )
        {
            _charSet = charSet;
            _value = value;
        }

        public void ProcessModelFinalizing(
            IConventionModelBuilder modelBuilder,
            IConventionContext<IConventionModelBuilder> context
        )
        {
            if (_charSet)
            {
                modelBuilder.HasAnnotation(MySqlAnnotationNames.CharSet, _value, fromDataAnnotation: true);
            }
            else
            {
                modelBuilder.UseCollation(_value, fromDataAnnotation: true);
            }
        }
    }
}
