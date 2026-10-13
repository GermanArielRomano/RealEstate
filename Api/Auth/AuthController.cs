using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RealEstate.Domain;
using RealEstate.Infrastructure;

namespace RealEstate.Api.Auth;

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; init; } = default!;
    [Required, StringLength(100)] public string Password { get; init; } = default!;
}

public sealed class ChangePasswordRequest
{
    [Required, StringLength(100)] public string CurrentPassword { get; init; } = default!;
    [Required, StringLength(100, MinimumLength = 8)] public string NewPassword { get; init; } = default!;
}

public sealed record UserInfo(Guid Id, string Email, string FullName, IReadOnlyList<string> Roles, Guid? TenantId, string? TenantName);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserInfo User);

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(UserManager<AppUser> users, AppDbContext db, JwtTokenService tokens) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(req.Email.Trim());

        // Mensaje único para usuario inexistente / inactivo / clave incorrecta (no revela qué cuentas existen)
        if (user is null || !user.IsActive)
            return InvalidCredentials();

        if (await users.IsLockedOutAsync(user))
            return Problem(statusCode: 429, title: "Cuenta bloqueada temporalmente",
                detail: "Demasiados intentos fallidos. Inténtalo de nuevo en unos minutos.");

        if (!await users.CheckPasswordAsync(user, req.Password))
        {
            await users.AccessFailedAsync(user);
            return InvalidCredentials();
        }

        await users.ResetAccessFailedCountAsync(user);

        string? tenantName = null;
        if (user.TenantId is { } tenantId)
        {
            // El login ocurre ANTES de conocer el tenant: se ignora solo el filtro de tenant
            var tenant = await db.Tenants.IgnoreQueryFilters([AppDbContext.TenantFilter])
                .AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);

            if (tenant is null || !tenant.IsActive)
                return Problem(statusCode: 403, title: "Inmobiliaria desactivada",
                    detail: "Tu inmobiliaria está desactivada. Contacta al administrador de la plataforma.");

            tenantName = tenant.Name;
        }

        var roles = (await users.GetRolesAsync(user)).ToList();
        var (token, expiresAt) = tokens.Create(user, roles);

        return Ok(new LoginResponse(token, expiresAt,
            new UserInfo(user.Id, user.Email ?? "", user.FullName, roles, user.TenantId, tenantName)));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var user = await users.FindByIdAsync(User.FindFirstValue("sub") ?? "");
        if (user is null) return Unauthorized();

        var result = await users.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(e.Code, e.Description);
            return ValidationProblem(ModelState);
        }

        return NoContent();
    }

    private ObjectResult InvalidCredentials() =>
        Problem(statusCode: 401, title: "Credenciales inválidas", detail: "Correo o contraseña incorrectos.");
}
