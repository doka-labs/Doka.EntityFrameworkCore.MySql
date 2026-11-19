namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Resolves application text fallbacks without replacing explicit schema intent.</summary>
internal static class MySqlTextDefaults
{
    private const string RuntimeReceipt = MySqlAnnotationNames.Prefix + "RuntimeTextDefaults";
    private const string RuntimeCharSet = MySqlAnnotationNames.Prefix + "RuntimeTextCharSet";
    private const string RuntimeCollation = MySqlAnnotationNames.Prefix + "RuntimeTextCollation";
    private const string RuntimeExplicitCharSet = MySqlAnnotationNames.Prefix + "RuntimeExplicitTextCharSet";
    private const string RuntimeExplicitCollation = MySqlAnnotationNames.Prefix + "RuntimeExplicitTextCollation";

    /// <summary>Reads retained model facets without treating them as explicit column overrides.</summary>
    internal static (string? CharSet, string? Collation) GetModelFacets(
        IReadOnlyModel model
    ) => (
        model.FindAnnotation(MySqlAnnotationNames.CharSet)
            ?.Value as string
        ?? model.FindAnnotation(RuntimeCharSet)
            ?.Value as string,
        model.FindAnnotation(RelationalAnnotationNames.Collation)
            ?.Value as string
        ?? model.FindAnnotation(RuntimeCollation)
            ?.Value as string);

    /// <summary>Checks the final pair using the same charset aliases supported by schema generation.</summary>
    public static void ValidatePair(
        string? charSet,
        string? collation,
        ProviderProfile profile
    )
    {
        if (!IsCompatible(charSet, collation, profile))
        {
            throw new InvalidOperationException(
                "The configured character set and collation do not identify the same MySQL character set.");
        }
    }

    /// <summary>Tests a pair without inventing a missing facet or querying the server.</summary>
    public static bool IsCompatible(
        string? charSet,
        string? collation,
        ProviderProfile profile
    )
    {
        if (charSet is null
            || collation is null)
        {
            return true;
        }

        var separator = collation.IndexOf('_');

        // WHY: Options validation runs for each context. Inspect the prefix without allocating
        // a substring; measured complete validation retains the same alias rules with zero prefix bytes.
        var collationCharSet = separator > 0
            ? collation.AsSpan(0, separator)
            : collation.AsSpan();

        if (collationCharSet.Equals(charSet.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if ((charSet.Equals("utf8", StringComparison.OrdinalIgnoreCase)
                && collationCharSet.Equals("utf8mb3", StringComparison.OrdinalIgnoreCase))
            || (charSet.Equals("utf8mb3", StringComparison.OrdinalIgnoreCase)
                && collationCharSet.Equals("utf8", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // WHY: Shared UCA-14 names intentionally have no single charset prefix on qualified MariaDB versions.
        return profile.Engine.Has(EngineCapability.SharedUca1400Collations)
            && collation.StartsWith("uca1400_", StringComparison.OrdinalIgnoreCase)
            && (charSet.Equals("ucs2", StringComparison.OrdinalIgnoreCase)
                || charSet.Equals("utf8", StringComparison.OrdinalIgnoreCase)
                || charSet.Equals("utf8mb3", StringComparison.OrdinalIgnoreCase)
                || charSet.Equals("utf8mb4", StringComparison.OrdinalIgnoreCase)
                || charSet.Equals("utf16", StringComparison.OrdinalIgnoreCase)
                || charSet.Equals("utf32", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Retains stronger facets and applies only compatible missing fallbacks.</summary>
    public static (string? CharSet, string? Collation) Resolve(
        string? defaultCharSet,
        string? defaultCollation,
        string? charSet,
        string? collation,
        ProviderProfile profile
    )
    {
        ValidatePair(defaultCharSet, defaultCollation, profile);
        ValidatePair(charSet, collation, profile);

        if (charSet is null
            && collation is null)
        {
            return (defaultCharSet, defaultCollation);
        }

        return (
            charSet
            ?? (IsCompatible(defaultCharSet, collation, profile)
                ? defaultCharSet
                : null),
            collation
            ?? (IsCompatible(charSet, defaultCollation, profile)
                ? defaultCollation
                : null));
    }

    /// <summary>Copies effective values and their precedence solely to the runtime representation.</summary>
    public static void AddRuntimeAnnotations(
        IDictionary<string, object?> annotations,
        IModel model
    )
    {
        if (model.FindAnnotation(MySqlAnnotationNames.CharSet) is null
            && model.FindAnnotation(RelationalAnnotationNames.Collation) is null)
        {
            return;
        }

        // WHY: EF removes design-only collation metadata. A primitive runtime receipt lets UseModel and
        // compiled models prove compatibility without retaining a second schema authority in snapshots.
        annotations[RuntimeReceipt] = true;
        AddOptional(annotations,
            RuntimeCharSet,
            model.FindAnnotation(MySqlAnnotationNames.CharSet)
                ?.Value);

        AddOptional(annotations,
            RuntimeCollation,
            model.FindAnnotation(RelationalAnnotationNames.Collation)
                ?.Value);

        annotations[RuntimeExplicitCharSet] = IsExplicit(model, MySqlAnnotationNames.CharSet);
        annotations[RuntimeExplicitCollation] = IsExplicit(model, RelationalAnnotationNames.Collation);
    }

    /// <summary>Rejects supplied models that cannot represent the requested fallbacks without rebuilding.</summary>
    public static void ValidateSuppliedModel(
        IModel model,
        string? defaultCharSet,
        string? defaultCollation,
        ProviderProfile profile
    )
    {
        if (defaultCharSet is null
            && defaultCollation is null)
        {
            return;
        }

        var runtime = model is RuntimeModel;
        if (runtime
            && model.FindAnnotation(RuntimeReceipt)
                ?.Value is not true)
        {
            throw new InvalidOperationException(
                "The supplied MySQL model cannot verify application text defaults. Regenerate the model with matching provider options.");
        }

        var charSet = GetString(model,
            runtime
                ? RuntimeCharSet
                : MySqlAnnotationNames.CharSet);

        var collation = GetString(model,
            runtime
                ? RuntimeCollation
                : RelationalAnnotationNames.Collation);

        var explicitCharSet = runtime
            ? model.FindAnnotation(RuntimeExplicitCharSet)
                ?.Value is true
            : IsExplicit(model, MySqlAnnotationNames.CharSet);

        var explicitCollation = runtime
            ? model.FindAnnotation(RuntimeExplicitCollation)
                ?.Value is true
            : IsExplicit(model, RelationalAnnotationNames.Collation);

        var expected = Resolve(defaultCharSet,
            defaultCollation,
            explicitCharSet
                ? charSet
                : null,
            explicitCollation
                ? collation
                : null,
            profile);

        if (!string.Equals(charSet, expected.CharSet, StringComparison.Ordinal)
            || !string.Equals(collation, expected.Collation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The supplied MySQL model has incompatible application text defaults. Regenerate the model with matching provider options.");
        }
    }

    private static bool IsExplicit(
        IModel model,
        string name
    )
    {
        var annotation = model.FindAnnotation(name);

        return annotation is not null
            && (annotation is not IConventionAnnotation convention
                || convention.GetConfigurationSource() != ConfigurationSource.Convention);
    }

    private static string? GetString(
        IModel model,
        string name
    ) => model.FindAnnotation(name)
        ?.Value as string;

    private static void AddOptional(
        IDictionary<string, object?> annotations,
        string name,
        object? value
    )
    {
        if (value is not null)
        {
            annotations[name] = value;
        }
    }
}
