namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Reconstructs one quoted source graph with local, shared definition variables.</summary>
[Experimental("EF9100")]
internal static class MySqlMaterializedSourceQuoting
{
    private static readonly ConstructorInfo s_definitionConstructor =
        typeof(MySqlMaterializedSourceDefinition).GetConstructor([typeof(SelectExpression)])!;

    private static readonly ConstructorInfo s_referenceConstructor =
        typeof(MySqlMaterializedSourceExpression).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(string),
                typeof(int),
                typeof(MySqlMaterializedSourceDefinition),
                typeof(IReadOnlyDictionary<string, IAnnotation>)
            ],
            modifiers: null)!;

    private static readonly MethodInfo s_invoke =
        typeof(Func<MySqlMaterializedSourceExpression>).GetMethod(nameof(Func<>.Invoke))!;

    private static readonly ConstructorInfo s_annotationConstructor =
        typeof(Annotation).GetConstructor([typeof(string), typeof(object)])!;

    private static readonly MethodInfo s_annotationAdd =
        typeof(Dictionary<string, IAnnotation>).GetMethod(nameof(Dictionary<,>.Add))!;

    /// <summary>Quotes a source DAG without retaining a global cache or duplicating its definitions.</summary>
    public static Expression Quote(
        MySqlMaterializedSourceExpression expression
    )
    {
        var builder = new QuotationBuilder();
        var reference = builder.CreateReference(expression);
        var body = Expression.Block(builder.Variables, builder.Initializers.Append(reference));
        var factory = Expression.Lambda<Func<MySqlMaterializedSourceExpression>>(body);

        // WHY: EF's C# translator lifts a bare block or inlines Expression.Invoke(lambda). The
        // delegate method call keeps declaration statements inside this one reconstruction scope.
        return Expression.Call(Expression.Convert(factory, typeof(Func<MySqlMaterializedSourceExpression>)), s_invoke);
    }

    /// <summary>Reconstructs concrete annotation storage while preserving the original values and nullability.</summary>
    private static Expression QuoteAnnotations(
        IReadOnlyDictionary<string, IAnnotation> annotations
    )
    {
        if (annotations.Count == 0)
        {
            return Expression.Constant(null, typeof(IReadOnlyDictionary<string, IAnnotation>));
        }

        // WHY: EF 10's helper looks for an interface constructor and quotes each IAnnotation
        // wrapper as its value. Construct the concrete dictionary and the actual object values.
        return Expression.ListInit(
            Expression.New(typeof(Dictionary<string, IAnnotation>)),
            annotations.Select(annotation => Expression.ElementInit(
                s_annotationAdd,
                Expression.Constant(annotation.Key),
                Expression.New(
                    s_annotationConstructor,
                    Expression.Constant(annotation.Key),
                    Expression.Constant(annotation.Value.Value, typeof(object))))));
    }

    /// <summary>Owns local definition identity for exactly one top-level quotation.</summary>
    private sealed class QuotationBuilder : ExpressionVisitor
    {
        private readonly Dictionary<MySqlMaterializedSourceDefinition, DefinitionVariable> _definitions = [];

        /// <summary>Gets variables declared in the reconstruction scope.</summary>
        public List<ParameterExpression> Variables { get; } = [];

        /// <summary>Gets dependency-ordered definition initializers.</summary>
        public List<Expression> Initializers { get; } = [];

        /// <summary>References a shared local definition, constructing its initializer only once.</summary>
        public NewExpression CreateReference(
            MySqlMaterializedSourceExpression expression
        )
        {
            if (!_definitions.TryGetValue(expression.Definition, out var definition))
            {
                var variable = Expression.Variable(
                    typeof(MySqlMaterializedSourceDefinition),
                    "sourceDefinition" + _definitions.Count.ToString(CultureInfo.InvariantCulture));

                definition = new DefinitionVariable(variable);
                _definitions.Add(expression.Definition, definition);
                Variables.Add(variable);
                var source = (SelectExpression)Visit(expression.Source);
                Initializers.Add(Expression.Assign(variable, Expression.New(s_definitionConstructor, source.Quote())));
                definition.Initialized = true;
            }
            else if (!definition.Initialized)
            {
                throw new InvalidOperationException(
                    "Materialized source quotation requires an acyclic definition graph.");
            }

            return Expression.New(
                s_referenceConstructor,
                Expression.Constant(expression.Alias),
                Expression.Constant(expression.BindingId),
                definition.Variable,
                QuoteAnnotations(
                    expression
                        .GetAnnotations()
                        .ToDictionary(annotation => annotation.Name, StringComparer.Ordinal)));
        }

        /// <inheritdoc />
        protected override Expression VisitExtension(
            Expression node
        ) => node is MySqlMaterializedSourceExpression reference
            ? new QuotedReference(reference, CreateReference(reference))
            : base.VisitExtension(node);
    }

    /// <summary>Tracks initialization so invalid cyclic inputs cannot reference an unassigned definition.</summary>
    private sealed class DefinitionVariable(ParameterExpression variable)
    {
        /// <summary>Gets the definition's local variable.</summary>
        public ParameterExpression Variable { get; } = variable;

        /// <summary>Gets or sets whether all dependency initializers have been emitted.</summary>
        public bool Initialized { get; set; }
    }

    /// <summary>Substitutes a local reference only while EF quotes an otherwise ordinary immutable SELECT.</summary>
    private sealed class QuotedReference : TableExpressionBase
    {
        private readonly NewExpression _quote;

        /// <summary>Initializes a temporary quotation proxy with the original SQL alias and annotations.</summary>
        public QuotedReference(
            MySqlMaterializedSourceExpression source,
            NewExpression quote
        ) : base(source.Alias, source.GetAnnotations())
        {
            _quote = quote;
        }

        private QuotedReference(
            string? alias,
            IReadOnlyDictionary<string, IAnnotation> annotations,
            NewExpression quote
        ) : base(alias, annotations)
        {
            _quote = quote;
        }

        /// <inheritdoc />
        public override Expression Quote() => _quote;

        /// <inheritdoc />
        public override TableExpressionBase Clone(
            string? alias,
            ExpressionVisitor cloningExpressionVisitor
        ) => WithAlias(alias ?? Alias!);

        /// <inheritdoc />
        public override TableExpressionBase WithAlias(
            string newAlias
        ) => new QuotedReference(
            newAlias,
            GetAnnotations()
                .ToDictionary(annotation => annotation.Name, StringComparer.Ordinal),
            _quote.Update([Expression.Constant(newAlias), .. _quote.Arguments.Skip(1)]));

        /// <inheritdoc />
        protected override TableExpressionBase WithAnnotations(
            IReadOnlyDictionary<string, IAnnotation> annotations
        ) => new QuotedReference(
            Alias,
            annotations,
            _quote.Update([.. _quote.Arguments.Take(3), QuoteAnnotations(annotations)]));

        /// <inheritdoc />
        protected override void Print(
            ExpressionPrinter expressionPrinter
        ) => expressionPrinter
            .Append("QuotedMaterializedSource(")
            .Append(Alias!)
            .Append(")");
    }
}
