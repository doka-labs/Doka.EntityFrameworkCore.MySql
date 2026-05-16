namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Identifies the MySQL-protocol family the active <see cref="EngineProfile"/>
/// targets. Drives both feature gating (every MariaDB-only feature like native
/// sequences keys off this) and syntax routing (REGEXP forms, JSON-column alias
/// shape, etc).
/// </summary>
internal enum EngineFamily
{
    MySql,
    MariaDb,
}
