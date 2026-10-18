using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RealEstate.Domain;
using RealEstate.Infrastructure;

namespace RealEstate.Api.Public;

public sealed class PublicInquiryRequest
{
    [Required, StringLength(100, MinimumLength = 2)] public string Name { get; init; } = default!;
    [EmailAddress, StringLength(200)] public string? Email { get; init; }
    [StringLength(30)] public string? Phone { get; init; }
    [StringLength(1000)] public string? Message { get; init; }

    /// <summary>Trampa para robots: un campo invisible que una persona nunca completa.</summary>
    [StringLength(200)] public string? Website { get; init; }
}

/// <summary>
/// Un comprador consulta por una propiedad (sin cuenta). Se crea el contacto y la consulta (Lead) de la inmobiliaria dueña,
/// que la ve en su panel. El tenant sale SIEMPRE de la propiedad en el servidor, nunca de lo que envía el cliente.
/// </summary>
[ApiController]
[Route("api/public/properties/{id:guid}/inquiries")]
[EnableRateLimiting("inquiry")]
public sealed class PublicInquiriesController(
    DbContextOptions<AppDbContext> options, AppDbContext readDb, ILogger<PublicInquiriesController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(Guid id, [FromBody] PublicInquiryRequest req, CancellationToken ct)
    {
        // Robot: se responde "ok" sin guardar nada, para que no sepa que fue detectado
        if (!string.IsNullOrWhiteSpace(req.Website)) return Accepted();

        var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim().ToLowerInvariant();
        var phone = Digits(req.Phone);

        if (email is null && phone is null)
        {
            ModelState.AddModelError("email", "Indica un correo o un teléfono para que puedan responderte.");
            return ValidationProblem(ModelState);
        }
        if (phone is { Length: < 7 or > 15 })
        {
            ModelState.AddModelError("phone", "El teléfono no es válido.");
            return ValidationProblem(ModelState);
        }

        // Solo propiedades visibles al público: disponibles y de una inmobiliaria activa
        var property = await readDb.Properties.AsNoTracking()
            .IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(p => p.Id == id && p.Status == PropertyStatus.Available)
            .Join(readDb.Tenants.AsNoTracking().IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(t => t.IsActive),
                p => p.TenantId, t => t.Id, (p, t) => new { p.Id, p.TenantId, p.AssignedAgentId })
            .FirstOrDefaultAsync(ct);

        if (property is null)
            return Problem(statusCode: 404, title: "La propiedad ya no está disponible.");

        // Escritura con un contexto "de sistema" y el TenantId de la propiedad (no el de un usuario: aquí no hay ninguno)
        await using var db = new AppDbContext(options, new SystemTenant());

        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.TenantId == property.TenantId &&
            ((email != null && c.Email != null && c.Email.ToLower() == email) ||
             (phone != null && c.Phone == phone)), ct);

        if (contact is null)
        {
            contact = new Contact
            {
                TenantId = property.TenantId,
                FullName = req.Name.Trim(),
                Email = email,
                Phone = phone,
                Type = ContactType.Buyer
            };
            db.Contacts.Add(contact);
        }
        else
        {
            // Mismo comprador: se completan datos que faltaban, sin pisar los existentes
            contact.Email ??= email;
            contact.Phone ??= phone;

            // Misma persona consultando lo mismo en 24 h: no se duplica (frena el reenvío accidental y el spam)
            var since = DateTimeOffset.UtcNow.AddHours(-24);
            if (await db.Leads.AnyAsync(l => l.TenantId == property.TenantId && l.PropertyId == id &&
                                             l.ContactId == contact.Id && l.CreatedAt >= since, ct))
                return Accepted();
        }

        var message = string.IsNullOrWhiteSpace(req.Message)
            ? "Hola, me interesa esta propiedad. ¿Podemos coordinar una visita?"
            : req.Message.Trim();

        db.Leads.Add(new Lead
        {
            TenantId = property.TenantId,
            PropertyId = id,
            Contact = contact,
            AssignedAgentId = property.AssignedAgentId,   // si la propiedad tiene agente, la consulta es suya
            Message = message,
            Status = LeadStatus.New
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Nueva consulta por la propiedad {PropertyId}", id);
        return Accepted();
    }

    private static string? Digits(string? raw)
    {
        var d = new string((raw ?? "").Where(char.IsDigit).ToArray());
        return d.Length == 0 ? null : d;
    }
}
