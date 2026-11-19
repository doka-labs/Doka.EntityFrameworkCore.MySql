namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlUpsertTests
{
    /// <summary>Native raw parameter identities remain unprefixed while SQL uses the same named tokens.</summary>
    [Fact]
    public void Native_parameter_names_preserve_raw_identity_and_prefixed_sql_tokens()
    {
        // Arrange
        var capture = new Opens();
        using var context = CreateContext(capture);
        var sql = context.GetService<ISqlGenerationHelper>();
        var shape = CreateShape(context, nameof(Row.Name));
        using var parameterFactory = new MySqlConnection().CreateCommand();
        Row[] inputs =
        [
            new()
            {
                Id = 1,
                Name = "value",
            },
        ];

        var next = 0;
        MySqlUpsertCommandBuilder.BoundRow? pending = null;
        var batch = shape.BindNextBatch(context,
            parameterFactory,
            inputs,
            ref next,
            ref pending,
            CancellationToken.None);

        var expectedNames = Enumerable
            .Range(0, batch.ParameterCount)
            .Select(index => "p" + index.ToString(CultureInfo.InvariantCulture))
            .ToArray();

        // Act
        var command = shape.BuildCommand(batch, context.GetService<IRelationalCommandBuilderFactory>(), sql);

        // Assert
        Assert.IsType<MySqlSqlGenerationHelper>(sql);
        Assert.Equal(expectedNames,
            Assert
                .Single(batch.Rows)
                .Parameters
                .Where(parameter => parameter is not null)
                .Select(parameter => parameter!.ParameterName));

        Assert.Equal(expectedNames, command.Parameters.Select(parameter => parameter.InvariantName));
        Assert.All(expectedNames, name => Assert.Contains("@" + name, command.CommandText, StringComparison.Ordinal));
        Assert.Equal(expectedNames.Length,
            command
                .Parameters
                .Select(parameter => parameter.InvariantName)
                .Distinct(StringComparer.Ordinal)
                .Count());

        Assert.Equal(0, capture.Count);
    }

    /// <summary>Replacement helpers preserve both formatting calls, even when their two representations differ.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_helper_keeps_its_parameter_and_placeholder_forms_and_call_order(
        bool differentForms
    )
    {
        // Arrange
        using var context = CreateContext(new Opens());
        var shape = CreateShape(context, nameof(Row.Name));
        var helper = new ObservedParameterHelper(context.GetService<RelationalSqlGenerationHelperDependencies>(),
            differentForms);

        using var parameterFactory = new MySqlConnection().CreateCommand();
        Row[] inputs =
        [
            new()
            {
                Id = 1,
                Name = "value",
            },
        ];

        var next = 0;
        MySqlUpsertCommandBuilder.BoundRow? pending = null;
        var batch = shape.BindNextBatch(context,
            parameterFactory,
            inputs,
            ref next,
            ref pending,
            CancellationToken.None);

        var names = Enumerable
            .Range(0, batch.ParameterCount)
            .Select(index => "p" + index.ToString(CultureInfo.InvariantCulture))
            .ToArray();

        var expectedCalls = names
            .SelectMany(name => new[] { ("name", name), ("placeholder", name) })
            .ToArray();

        // Act
        var command = shape.BuildCommand(batch, context.GetService<IRelationalCommandBuilderFactory>(), helper);

        // Assert
        Assert.Equal(expectedCalls, helper.Calls);
        Assert.Equal(names.Select(name => (differentForms
                    ? "$"
                    : "@")
                + name),
            Assert
                .Single(batch.Rows)
                .Parameters
                .Where(parameter => parameter is not null)
                .Select(parameter => parameter!.ParameterName));

        Assert.Equal(names, command.Parameters.Select(parameter => parameter.InvariantName));
        Assert.All(names,
            name => Assert.Contains((differentForms
                    ? ":"
                    : "@")
                + name,
                command.CommandText,
                StringComparison.Ordinal));
    }

    /// <summary>Observes the two caller-facing formatting hooks independently of their internal delegation.</summary>
    private sealed class ObservedParameterHelper : RelationalSqlGenerationHelper
    {
        private readonly bool _differentForms;

        /// <summary>Initializes the test helper with its supplied dependencies and values.</summary>
        public ObservedParameterHelper(
            RelationalSqlGenerationHelperDependencies dependencies,
            bool differentForms
        ) : base(dependencies)
        {
            _differentForms = differentForms;
        }

        internal List<(string Operation, string Name)> Calls { get; } = [];

        /// <inheritdoc />
        public override string GenerateParameterName(
            string name
        )
        {
            Calls.Add(("name", name));

            return _differentForms
                ? "$" + name
                : base.GenerateParameterName(name);
        }

        /// <inheritdoc />
        public override string GenerateParameterNamePlaceholder(
            string name
        )
        {
            Calls.Add(("placeholder", name));

            // WHY: Track only the external placeholder hook. Calling the base placeholder
            // would virtually re-enter the name hook and obscure the caller's actual sequence.
            return _differentForms
                ? ":" + name
                : base.GenerateParameterName(name);
        }
    }
}
