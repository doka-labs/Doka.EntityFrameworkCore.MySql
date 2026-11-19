namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies that native JSON membership preserves mapped function identity and provider-side representations.</summary>
public sealed class MySqlJsonMembershipFunctionIdentityTests
{
    /// <summary>A schema-qualified custom function sharing a decoder name retains its own server semantics.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB instead of MySQL.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_unquote_function_is_not_replaced_by_native_document_membership(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Where(row => row
            .Values
            .Select(value => CustomUnquote(value))
            .Contains("mapped:12"));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("`custom_functions`.`JSON_UNQUOTE`(`v`.`value`)", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(`r`.`Values`, '$[*]'", sql, StringComparison.Ordinal);
        Assert.Contains("'mapped:12'", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("(`custom_functions`.`JSON_UNQUOTE`(`v`.`value`)) <=> ('mapped:12')",
            sql,
            StringComparison.Ordinal);

        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>Checking the outer builtin alone cannot discard a schema-qualified custom inner function.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB instead of MySQL.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_quote_under_builtin_unquote_retains_the_complete_projection(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Where(row => row
            .Values
            .Select(value => BuiltinUnquote(CustomQuote(value)))
            .Contains("mapped:12"));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("JSON_UNQUOTE(`custom_functions`.`JSON_QUOTE`(`v`.`value`))", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(`r`.`Values`, '$[*]'", sql, StringComparison.Ordinal);
        Assert.Contains("'mapped:12'", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("(JSON_UNQUOTE(`custom_functions`.`JSON_QUOTE`(`v`.`value`))) <=> ('mapped:12')",
            sql,
            StringComparison.Ordinal);

        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>A real builtin converting numeric members into strings is not an identity of the original JSON numbers.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB instead of MySQL.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Builtin_numeric_to_string_projection_is_not_replaced_by_numeric_document_membership(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Where(row => row
            .Values
            .Select(value => BuiltinUnquoteNumber(value))
            .Contains("12"));

        // Act
        var sql = query.ToQueryString();

        // Assert
        // WHY: JSON_UNQUOTE(12) yields text; JSON_CONTAINS over the original numeric array
        // would compare that text with JSON numbers and silently change the predicate.
        Assert.Contains("JSON_UNQUOTE(`v`.`value`)", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(`r`.`Values`, '$[*]'", sql, StringComparison.Ordinal);
        Assert.Contains("'12'", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("(JSON_UNQUOTE(`v`.`value`)) <=> ('12')", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>Ordinary custom function names retain the same mapped projection contract as builtin-shaped names.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB instead of MySQL.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ordinary_custom_function_projection_remains_in_the_membership_subquery(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Where(row => row
            .Values
            .Select(value => CustomFormat(value))
            .Contains("mapped:12"));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("`custom_functions`.`FORMAT_MEMBER`(`v`.`value`)", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(`r`.`Values`, '$[*]'", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("(`custom_functions`.`FORMAT_MEMBER`(`v`.`value`)) <=> ('mapped:12')",
            sql,
            StringComparison.Ordinal);

        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>A mapped function with a value and literal argument retains both arguments in the correlated scalar predicate.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB instead of MySQL.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_function_with_literal_argument_retains_its_complete_correlated_projection(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Where(row => row
            .Values
            .Select(value => CustomPrefix(value, "mapped:"))
            .Contains("mapped:12"));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(`r`.`Values`, '$[*]'", sql, StringComparison.Ordinal);
        Assert.Contains("(`custom_functions`.`FORMAT_MEMBER_PREFIX`(`v`.`value`, 'mapped:')) <=> ('mapped:12')",
            sql,
            StringComparison.Ordinal);

        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>A captured mapped argument remains a SQL parameter without losing the row-dependent projection.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB instead of MySQL.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_function_with_parameter_argument_retains_its_complete_correlated_projection(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var prefix = "mapped:";
        var query = context.Rows.Where(row => row
            .Values
            .Select(value => CustomPrefix(value, prefix))
            .Contains("mapped:12"));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(`r`.`Values`, '$[*]'", sql, StringComparison.Ordinal);
        Assert.Contains("(`custom_functions`.`FORMAT_MEMBER_PREFIX`(`v`.`value`, @prefix)) <=> ('mapped:12')",
            sql,
            StringComparison.Ordinal);

        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>An independent parameter rowset keeps its materializable IN form even when members use a mapped function.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB instead of MySQL.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Independent_parameter_membership_does_not_gain_a_correlated_count(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var values = new[] { 12, 13 };

        // WHY: A required candidate column keeps EF's earlier null compensation from
        // replacing IN with EXISTS; this control targets the provider's independent-rowset boundary.
        var query = context.Rows.Where(row => EF
            .Parameter(values)
            .Select(value => CustomPrefix(value, "mapped:"))
            .Contains(row.Prefix));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("WHERE (`r`.`Prefix`) IN (", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(@values", sql, StringComparison.Ordinal);
        Assert.Contains("`custom_functions`.`FORMAT_MEMBER_PREFIX`(`v`.`value`, 'mapped:')",
            sql,
            StringComparison.Ordinal);

        Assert.DoesNotContain("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>A mapped scalar argument from the outer row remains attached to its original alias in correlated membership.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB instead of MySQL.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_function_with_outer_column_argument_preserves_its_correlated_projection(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Where(row => row
            .Values
            .Select(value => CustomPrefix(value, row.Prefix))
            .Contains("mapped:12"));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("(`custom_functions`.`FORMAT_MEMBER_PREFIX`(`v`.`value`, `r`.`Prefix`)) <=> ('mapped:12')",
            sql,
            StringComparison.Ordinal);

        Assert.Contains("JSON_TABLE(`r`.`Values`, '$[*]'", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>A member-independent scalar projection still depends on the original collection containing a row.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB instead of MySQL.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_projection_without_a_member_argument_preserves_original_collection_cardinality(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var prefix = "mapped:";
        var query = context.Rows.Where(row => row
            .Values
            .Select(_ => CustomPrefix(12, prefix))
            .Contains("mapped:12"));

        // Act
        var sql = query.ToQueryString();

        // Assert
        // WHY: Even a constant projection must evaluate over the original rows; an empty
        // collection must not become a match merely because the scalar function matches.
        Assert.Contains("(`custom_functions`.`FORMAT_MEMBER_PREFIX`(12, @prefix)) <=> ('mapped:12')",
            sql,
            StringComparison.Ordinal);

        Assert.Contains("JSON_TABLE(`r`.`Values`, '$[*]'", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>A scalar builtin is evaluated for each member rather than compared against the untransformed document.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB instead of MySQL.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Builtin_absolute_value_projection_preserves_its_correlated_membership_semantics(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Where(row => row
            .Values
            .Select(value => Math.Abs(value))
            .Contains(12));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("(ABS(`v`.`value`)) <=> (12)", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(`r`.`Values`, '$[*]'", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>Real LINQ arithmetic retains both the member and outer-row operands inside correlated membership.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB instead of MySQL.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Arithmetic_projection_preserves_the_complete_member_and_outer_row_expression(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Where(row => row
            .Values
            .Select(value => value + row.Id)
            .Contains(12));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("(`v`.`value` + `r`.`Id`) <=> (12)", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(`r`.`Values`, '$[*]'", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>Provider-owned identity decoding remains eligible for the supported native or escape-safe membership path.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB instead of MySQL.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plain_text_document_membership_retains_its_provider_owned_translation(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Where(row => row.Texts.Contains("plain"));

        // Act
        var sql = query.ToQueryString();

        // Assert
        if (mariaDb)
        {
            Assert.Contains("JSON_TABLE(`r`.`Texts`, '$[*]'", sql, StringComparison.Ordinal);
            Assert.Contains("CAST(JSON_UNQUOTE(`t`.`value`) AS BINARY)", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(
                "JSON_CONTAINS(`r`.`Texts`, JSON_EXTRACT(JSON_ARRAY(CONVERT('plain' USING utf8mb4)), '$[0]'))",
                sql,
                StringComparison.Ordinal);

            Assert.DoesNotContain("JSON_TABLE(", sql, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("FROM_BASE64", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("custom_functions", sql, StringComparison.Ordinal);
    }

    /// <summary>Creates isolated provider options without registering another raw service-provider owner.</summary>
    private static FunctionIdentityContext CreateContext(
        bool mariaDb
    )
    {
        var options = MySqlFunctionalTestOptions
            .CreateTransientBuilder<FunctionIdentityContext>()
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=password;",
                mariaDb
                    ? MySqlServerVersion.MariaDb(new Version(12, 3, 0))
                    : MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .Options;

        return new FunctionIdentityContext(options);
    }

    /// <summary>Represents a mapped custom stored function whose name collides with a builtin decoder.</summary>
    private static string CustomUnquote(
        int value
    ) => throw new InvalidOperationException("This function must be translated to SQL.");

    /// <summary>Represents a mapped custom function whose name collides with a nested builtin decoder.</summary>
    private static string CustomQuote(
        int value
    ) => throw new InvalidOperationException("This function must be translated to SQL.");

    /// <summary>Represents the actual builtin JSON_UNQUOTE around a custom string projection.</summary>
    private static string BuiltinUnquote(
        string value
    ) => throw new InvalidOperationException("This function must be translated to SQL.");

    /// <summary>Represents the actual builtin JSON_UNQUOTE converting JSON numbers to text.</summary>
    private static string BuiltinUnquoteNumber(
        int value
    ) => throw new InvalidOperationException("This function must be translated to SQL.");

    /// <summary>Represents an ordinary mapped custom stored function without a builtin name collision.</summary>
    private static string CustomFormat(
        int value
    ) => throw new InvalidOperationException("This function must be translated to SQL.");

    /// <summary>Represents a mapped scalar function whose additional argument must remain part of the projection.</summary>
    private static string CustomPrefix(
        int value,
        string prefix
    ) => throw new InvalidOperationException("This function must be translated to SQL.");

    /// <summary>Maps genuine EF functions and primitive collections used by the regression queries.</summary>
    private sealed class FunctionIdentityContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public FunctionIdentityContext(
            DbContextOptions<FunctionIdentityContext> options
        ) : base(options) { }

        /// <summary>Gets the primitive-collection query root.</summary>
        public DbSet<FunctionIdentityRow> Rows => Set<FunctionIdentityRow>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<FunctionIdentityRow>(entity =>
            {
                entity.ToTable("Rows");
                entity.HasKey(row => row.Id);
                entity
                    .PrimitiveCollection(row => row.Values)
                    .HasColumnType("json");

                entity
                    .PrimitiveCollection(row => row.Texts)
                    .HasColumnType("json");
            });

            modelBuilder
                .HasDbFunction(() => CustomUnquote(default))
                .HasName("JSON_UNQUOTE")
                .HasSchema("custom_functions")
                .IsBuiltIn(false);

            modelBuilder
                .HasDbFunction(() => CustomQuote(default))
                .HasName("JSON_QUOTE")
                .HasSchema("custom_functions")
                .IsBuiltIn(false);

            modelBuilder
                .HasDbFunction(() => CustomFormat(default))
                .HasName("FORMAT_MEMBER")
                .HasSchema("custom_functions")
                .IsBuiltIn(false);

            modelBuilder
                .HasDbFunction(() => CustomPrefix(default, default!))
                .HasName("FORMAT_MEMBER_PREFIX")
                .HasSchema("custom_functions")
                .IsBuiltIn(false);

            modelBuilder
                .HasDbFunction(() => BuiltinUnquote(default!))
                .HasName("JSON_UNQUOTE")
                .IsBuiltIn();

            modelBuilder
                .HasDbFunction(() => BuiltinUnquoteNumber(default))
                .HasName("JSON_UNQUOTE")
                .IsBuiltIn();
        }
    }

    /// <summary>Stores distinguishable numeric and text JSON representations.</summary>
    private sealed class FunctionIdentityRow
    {
        /// <summary>Gets or sets the entity identifier.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets an outer-table argument which is not a JSON row-source column.</summary>
        public string Prefix { get; set; } = string.Empty;

        /// <summary>Gets or sets JSON numeric members that a custom projection may transform.</summary>
        public int[] Values { get; set; } = [];

        /// <summary>Gets or sets unchanged JSON strings for the builtin control.</summary>
        public string[] Texts { get; set; } = [];
    }
}
