namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Identifies an inclusive endpoint of a ROWS or numeric RANGE window frame.</summary>
/// <remarks>
/// The default value denotes CURRENT ROW. Use the typed factories directly in the query;
/// capture numeric offsets rather than a whole endpoint. Offsets are nonnegative;
/// ROWS requires an integral offset. Parameterized frame offsets require MySQL.
/// </remarks>
public readonly struct MySqlWindowFrameBound : IEquatable<MySqlWindowFrameBound>
{
    private MySqlWindowFrameBound(
        MySqlWindowBoundKind kind,
        decimal offset
    )
    {
        Kind = kind;
        Offset = offset;
    }

    /// <summary>Gets the current row or its ordering peers, depending on the frame kind.</summary>
    public static MySqlWindowFrameBound CurrentRow => default;

    /// <summary>Gets the first possible position in the partition.</summary>
    public static MySqlWindowFrameBound UnboundedPreceding => new(MySqlWindowBoundKind.UnboundedPreceding, 0);

    /// <summary>Gets the last possible position in the partition.</summary>
    public static MySqlWindowFrameBound UnboundedFollowing => new(MySqlWindowBoundKind.UnboundedFollowing, 0);

    /// <summary>Creates an endpoint before the current row or numeric ordering value.</summary>
    /// <param name="offset">The nonnegative row count or numeric distance.</param>
    /// <returns>The inclusive preceding endpoint.</returns>
    public static MySqlWindowFrameBound Preceding(
        decimal offset
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        return new MySqlWindowFrameBound(MySqlWindowBoundKind.Preceding, offset);
    }

    /// <summary>Creates an endpoint after the current row or numeric ordering value.</summary>
    /// <param name="offset">The nonnegative row count or numeric distance.</param>
    /// <returns>The inclusive following endpoint.</returns>
    public static MySqlWindowFrameBound Following(
        decimal offset
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        return new MySqlWindowFrameBound(MySqlWindowBoundKind.Following, offset);
    }

    /// <summary>Gets the endpoint operation without exposing an arbitrary SQL-fragment API.</summary>
    internal MySqlWindowBoundKind Kind { get; }

    /// <summary>Gets the exact numeric offset, before dialect-specific validation.</summary>
    internal decimal Offset { get; }

    /// <inheritdoc />
    public bool Equals(
        MySqlWindowFrameBound other
    ) => Kind == other.Kind && Offset == other.Offset;

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is MySqlWindowFrameBound other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Offset);

    /// <summary>Compares two frame endpoints.</summary>
    /// <param name="left">The first endpoint.</param>
    /// <param name="right">The second endpoint.</param>
    /// <returns>Whether both endpoints describe the same operation and offset.</returns>
    public static bool operator ==(
        MySqlWindowFrameBound left,
        MySqlWindowFrameBound right
    ) => left.Equals(right);

    /// <summary>Compares two frame endpoints.</summary>
    /// <param name="left">The first endpoint.</param>
    /// <param name="right">The second endpoint.</param>
    /// <returns>Whether the endpoints differ.</returns>
    public static bool operator !=(
        MySqlWindowFrameBound left,
        MySqlWindowFrameBound right
    ) => !left.Equals(right);
}

/// <summary>Contains only native endpoint operations admitted by the typed frame contract.</summary>
internal enum MySqlWindowBoundKind
{
    CurrentRow,
    UnboundedPreceding,
    UnboundedFollowing,
    Preceding,
    Following,
}
