namespace Doka.EntityFrameworkCore.MySql;

public static partial class MySqlDbFunctionsExtensions
{
    /// <summary>Starts an expression-only SQL window definition.</summary>
    /// <param name="functions">The EF query function marker.</param>
    /// <returns>A window expression that is translated on the server.</returns>
    /// <remarks>No client implementation, implicit result ordering or materialization is provided.</remarks>
    public static MySqlWindowDefinition Window(

        // WHY: EF otherwise parameterizes this structural singleton receiver. Keeping it constant
        // preserves the exact marker and lets translation reject a captured NULL before database I/O.
        [NotParameterized] this DbFunctions functions
    )
    {
        ArgumentNullException.ThrowIfNull(functions);

        throw MySqlWindowDefinition.ClientEvaluation();
    }
}
