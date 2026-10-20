namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Reconstructs concrete relational annotations for provider-owned quoted nodes.</summary>
internal static class MySqlRelationalExpressionQuoting
{
    private static readonly ConstructorInfo s_annotationConstructor = typeof(Annotation)
        .GetConstructor([typeof(string), typeof(object)])!;

    private static readonly ConstructorInfo s_pairConstructor = typeof(KeyValuePair<string, IAnnotation>)
        .GetConstructor([typeof(string), typeof(IAnnotation)])!;

    private static readonly ConstructorInfo s_dictionaryConstructor = typeof(Dictionary<string, IAnnotation>)
        .GetConstructor([typeof(IEnumerable<KeyValuePair<string, IAnnotation>>)])!;

    private static readonly MethodInfo s_pairAdd = typeof(List<KeyValuePair<string, IAnnotation>>)
        .GetMethod(nameof(List<>.Add))!;

    /// <summary>Preserves actual annotation values without quoting their IAnnotation wrappers.</summary>
    public static Expression QuoteAnnotations(
        IReadOnlyDictionary<string, IAnnotation>? annotations
    )
    {
        if (annotations is null
            || annotations.Count == 0)
        {
            return Expression.Constant(null, typeof(IReadOnlyDictionary<string, IAnnotation>));
        }

        // WHY: EF 10 cannot quote an interface constructor or translate a two-argument dictionary
        // ListInit to C#. A concrete constructor over pairs preserves actual values in both paths.
        return Expression.New(
            s_dictionaryConstructor,
            Expression.ListInit(
                Expression.New(typeof(List<KeyValuePair<string, IAnnotation>>)),
                annotations.Select(annotation => Expression.ElementInit(
                    s_pairAdd,
                    Expression.New(
                        s_pairConstructor,
                        Expression.Constant(annotation.Key),
                        Expression.New(
                            s_annotationConstructor,
                            Expression.Constant(annotation.Key),
                            Expression.Constant(annotation.Value.Value, typeof(object))))))));
    }
}
