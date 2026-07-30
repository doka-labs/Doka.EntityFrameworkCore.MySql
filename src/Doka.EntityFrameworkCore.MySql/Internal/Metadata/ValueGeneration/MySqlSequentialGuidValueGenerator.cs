namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Generates time-ordered UUIDv7 values for client-generated GUID properties.
/// Their RFC 4122 byte order remains monotonic when stored through the provider's
/// default <c>binary(16)</c> mapping, reducing random B-tree page splits.
/// </summary>
internal sealed class MySqlSequentialGuidValueGenerator : ValueGenerator<Guid>
{
    /// <inheritdoc />
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc />
    public override Guid Next(
        EntityEntry entry
    )
    {
        ArgumentNullException.ThrowIfNull(entry);

        return Guid.CreateVersion7();
    }
}
