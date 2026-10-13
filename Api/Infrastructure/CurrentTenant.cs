using System.Security.Claims;
using RealEstate.Domain;

namespace RealEstate.Infrastructure;

public interface ICurrentTenant
{
    Guid? TenantId { get; }
    bool IsSuperAdmin { get; }
}

/// <summary>El tenant sale del JWT que emite este backend. Nunca de un header o parámetro del cliente.</summary>
public sealed class HttpCurrentTenant(IHttpContextAccessor accessor) : ICurrentTenant
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public bool IsSuperAdmin => User?.IsInRole(Roles.SuperAdmin) ?? false;

    public Guid? TenantId =>
        Guid.TryParse(User?.FindFirstValue("tenant_id"), out var id) ? id : null;
}

/// <summary>Contexto "de sistema" para arranque y seeding (no hay HttpContext): actúa como SuperAdmin.</summary>
public sealed class SystemTenant : ICurrentTenant
{
    public Guid? TenantId => null;
    public bool IsSuperAdmin => true;
}
