namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies actual owned and primitive JSON contracts under enabled application text defaults.</summary>
public sealed partial class MySqlDefaultTextModelTests
{
    /// <summary>Owned JSON and serialized primitive collections retain their respective established storage contracts.</summary>
    /// <param name="mariaDb">Whether to inspect the MariaDB JSON alias instead of native MySQL JSON.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enabled_text_defaults_preserve_owned_and_primitive_json_storage(
        bool mariaDb
    )
    {
        // Arrange
        using var context = new JsonDefaultsContext(SpecialTypeOptions<JsonDefaultsContext>(mariaDb));
        using var control =
            new JsonDefaultsContext(SpecialTypeOptions<JsonDefaultsContext>(mariaDb, textDefaults: false));

        var controlScript = control.Database.GenerateCreateScript();
        var controlOwned = ColumnDefinition(controlScript, nameof(JsonDefaultsRow.Owned));
        var controlPrimitive = ColumnDefinition(controlScript, nameof(JsonDefaultsRow.Tags));

        // Act
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        var script = context.Database.GenerateCreateScript();
        var ownedColumn = ColumnDefinition(script, nameof(JsonDefaultsRow.Owned));
        var primitiveColumn = ColumnDefinition(script, nameof(JsonDefaultsRow.Tags));

        // Assert
        Assert.Equal("utf8mb4", model.GetMySqlCharSet());
        Assert.Equal("utf8mb4_unicode_ci", model.GetCollation());
        Assert.DoesNotContain("utf8mb4_unicode_ci", ownedColumn, StringComparison.Ordinal);
        Assert.DoesNotContain("utf8mb4_unicode_ci", primitiveColumn, StringComparison.Ordinal);
        Assert.Equal(controlOwned, ownedColumn);
        Assert.Equal(controlPrimitive, primitiveColumn);

        // WHY: Unannotated primitive collections already use serialized LONGTEXT on both engines;
        // the text-default feature must preserve that contract rather than invent native JSON metadata.
        Assert.Contains("longtext", primitiveColumn, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JSON_VALID(`Tags`)", script, StringComparison.OrdinalIgnoreCase);
        if (mariaDb)
        {
            Assert.Contains("longtext", ownedColumn, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("utf8mb4_bin", ownedColumn, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("JSON_VALID(`Owned`)", script, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains(" json", ownedColumn, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("COLLATE", ownedColumn, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("COLLATE", primitiveColumn, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("JSON_VALID", script, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Owned ToJson scalar access remains a translated JSON expression with model defaults enabled.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB rather than MySQL.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enabled_text_defaults_preserve_owned_json_query_translation(
        bool mariaDb
    )
    {
        // Arrange
        using var context = new JsonDefaultsContext(SpecialTypeOptions<JsonDefaultsContext>(mariaDb));
        var query = context
            .Set<JsonDefaultsRow>()
            .Where(row => row.Owned.Label == "owner")
            .Select(row => row.Owned.Label);

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("JSON_EXTRACT(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$.Label", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("utf8mb4_unicode_ci", sql, StringComparison.Ordinal);
    }

    /// <summary>Primitive string membership retains server-side JSON translation without a per-query text override.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB rather than MySQL.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enabled_text_defaults_preserve_primitive_collection_query_translation(
        bool mariaDb
    )
    {
        // Arrange
        using var context = new JsonDefaultsContext(SpecialTypeOptions<JsonDefaultsContext>(mariaDb));
        var query = context
            .Set<JsonDefaultsRow>()
            .Where(row => row.Tags.Contains("alpha"))
            .Select(row => row.Id);

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("`Tags`", sql, StringComparison.Ordinal);
        Assert.True(sql.Contains("JSON_CONTAINS(", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("JSON_TABLE(", StringComparison.OrdinalIgnoreCase),
            sql);

        Assert.DoesNotContain("utf8mb4_unicode_ci", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COLLATE", sql, StringComparison.OrdinalIgnoreCase);
    }
}
