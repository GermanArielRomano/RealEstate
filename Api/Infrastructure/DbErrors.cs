using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RealEstate.Infrastructure;

public static class DbErrors
{
    /// <summary>Violación de índice/clave única en PostgreSQL (SQLSTATE 23505).</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
