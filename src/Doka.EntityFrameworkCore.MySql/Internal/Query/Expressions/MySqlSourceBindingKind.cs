namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Distinguishes native ordinary relations from the explicit shared-value source fence.</summary>
internal enum MySqlSourceBindingKind
{
    /// <summary>Retains the existing once-bound occurrences and values through an unreachable recursive arm.</summary>
    Materialized,

    /// <summary>Leaves merge and materialization decisions to the native optimizer.</summary>
    Ordinary,

    /// <summary>Combines an anchor and one finite self-reference leaf using bounded native UNION ALL recursion.</summary>
    Recursive,
}
