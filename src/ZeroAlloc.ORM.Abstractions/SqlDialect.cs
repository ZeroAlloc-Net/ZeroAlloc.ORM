namespace ZeroAlloc.ORM;

/// <summary>
/// The database a repository runs against, declared with <see cref="DialectAttribute"/> so the
/// generator can apply that provider's rules at compile time.
/// </summary>
/// <remarks>
/// Each member stands for the database and the ADO.NET provider the ORM documents for it. The
/// values are fixed: the generator reads the declared value as a number.
/// </remarks>
public enum SqlDialect
{
    /// <summary>Microsoft SQL Server, through Microsoft.Data.SqlClient.</summary>
    SqlServer = 0,

    /// <summary>PostgreSQL, through Npgsql.</summary>
    PostgreSql = 1,

    /// <summary>SQLite, through Microsoft.Data.Sqlite.</summary>
    Sqlite = 2,

    /// <summary>MySQL, through MySqlConnector or MySql.Data.</summary>
    MySql = 3,
}
