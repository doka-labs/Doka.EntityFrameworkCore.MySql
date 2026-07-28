namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlModelCodeGenerator : IModelCodeGenerator
{
    private static readonly Regex s_dbSetPropertyRegex = DbSetPropertyRegex();

    private readonly IModelCodeGenerator _innerGenerator;
    private readonly ICSharpHelper _csharpHelper;

    public MySqlModelCodeGenerator(
        IModelCodeGenerator innerGenerator,
        ICSharpHelper csharpHelper
    )
    {
        _innerGenerator = innerGenerator ?? throw new ArgumentNullException(nameof(innerGenerator));
        _csharpHelper = csharpHelper ?? throw new ArgumentNullException(nameof(csharpHelper));
    }

    public string Language => _innerGenerator.Language ?? "C#";

    public ScaffoldedModel GenerateModel(
        IModel model,
        ModelCodeGenerationOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);

        var scaffoldedModel = _innerGenerator.GenerateModel(model, options);

        scaffoldedModel.ContextFile.Code = EnsureProviderNamespaceUsing(
            scaffoldedModel.ContextFile.Code);
        scaffoldedModel.ContextFile.Code = RewriteDbSetProperties(scaffoldedModel.ContextFile.Code);
        scaffoldedModel.ContextFile.Code = AddMissingRelationalConfiguration(
            scaffoldedModel.ContextFile.Code,
            model);

        if (options.UseNullableReferenceTypes)
        {
            scaffoldedModel.ContextFile.Code = EnsureNullableEnable(scaffoldedModel.ContextFile.Code);

            foreach (var additionalFile in scaffoldedModel.AdditionalFiles)
            {
                additionalFile.Code = EnsureNullableEnable(additionalFile.Code);
            }
        }

        return scaffoldedModel;
    }

    private static string EnsureProviderNamespaceUsing(
        string code
    )
    {
        const string providerUsing = "using Doka.EntityFrameworkCore.MySql;";

        if (code.Contains(providerUsing, StringComparison.Ordinal))
        {
            return code;
        }

        var newline = code.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        var insertionIndex = code.IndexOf("using Doka.", StringComparison.Ordinal);

        if (insertionIndex < 0)
        {
            insertionIndex = code.IndexOf("using Microsoft.", StringComparison.Ordinal);
        }

        if (insertionIndex < 0)
        {
            insertionIndex = code.IndexOf("using ", StringComparison.Ordinal);
        }

        if (insertionIndex < 0)
        {
            throw new InvalidOperationException(
                "The generated DbContext does not contain a using-directive insertion point.");
        }

        return code.Insert(insertionIndex, providerUsing + newline);
    }

    private string AddMissingRelationalConfiguration(
        string code,
        IModel model
    )
    {
        var entityConfigurations = model
            .GetEntityTypes()
            .Select(entityType => new
            {
                EntityType = entityType,
                PrimaryKey = entityType.FindPrimaryKey(),
                AlternateKeys = entityType
                    .GetKeys()
                    .Where(key => !key.IsPrimaryKey())
                    .OrderBy(key => key.GetName(), StringComparer.Ordinal)
                    .ToArray(),
                CheckConstraints = entityType
                    .GetCheckConstraints()
                    .OrderBy(checkConstraint => checkConstraint.Name, StringComparer.Ordinal)
                    .ToArray(),
                ColumnOrders = entityType
                    .GetProperties()
                    .Select(property => new
                    {
                        Property = property,
                        Order = property.GetColumnOrder(),
                    })
                    .Where(configuration => configuration.Order is not null)
                    .OrderBy(configuration => configuration.Order)
                    .ToArray(),
            })
            .Where(configuration => configuration.PrimaryKey?.GetName() is not null
                || configuration.AlternateKeys.Length > 0
                || configuration.CheckConstraints.Length > 0
                || configuration.ColumnOrders.Length > 0)
            .OrderBy(configuration => configuration.EntityType.Name, StringComparer.Ordinal)
            .ToArray();

        if (entityConfigurations.Length == 0)
        {
            return code;
        }

        const string insertionMarker = "        OnModelCreatingPartial(modelBuilder);";
        var insertionIndex = code.LastIndexOf(insertionMarker, StringComparison.Ordinal);

        if (insertionIndex < 0)
        {
            throw new InvalidOperationException(
                "The generated DbContext does not contain the expected OnModelCreating insertion point.");
        }

        var newline = code.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        var configurationCode = new StringBuilder();

        foreach (var configuration in entityConfigurations)
        {
            var primaryKeyName = configuration.PrimaryKey?.GetName();

            foreach (var columnOrder in configuration.ColumnOrders)
            {
                if (HasColumnOrderConfiguration(
                        code,
                        configuration.EntityType.Name,
                        columnOrder.Property.Name,
                        columnOrder.Order!.Value))
                {
                    continue;
                }

                configurationCode
                    .Append("        modelBuilder.Entity<")
                    .Append(configuration.EntityType.Name)
                    .Append(">()")
                    .Append(newline)
                    .Append("            .Property(e => e.")
                    .Append(columnOrder.Property.Name)
                    .Append(')')
                    .Append(newline)
                    .Append("            .HasColumnOrder(")
                    .Append(columnOrder.Order.Value)
                    .Append(");")
                    .Append(newline)
                    .Append(newline);
            }

            if (configuration.PrimaryKey is not null
                && primaryKeyName is not null
                && !code.Contains($".HasName({_csharpHelper.Literal(primaryKeyName)})", StringComparison.Ordinal))
            {
                configurationCode
                    .Append("        modelBuilder.Entity<")
                    .Append(configuration.EntityType.Name)
                    .Append(">()")
                    .Append(newline)
                    .Append("            .HasKey(")
                    .Append(_csharpHelper.Lambda(configuration.PrimaryKey.Properties, "e"))
                    .Append(')')
                    .Append(newline)
                    .Append("            .HasName(")
                    .Append(_csharpHelper.Literal(primaryKeyName))
                    .Append(");")
                    .Append(newline)
                    .Append(newline);
            }

            foreach (var alternateKey in configuration.AlternateKeys)
            {
                configurationCode
                    .Append("        modelBuilder.Entity<")
                    .Append(configuration.EntityType.Name)
                    .Append(">()")
                    .Append(newline)
                    .Append("            .HasAlternateKey(")
                    .Append(_csharpHelper.Lambda(alternateKey.Properties, "e"))
                    .Append(')')
                    .Append(newline)
                    .Append("            .HasName(")
                    .Append(_csharpHelper.Literal(alternateKey.GetName()))
                    .Append(");")
                    .Append(newline)
                    .Append(newline);
            }

            if (configuration.CheckConstraints.Length == 0)
            {
                continue;
            }

            configurationCode
                .Append("        modelBuilder.Entity<")
                .Append(configuration.EntityType.Name)
                .Append(">()")
                .Append(newline)
                .Append("            .ToTable(tableBuilder =>")
                .Append(newline)
                .Append("            {")
                .Append(newline);

            foreach (var checkConstraint in configuration.CheckConstraints)
            {
                configurationCode
                    .Append("                tableBuilder.HasCheckConstraint(")
                    .Append(_csharpHelper.Literal(checkConstraint.Name))
                    .Append(", ")
                    .Append(_csharpHelper.Literal(checkConstraint.Sql))
                    .Append(");")
                    .Append(newline);
            }

            configurationCode
                .Append("            });")
                .Append(newline)
                .Append(newline);
        }

        return code.Insert(insertionIndex, configurationCode.ToString());
    }

    private static bool HasColumnOrderConfiguration(
        string code,
        string entityTypeName,
        string propertyName,
        int columnOrder
    )
    {
        var entityMarker = $"modelBuilder.Entity<{entityTypeName}>(entity =>";
        var entityStart = code.IndexOf(entityMarker, StringComparison.Ordinal);

        if (entityStart < 0)
        {
            return false;
        }

        const string entityEndMarker = "        });";
        var entityEnd = code.IndexOf(entityEndMarker, entityStart, StringComparison.Ordinal);

        if (entityEnd < 0)
        {
            return false;
        }

        var propertyMarker = $"entity.Property(e => e.{propertyName})";
        var propertyStart = code.IndexOf(propertyMarker, entityStart, StringComparison.Ordinal);

        if (propertyStart < 0
            || propertyStart >= entityEnd)
        {
            return false;
        }

        var statementEnd = code.IndexOf(';', propertyStart);

        if (statementEnd < 0
            || statementEnd >= entityEnd)
        {
            return false;
        }

        return code
            .AsSpan(propertyStart, statementEnd - propertyStart)
            .Contains($".HasColumnOrder({columnOrder})", StringComparison.Ordinal);
    }

    private static string EnsureNullableEnable(
        string code
    )
    {
        ArgumentNullException.ThrowIfNull(code);

        const string nullableEnableDirective = "#nullable enable";

        if (code.StartsWith(nullableEnableDirective, StringComparison.Ordinal))
        {
            return code;
        }

        return nullableEnableDirective + Environment.NewLine + Environment.NewLine + code;
    }

    private static string RewriteDbSetProperties(
        string code
    )
    {
        ArgumentNullException.ThrowIfNull(code);

        return s_dbSetPropertyRegex.Replace(
            code,
            static match =>
            {
                var indent = match.Groups["indent"].Value;
                var entity = match.Groups["entity"].Value;
                var name = match.Groups["name"].Value;

                return FormattableString.Invariant(
                    $"{indent}public virtual DbSet<{entity}> {name} => Set<{entity}>();");
            });
    }

    [GeneratedRegex(
        @"^(?<indent>\s*)public\s+virtual\s+DbSet<(?<entity>[^>]+)>\s+(?<name>\w+)\s+\{\s*get;\s*set;\s*\}\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex DbSetPropertyRegex();
}
