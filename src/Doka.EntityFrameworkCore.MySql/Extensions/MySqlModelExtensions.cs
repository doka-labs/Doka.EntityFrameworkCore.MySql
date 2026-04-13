namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlModelExtensions
{
    public static string? GetMySqlCharSet(
        this IReadOnlyModel model
    )
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.FindAnnotation(MySqlAnnotationNames.CharSet)?.Value as string;
    }

    public static void SetMySqlCharSet(
        this IMutableModel model,
        string? charSet
    )
    {
        ArgumentNullException.ThrowIfNull(model);

        if (string.IsNullOrWhiteSpace(charSet))
        {
            model.RemoveAnnotation(MySqlAnnotationNames.CharSet);
            return;
        }

        model.SetAnnotation(MySqlAnnotationNames.CharSet, charSet);
    }
}
