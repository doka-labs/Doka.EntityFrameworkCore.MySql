namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Protects boxed scalar string conversions from losing their projected CLR type.</summary>
public sealed class MySqlBoxedStringConversionTests
{
    private static readonly MethodInfo s_convertToString = typeof(Convert).GetMethod(
        nameof(Convert.ToString),
        [typeof(object)])!;

    /// <summary>Boxing must preserve the same server-side conversion as the scalar overload.</summary>
    /// <param name="mariaDb">Whether to use MariaDB translation.</param>
    /// <param name="shape">The scalar representation selected by the query.</param>
    [Theory]
    [InlineData(false, "string-enum")]
    [InlineData(true, "string-enum")]
    [InlineData(false, "numeric-enum")]
    [InlineData(true, "numeric-enum")]
    [InlineData(false, "integer")]
    [InlineData(true, "integer")]
    [InlineData(false, "boolean")]
    [InlineData(true, "boolean")]
    [InlineData(false, "string")]
    [InlineData(true, "string")]
    [InlineData(false, "nullable-integer")]
    [InlineData(true, "nullable-integer")]
    [InlineData(false, "nullable-boolean")]
    [InlineData(true, "nullable-boolean")]
    public void Boxed_scalar_conversion_compiles_a_string_projection(
        bool mariaDb,
        string shape
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = shape switch
        {
            "string-enum" => StringQuery(context.Rows, row => row.StringRole),
            "numeric-enum" => StringQuery(context.Rows, row => row.NumericRole),
            "integer" => StringQuery(context.Rows, row => row.Number),
            "boolean" => StringQuery(context.Rows, row => row.Enabled),
            "string" => StringQuery(context.Rows, row => row.Text),
            "nullable-integer" => StringQuery(context.Rows, row => row.NullableNumber),
            "nullable-boolean" => StringQuery(context.Rows, row => row.NullableEnabled),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("SELECT", sql, StringComparison.Ordinal);
        Assert.Contains("BoxedStringRows", sql, StringComparison.Ordinal);
    }

    /// <summary>A predicate requires actual SQL conversion instead of projection-only client evaluation.</summary>
    /// <param name="mariaDb">Whether to use MariaDB translation.</param>
    /// <param name="shape">The scalar representation under test.</param>
    /// <param name="expected">The canonical text compared in the predicate.</param>
    [Theory]
    [InlineData(false, "string-enum", "Admin")]
    [InlineData(true, "string-enum", "Admin")]
    [InlineData(false, "numeric-enum", "Admin")]
    [InlineData(true, "numeric-enum", "Admin")]
    [InlineData(false, "integer", "42")]
    [InlineData(true, "integer", "42")]
    [InlineData(false, "boolean", "True")]
    [InlineData(true, "boolean", "True")]
    public void Boxed_scalar_conversion_translates_in_a_predicate(
        bool mariaDb,
        string shape,
        string expected
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var projection = shape switch
        {
            "string-enum" => StringQuery(context.Rows, row => row.StringRole),
            "numeric-enum" => StringQuery(context.Rows, row => row.NumericRole),
            "integer" => StringQuery(context.Rows, row => row.Number),
            "boolean" => StringQuery(context.Rows, row => row.Enabled),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var query = projection.Where(value => value == expected);

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("WHERE", sql, StringComparison.Ordinal);
        Assert.Contains(expected, sql, StringComparison.Ordinal);
    }

    /// <summary>The object overload returns an empty string for a boxed null value.</summary>
    /// <param name="mariaDb">Whether to use MariaDB translation.</param>
    /// <param name="shape">The nullable scalar representation.</param>
    [Theory]
    [InlineData(false, "enum")]
    [InlineData(true, "enum")]
    [InlineData(false, "string")]
    [InlineData(true, "string")]
    [InlineData(false, "integer")]
    [InlineData(true, "integer")]
    public void Boxed_nullable_conversion_retains_empty_string_null_semantics(
        bool mariaDb,
        string shape
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = shape switch
        {
            "enum" => StringQuery(context.Rows, row => row.NullableRole),
            "string" => StringQuery(context.Rows, row => row.Text),
            "integer" => StringQuery(context.Rows, row => row.NullableNumber),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("COALESCE", sql, StringComparison.Ordinal);
        Assert.Contains("''", sql, StringComparison.Ordinal);
    }

    /// <summary>A string storage converter cannot supply arbitrary CLR ToString semantics.</summary>
    /// <param name="mariaDb">Whether to use MariaDB translation.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Boxed_custom_value_conversion_is_not_assumed_to_equal_its_storage_text(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = StringQuery(context.Rows, row => row.Custom)
            .Where(value => value == "prefix:value");

        // Act
        var exception = Record.Exception(query.ToQueryString);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>The GA framework's explicit string cast retains a string-converted enum's storage shape.</summary>
    /// <param name="mariaDb">Whether to use MariaDB translation.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void String_converted_enum_explicit_string_cast_compiles(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(row => (string)(object)row.StringRole);

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("StringRole", sql, StringComparison.Ordinal);
    }

    /// <summary>Builds the exact object overload without executing a culture-sensitive CLR conversion.</summary>
    private static IQueryable<string?> StringQuery<TValue>(
        IQueryable<ConversionRow> rows,
        Expression<Func<ConversionRow, TValue>> selector
    )
    {
        // WHY: This regression specifically exercises the provider's one-argument
        // object overload; an IFormatProvider overload would test another API.
        var conversion = Expression.Call(s_convertToString, Expression.Convert(selector.Body, typeof(object)));
        var projection = Expression.Lambda<Func<ConversionRow, string?>>(conversion, selector.Parameters);

        return rows.Select(projection);
    }

    private static ConversionContext CreateContext(
        bool mariaDb
    )
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<ConversionContext>();
        builder.UseMySql(
            "Server=localhost;Database=boxed_conversion;User ID=test;Password=unused;",
            mariaDb
                ? MySqlServerVersion.MariaDb(new Version(12, 3, 0))
                : MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return new ConversionContext(builder.Options);
    }

    private sealed class ConversionContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public ConversionContext(
            DbContextOptions<ConversionContext> options
        ) : base(options) { }

        public DbSet<ConversionRow> Rows => Set<ConversionRow>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<ConversionRow>();
            entity.ToTable("BoxedStringRows");
            entity
                .Property(row => row.StringRole)
                .HasConversion<string>();

            entity
                .Property(row => row.NullableRole)
                .HasConversion<string>();

            entity
                .Property(row => row.Custom)
                .HasConversion(value => value.Text, value => new CustomValue(value));
        }
    }

    private sealed class ConversionRow
    {
        public int Id { get; set; }
        public Role StringRole { get; set; }
        public Role NumericRole { get; set; }
        public Role? NullableRole { get; set; }
        public int Number { get; set; }
        public bool Enabled { get; set; }
        public string? Text { get; set; }
        public int? NullableNumber { get; set; }
        public bool? NullableEnabled { get; set; }
        public CustomValue Custom { get; set; } = new("value");
    }

    // WHY: SQL enum-name conversion needs both metadata names and their original zero/one values.
    // ReSharper disable UnusedMember.Local
    private enum Role
    {
        User,
        Admin,
    }

    // ReSharper restore UnusedMember.Local

    private sealed record CustomValue(string Text)
    {
        public override string ToString() => "prefix:" + Text;
    }
}
