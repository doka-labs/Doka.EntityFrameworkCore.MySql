namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Creates unique CLR identifiers from database object names.
/// </summary>
internal sealed partial class MySqlScaffoldingModelFactory
{
    private static string CreateUniqueClrIdentifier(
        string databaseIdentifier,
        HashSet<string> usedNames,
        string fallbackRoot
    )
    {
        var candidate = SanitizeToPascalCase(databaseIdentifier);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = fallbackRoot;
        }

        if (!char.IsLetter(candidate[0])
            && candidate[0] != '_')
        {
            candidate = "_" + candidate;
        }

        var uniqueCandidate = candidate;
        var suffix = 1;

        while (!usedNames.Add(uniqueCandidate))
        {
            uniqueCandidate = candidate + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }

        return uniqueCandidate;
    }

    private static string SanitizeToPascalCase(
        string value
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var nextUpper = true;

        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                nextUpper = true;
                continue;
            }

            builder.Append(nextUpper ? char.ToUpperInvariant(character) : character);
            nextUpper = false;
        }

        return builder.ToString();
    }
}
