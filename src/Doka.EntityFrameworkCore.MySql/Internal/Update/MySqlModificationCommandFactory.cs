namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Creates MySQL-aware modification commands for tracked and non-tracked
/// update pipelines.
/// </summary>
internal sealed class MySqlModificationCommandFactory : IModificationCommandFactory
{
    /// <inheritdoc />
    public IModificationCommand CreateModificationCommand(
        in ModificationCommandParameters parameters
    ) => new MySqlModificationCommand(in parameters);

    /// <inheritdoc />
    public INonTrackedModificationCommand CreateNonTrackedModificationCommand(
        in NonTrackedModificationCommandParameters parameters
    ) => new MySqlModificationCommand(in parameters);
}
