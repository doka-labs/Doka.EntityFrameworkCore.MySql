global using System;
global using System.Threading;
global using Doka.EntityFrameworkCore.MySql;
global using Doka.EntityFrameworkCore.MySql.Examples;
global using Microsoft.EntityFrameworkCore;
global using MySqlConnector;

// WHY: The sample selects Doka's model representation; the driver's transport enum has the same name.
global using MySqlGuidFormat = Doka.EntityFrameworkCore.MySql.MySqlGuidFormat;
