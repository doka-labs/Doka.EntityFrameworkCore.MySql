namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// MySQL modification command that can propagate stored-procedure output
/// parameters returned through a synthetic result set.
/// </summary>
internal sealed class MySqlModificationCommand : ModificationCommand
{
    private readonly bool _detailedErrorsEnabled;

    public MySqlModificationCommand(
        in ModificationCommandParameters parameters
    ) : base(in parameters)
    {
        _detailedErrorsEnabled = parameters.DetailedErrorsEnabled;
    }

    public MySqlModificationCommand(
        in NonTrackedModificationCommandParameters parameters
    ) : base(in parameters) { }

    /// <summary>
    /// Propagates the provider-generated <c>SELECT @_out_...</c> row. EF's
    /// relational implementation intentionally skips output parameters until a
    /// command reader closes; MySQL exposes those values as a result set instead.
    /// </summary>
    public void PropagateStoredProcedureOutputParameters(
        RelationalDataReader reader
    )
    {
        ArgumentNullException.ThrowIfNull(reader);

        var readerIndex = 0;
        var modifications = ColumnModifications;
        var modificationCount = modifications.Count;

        // WHY: Index EF's stable output descriptors without an interface enumerator or a buffered copy.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var modificationIndex = 0; modificationIndex < modificationCount; modificationIndex++)
        {
            var modification = modifications[modificationIndex];

            if (modification.Column is not IStoreStoredProcedureParameter
                {
                    Direction: ParameterDirection.Output or ParameterDirection.InputOutput,
                })
            {
                continue;
            }

            if (modification is { IsRead: true, Property: not null, })
            {
                modification.Value = modification.Property.GetReaderFieldValue(
                    reader,
                    readerIndex,
                    _detailedErrorsEnabled);
            }

            readerIndex++;
        }
    }
}
