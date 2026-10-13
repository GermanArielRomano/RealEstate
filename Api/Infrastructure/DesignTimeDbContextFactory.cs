using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RealEstate.Infrastructure;

/// <summary>
/// Permite ejecutar `dotnet ef migrations add` SIN variables de entorno ni base de datos:
/// generar una migración no se conecta a PostgreSQL.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=design;Username=design;Password=design")
            .Options;

        return new AppDbContext(options, new SystemTenant());
    }
}
