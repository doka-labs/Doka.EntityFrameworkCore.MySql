namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Builds provider commands while preserving EF's executable and redacted SQL representations.</summary>
internal sealed class MySqlRelationalCommandBuilder : RelationalCommandBuilder
{
    /// <summary>Initializes the immutable dependencies for this operation.</summary>
    public MySqlRelationalCommandBuilder(
        RelationalCommandBuilderDependencies dependencies
    ) : base(dependencies) { }

    /// <inheritdoc />
    public override IRelationalCommand Build()
    {
        // WHY: EF owns SQL redaction. Wrapping its cold-path result preserves both buffers
        // rather than accidentally logging executable text containing sensitive literals.
        var command = base.Build();

        return new MySqlRelationalCommand(Dependencies,
            command.CommandText,
            command.LogCommandText,
            command.Parameters);
    }
}
