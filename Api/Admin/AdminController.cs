using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstate.Domain;
using RealEstate.Infrastructure;

namespace RealEstate.Api.Admin;

/// <summary>Gestión de inmobiliarias (socios). Solo para el SuperAdmin de la plataforma.</summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = Roles.SuperAdmin)]
[Produces("application/json")]
public sealed class AdminController(AppDbContext db, UserManager<AppUser> users) : ControllerBase
{
    // ---------------------------------------------------------------
    //  Inmobiliarias
    // ---------------------------------------------------------------
    [HttpGet("tenants")]
    public async Task<ActionResult<List<TenantDto>>> ListTenants([FromQuery] string? search, CancellationToken ct)
    {
        IQueryable<Tenant> query = db.Tenants;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(t => t.Name.ToLower().Contains(s) || t.Slug.Contains(s));
        }

        // Filtrar y ordenar ANTES de proyectar al DTO (EF no traduce el orden sobre el DTO)
        return await Project(query.OrderByDescending(t => t.CreatedAt).Take(200)).ToListAsync(ct);
    }

    [HttpGet("tenants/{id:guid}")]
    public async Task<ActionResult<TenantDto>> GetTenant(Guid id, CancellationToken ct)
    {
        var tenant = await Project(db.Tenants.Where(t => t.Id == id)).FirstOrDefaultAsync(ct);
        return tenant is null ? NotFound() : tenant;
    }

    /// <summary>Crea la inmobiliaria y su primer usuario (TenantAdmin) en una sola transacción.</summary>
    [HttpPost("tenants")]
    [ProducesResponseType<TenantCreatedDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest req, CancellationToken ct)
    {
        var slug = string.IsNullOrWhiteSpace(req.Slug)
            ? await UniqueSlugAsync(Slugify(req.Name), ct)
            : req.Slug.Trim();

        if (await db.Tenants.AnyAsync(t => t.Slug == slug, ct))
            return Problem(statusCode: 409, title: "Identificador duplicado",
                detail: $"Ya existe una inmobiliaria con el identificador '{slug}'.");

        var email = req.AdminEmail.Trim();
        if (await users.FindByEmailAsync(email) is not null)
            return Problem(statusCode: 409, title: "Correo en uso",
                detail: "Ya existe un usuario con ese correo.");

        // Si algo falla antes del Commit, la transacción se revierte al salir
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var tenant = new Tenant
        {
            Name = req.Name.Trim(),
            Slug = slug,
            TaxId = req.TaxId?.Trim(),
            Plan = req.Plan
        };
        db.Tenants.Add(tenant);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return Problem(statusCode: 409, title: "Identificador duplicado");
        }

        var admin = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = req.AdminFullName.Trim(),
            TenantId = tenant.Id,
            IsActive = true
        };

        var created = await users.CreateAsync(admin, req.AdminPassword);
        if (!created.Succeeded) return IdentityProblem(created);

        var inRole = await users.AddToRoleAsync(admin, Roles.TenantAdmin);
        if (!inRole.Succeeded) return IdentityProblem(inRole);

        await tx.CommitAsync(ct);

        var dto = new TenantDto(tenant.Id, tenant.Name, tenant.Slug, tenant.TaxId, tenant.Plan,
            tenant.IsActive, tenant.CreatedAt, PropertyCount: 0, UserCount: 1, WhatsApp: tenant.WhatsApp);

        return CreatedAtAction(nameof(GetTenant), new { id = tenant.Id }, new TenantCreatedDto(dto, admin.Id));
    }

    [HttpPut("tenants/{id:guid}")]
    public async Task<IActionResult> UpdateTenant(Guid id, [FromBody] UpdateTenantRequest req, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        tenant.Name = req.Name.Trim();
        tenant.TaxId = req.TaxId?.Trim();
        tenant.Plan = req.Plan;
        tenant.WhatsApp = req.WhatsApp?.Trim();
        tenant.IsActive = req.IsActive; // desactivar impide nuevos logins y nuevas altas (los tokens vigentes caducan solos)

        await db.SaveChangesAsync(ct);
        return Ok(await Project(db.Tenants.Where(t => t.Id == id)).FirstAsync(ct));
    }

    // ---------------------------------------------------------------
    //  Usuarios de una inmobiliaria
    // ---------------------------------------------------------------
    [HttpGet("tenants/{id:guid}/users")]
    public async Task<ActionResult<List<TenantUserDto>>> TenantUsers(Guid id, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return NotFound();

        // AppUser no tiene filtro global: se filtra por TenantId de forma explícita
        return await db.Users.AsNoTracking()
            .Where(u => u.TenantId == id)
            .OrderBy(u => u.FullName)
            .Select(u => new TenantUserDto(
                u.Id, u.Email ?? "", u.FullName, u.IsActive,
                (from ur in db.UserRoles
                 join r in db.Roles on ur.RoleId equals r.Id
                 where ur.UserId == u.Id
                 select r.Name).FirstOrDefault()))
            .ToListAsync(ct);
    }

    [HttpPost("users/{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest req)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        // Se valida ANTES de quitar la clave actual para no dejar al usuario sin contraseña
        foreach (var validator in users.PasswordValidators)
        {
            var check = await validator.ValidateAsync(users, user, req.NewPassword);
            if (!check.Succeeded) return IdentityProblem(check);
        }

        if (await users.HasPasswordAsync(user))
        {
            var removed = await users.RemovePasswordAsync(user);
            if (!removed.Succeeded) return IdentityProblem(removed);
        }

        var added = await users.AddPasswordAsync(user, req.NewPassword);
        if (!added.Succeeded) return IdentityProblem(added);

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return NoContent();
    }

    [HttpPut("users/{id:guid}/active")]
    public async Task<IActionResult> SetUserActive(Guid id, [FromBody] SetActiveRequest req)
    {
        if (id.ToString() == User.FindFirstValue("sub") && !req.IsActive)
            return Problem(statusCode: 400, title: "No puedes desactivar tu propia cuenta.");

        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        user.IsActive = req.IsActive;
        var result = await users.UpdateAsync(user);
        return result.Succeeded ? NoContent() : IdentityProblem(result);
    }

    // ---------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------
    private IQueryable<TenantDto> Project(IQueryable<Tenant> source) =>
        source.AsNoTracking().Select(t => new TenantDto(
            t.Id, t.Name, t.Slug, t.TaxId, t.Plan, t.IsActive, t.CreatedAt,
            db.Properties.Count(p => p.TenantId == t.Id),   // excluye borradas (filtro SoftDelete)
            db.Users.Count(u => u.TenantId == t.Id),
            t.WhatsApp));

    private ActionResult IdentityProblem(IdentityResult result)
    {
        foreach (var e in result.Errors) ModelState.AddModelError(e.Code, e.Description);
        return ValidationProblem(ModelState);
    }

    private async Task<string> UniqueSlugAsync(string baseSlug, CancellationToken ct)
    {
        for (var n = 1; n <= 50; n++)
        {
            var candidate = n == 1 ? baseSlug : $"{baseSlug}-{n}";
            if (!await db.Tenants.AnyAsync(t => t.Slug == candidate, ct)) return candidate;
        }
        return $"{baseSlug}-{Guid.NewGuid():N}"[..Math.Min(baseSlug.Length + 9, 100)];
    }

    /// <summary>Sin string.Normalize (el backend corre con InvariantGlobalization).</summary>
    private static string Slugify(string name)
    {
        const string from = "áàäâéèëêíìïîóòöôúùüûñç";
        const string to = "aaaaeeeeiiiioooouuuunc";

        var sb = new StringBuilder();
        foreach (var ch in name.ToLowerInvariant())
        {
            var i = from.IndexOf(ch);
            var c = i >= 0 ? to[i] : ch;

            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length == 0) return "inmobiliaria";
        return slug[..Math.Min(slug.Length, 60)].TrimEnd('-');
    }
}
