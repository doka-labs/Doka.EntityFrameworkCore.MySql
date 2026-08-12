namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Owns the exact EF Core 10 built-in operation types reserved for provider
/// dispatch. Keeping the set explicit avoids reflection and prevents an
/// external handler from shadowing provider DDL.
/// </summary>
internal static class MySqlStandardMigrationOperations
{
    private static readonly FrozenSet<Type> s_types = new HashSet<Type>
    {
        typeof(AddColumnOperation),
        typeof(AddForeignKeyOperation),
        typeof(AddPrimaryKeyOperation),
        typeof(AddUniqueConstraintOperation),
        typeof(AlterColumnOperation),
        typeof(AlterDatabaseOperation),
        typeof(AlterSequenceOperation),
        typeof(AlterTableOperation),
        typeof(AddCheckConstraintOperation),
        typeof(CreateIndexOperation),
        typeof(CreateSequenceOperation),
        typeof(CreateTableOperation),
        typeof(DropColumnOperation),
        typeof(DropForeignKeyOperation),
        typeof(DropIndexOperation),
        typeof(DropPrimaryKeyOperation),
        typeof(DropSchemaOperation),
        typeof(DropSequenceOperation),
        typeof(DropTableOperation),
        typeof(DropUniqueConstraintOperation),
        typeof(DropCheckConstraintOperation),
        typeof(EnsureSchemaOperation),
        typeof(RenameColumnOperation),
        typeof(RenameIndexOperation),
        typeof(RenameSequenceOperation),
        typeof(RenameTableOperation),
        typeof(RestartSequenceOperation),
        typeof(SqlOperation),
        typeof(InsertDataOperation),
        typeof(DeleteDataOperation),
        typeof(UpdateDataOperation),
    }.ToFrozenSet();

    public static bool Contains(
        Type operationType
    ) => s_types.Contains(operationType);

    internal static IReadOnlySet<Type> Types => s_types;
}
