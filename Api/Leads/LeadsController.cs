using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstate.Api.Properties;
using RealEstate.Domain;
using RealEstate.Infrastructure;

namespace RealEstate.Api.Leads;

public sealed record LeadListItemDto(
    Guid Id, DateTimeOffset CreatedAt, LeadStatus Status, string? Message,
    Guid PropertyId, string PropertyCode, string PropertyTitle,
    string ContactName, string? ContactEmail, string? ContactPhone, string? AssignedAgentName);

public sealed record LeadSummaryDto(int NewCount);

public sealed class SetLeadStatusRequest
{
    [EnumDataType(typeof(LeadStatus))] public LeadStatus Status { get; init; }
}

/// <summary>
/// Consultas recibidas. El filtro global de tenant garantiza que cada inmobiliaria vea solo las suyas;
/// un agente, además, solo las que tiene asignadas.
/// </summary>
[ApiController]
[Route("api/leads")]
[Authorize(Roles = $"{Roles.TenantAdmin},{Roles.Agent}")]
[Produces("application/json")]
public sealed class LeadsController(AppDbContext db) : ControllerBase
{
    private Guid? CurrentUserId => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;
    private bool IsAdmin => User.IsInRole(Roles.TenantAdmin);

    private IQueryable<Lead> Visible()
    {
        var query = db.Leads.AsNoTracking();
        if (IsAdmin) return query;

        var me = CurrentUserId;
        return query.Where(l => l.AssignedAgentId == me);
    }

    private bool CanManage(Lead lead) => IsAdmin || (lead.AssignedAgentId is not null && lead.AssignedAgentId == CurrentUserId);

    [HttpGet]
    public async Task<ActionResult<PagedResult<LeadListItemDto>>> List(
        [FromQuery] LeadStatus? status,
        [FromQuery, Range(1, 100_000)] int page = 1,
        [FromQuery, Range(1, 50)] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = Visible();
        if (status is { } s) query = query.Where(l => l.Status == s);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new LeadListItemDto(
                l.Id, l.CreatedAt, l.Status, l.Message,
                l.PropertyId, l.Property.Code, l.Property.Title,
                l.Contact.FullName, l.Contact.Email, l.Contact.Phone,
                l.AssignedAgent != null ? l.AssignedAgent.FullName : null))
            .ToListAsync(ct);

        return new PagedResult<LeadListItemDto>(items, page, pageSize, total);
    }

    /// <summary>Cantidad de consultas nuevas: es el numerito de la barra del panel.</summary>
    [HttpGet("summary")]
    public async Task<LeadSummaryDto> Summary(CancellationToken ct) =>
        new(await Visible().CountAsync(l => l.Status == LeadStatus.New, ct));

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetLeadStatusRequest req, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return NotFound();
        if (!CanManage(lead)) return Forbid();

        lead.Status = req.Status;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Borrado lógico (útil para limpiar consultas basura).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return NotFound();
        if (!CanManage(lead)) return Forbid();

        db.Leads.Remove(lead);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
